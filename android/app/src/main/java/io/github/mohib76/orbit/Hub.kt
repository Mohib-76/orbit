package io.github.mohib76.orbit

import android.annotation.SuppressLint
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.os.ParcelFileDescriptor
import android.provider.OpenableColumns
import android.util.Base64
import android.util.Log
import io.github.mohib76.orbit.buds.Buds
import io.github.mohib76.orbit.buds.BudsService
import io.github.mohib76.orbit.buds.BudsTileService
import io.github.mohib76.orbit.media.Media
import io.github.mohib76.orbit.mirror.Mirror
import io.github.mohib76.orbit.mirror.MirrorTileService
import io.github.mohib76.orbit.remote.Links
import io.github.mohib76.orbit.remote.Remote
import io.github.mohib76.orbit.remote.RemoteInput
import io.github.mohib76.orbit.usb.Usb
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.update
import org.json.JSONObject
import java.util.UUID

enum class Conn { NotPaired, Waiting, Connected }
data class LinkUi(val conn: Conn = Conn.NotPaired, val pcName: String = "", val link: String = "", val since: Long = 0)
data class ClipEntry(val text: String, val fromPc: Boolean, val time: Long = System.currentTimeMillis())

enum class TState { Queued, Waiting, Running, Paused, Done, Failed, Cancelled }
data class TransferItem(
    val id: String, val name: String, val size: Long, val outgoing: Boolean,
    val done: Long = 0, val state: TState = TState.Queued, val speed: Double = 0.0,
    val savedUri: Uri? = null, val time: Long = System.currentTimeMillis()
) {
    val progress: Float get() = if (size <= 0) (if (state == TState.Done) 1f else 0f) else (done.toFloat() / size).coerceIn(0f, 1f)
    val active: Boolean get() = state == TState.Queued || state == TState.Running || state == TState.Paused || state == TState.Waiting
}

/** Pairing details from the PC's QR code (or handed over by the PC through USB). */
data class PairOffer(val pcId: String, val pcName: String, val pcFp: String, val token: String, val expires: Long)

/** App-wide state and actions. The UI observes the flows; the service and server feed them. */
@SuppressLint("StaticFieldLeak")
object Hub {
    const val TAG = "Orbit"
    lateinit var app: Context
    lateinit var prefs: Prefs
    private val main = Handler(Looper.getMainLooper())

    val ui = MutableStateFlow(LinkUi())
    val clips = MutableStateFlow<List<ClipEntry>>(emptyList())
    val transfers = MutableStateFlow<List<TransferItem>>(emptyList())
    /** A scanned/handed-over QR waiting for the user to tap Pair. */
    val offer = MutableStateFlow<PairOffer?>(null)
    /** Confirmed by the user; the PC will connect and prove it holds the same token. */
    @Volatile var accepted: PairOffer? = null
    val settingsVersion = MutableStateFlow(0)
    /** "system", "light" or "dark" (Settings → Theme). */
    val theme = MutableStateFlow("system")

    /** Empty when both apps speak the same protocol; otherwise which side needs updating. */
    val versionWarning = MutableStateFlow("")
    @Volatile var peerAppVersion = ""

    fun checkPeerVersion(b: JSONObject) {
        // PCs from before the version handshake send nothing: treat them as protocol 1.
        val proto = b.optInt("proto", 1)
        peerAppVersion = b.optString("app")
        versionWarning.value = when {
            proto < Proto.VERSION -> "Orbit on your PC is older than on this phone. Update it so every feature works."
            proto > Proto.VERSION -> "Orbit on this phone is older than on your PC. Update it so every feature works."
            else -> ""
        }
    }

    @Volatile var link: Link? = null

    /** Open file descriptors for outgoing files (survive the share sheet's URI grant ending). */
    internal val sources = HashMap<String, ParcelFileDescriptor>()

    fun init(ctx: Context) {
        app = ctx.applicationContext
        prefs = Prefs(app)
        Identity.init(app)
        theme.value = prefs.theme
        prefs.pc?.let { ui.value = LinkUi(Conn.Waiting, it.name) }
        Buds.init(app)
        Media.init(app)
        Usb.init(app)
        Links.publishShareTarget(app)
    }

    // ------------------------------------------------------------------ pairing

    fun parsePairPayload(raw: String): PairOffer? = try {
        val d = Uri.parse(raw).getQueryParameter("d") ?: raw
        val json = JSONObject(String(Base64.decode(d, Base64.URL_SAFE or Base64.NO_PADDING or Base64.NO_WRAP)))
        PairOffer(json.getString("id"), json.optString("name", "PC"), json.getString("fp"), json.getString("token"),
            System.currentTimeMillis() + 5 * 60_000)
    } catch (e: Exception) {
        Log.w(TAG, "bad pair payload", e); null
    }

    fun acceptOffer(o: PairOffer) {
        accepted = o
        offer.value = null
        ui.update { it.copy(conn = Conn.Waiting, pcName = o.pcName) }
        OrbitService.start(app)
    }

    fun onPaired(pc: PairedPc) {
        prefs.pc = pc
        accepted = null
        ui.update { it.copy(pcName = pc.name) }
        Links.publishShareTarget(app)
    }

    fun unpair() {
        link?.send("orbit.unpair")
        link?.close()
        prefs.pc = null
        ui.value = LinkUi(Conn.NotPaired)
        Links.publishShareTarget(app)
    }

    fun renamePc(name: String) {
        val pc = prefs.pc ?: return
        prefs.pc = pc.copy(name = name)
        ui.update { it.copy(pcName = name) }
        Links.publishShareTarget(app)
    }

    // ------------------------------------------------------------------ link state

    fun onConnected(l: Link) {
        val old = link
        link = l
        if (old != null && old !== l) old.close()
        ui.value = LinkUi(Conn.Connected, prefs.pc?.name ?: "PC", l.kind, System.currentTimeMillis())
        OrbitService.instance?.onLinkUp(l)
        MirrorTileService.refresh(app)
        NotifListener.instance?.sendActive(l)
        main.post { Buds.publish() }
        Media.onConnected()
        Usb.send()
        // Re-offer outgoing files that were interrupted.
        transfers.value.filter { it.outgoing && (it.state == TState.Queued || it.state == TState.Paused) }.forEach { offerFile(it) }
    }

    fun onDisconnected(l: Link) {
        if (link !== l) return
        link = null
        main.post { Buds.onPeerGone() }
        Media.onPeerGone()
        Usb.onPeerGone()
        Mirror.onLinkGone()
        Remote.onPeerGone()
        RemoteInput.onPeerGone()
        ui.update { if (prefs.pc != null) it.copy(conn = Conn.Waiting) else LinkUi(Conn.NotPaired) }
        transfers.update { list -> list.map { if (it.state == TState.Running) it.copy(state = TState.Paused) else it } }
        OrbitService.instance?.refreshNotification()
    }

    fun handle(l: Link, type: String, b: JSONObject) {
        when (type) {
            "orbit.ping" -> l.send("orbit.pong")
            "orbit.pong" -> {}
            "orbit.clipboard" -> onRemoteClipboard(b.optString("text"))
            "orbit.notification.dismiss" -> NotifListener.instance?.dismiss(b.optString("key"))
            "orbit.file.offer" -> Transfers.onOffer(b)
            "orbit.file.accept" -> Transfers.onAccept(b)
            "orbit.file.done" -> Transfers.onDone(b)
            "orbit.file.cancel" -> Transfers.onCancel(b)
            "orbit.buds.state" -> main.post { Buds.onPeerState(b) }
            "orbit.buds.cmd" -> main.post { Buds.onPeerCommand(b) }
            "orbit.media" -> main.post { Buds.onPeerMedia(b.optString("action")) }
            "orbit.media.state" -> Media.onPeerState(b)
            "orbit.media.art" -> Media.onPeerArt(b)
            "orbit.media.cmd" -> Media.onCommand(b)
            "orbit.usb.control" -> Usb.onControl(b)
            "orbit.mirror.state" -> Mirror.onState(b)
            "orbit.mirror.prepare" -> Mirror.onPrepare(l)
            "orbit.mirror.setup" -> Mirror.onSetupRequested(l)
            "orbit.url" -> Links.onUrl(b)
            "orbit.remote.caps" -> Remote.onCaps(b)
            "orbit.volume.state" -> Remote.onVolume(b)
            "orbit.remote.result" -> Remote.onResult(b)
            "orbit.focus" -> RemoteInput.onFocus(b)
            "orbit.settings" -> {
                // Older PCs only send autoUsbTethering.
                val usbMode = b.optString("usbMode").ifEmpty {
                    if (b.has("autoUsbTethering")) (if (b.getBoolean("autoUsbTethering")) Usb.TETHERING else Usb.CHARGING) else ""
                }
                if (usbMode in Usb.MODES) prefs.usbMode = usbMode
                if (b.has("mediaWanted")) Media.onPeerWants(b.getBoolean("mediaWanted"))
                if (b.has("budsEnabled") && b.getBoolean("budsEnabled") != prefs.budsEnabled)
                    main.post { setBudsEnabled(b.getBoolean("budsEnabled"), fromPeer = true) }
                val name = b.optString("pcName")
                if (name.isNotEmpty()) prefs.pc?.let { if (it.name != name) renamePc(name) }
                settingsVersion.update { it + 1 }
            }
            "orbit.unpair" -> {
                prefs.pc = null
                l.close()
                ui.value = LinkUi(Conn.NotPaired)
                Links.publishShareTarget(app)
            }
            else -> Log.d(TAG, "unknown packet $type")
        }
    }

    fun pushSettings() {
        link?.send("orbit.settings", JSONObject()
            .put("autoUsbTethering", prefs.usbMode == Usb.TETHERING)
            .put("usbMode", prefs.usbMode)
            .put("budsEnabled", prefs.budsEnabled)
            .put("mediaWanted", prefs.showPcMedia))
    }

    /** The Earbuds toggle. Synced with the PC so one switch turns the feature on or off everywhere. */
    fun setBudsEnabled(on: Boolean, fromPeer: Boolean = false) {
        prefs.budsEnabled = on
        if (on) {
            Buds.autoSelect()
            BudsService.start(app)
            Buds.connect()
        } else {
            BudsService.stop(app)
            Buds.disconnect()
        }
        BudsTileService.refresh(app)
        Buds.schedulePublish()
        if (!fromPeer) pushSettings()
        settingsVersion.update { it + 1 }
    }

    // ------------------------------------------------------------------ clipboard

    @Volatile private var lastRemoteClip: String? = null

    private fun onRemoteClipboard(text: String) {
        if (text.isEmpty()) return
        lastRemoteClip = text
        addClip(ClipEntry(text, fromPc = true))
        if (!prefs.clipboardAutoReceive) return
        main.post {
            val cm = app.getSystemService(ClipboardManager::class.java)
            cm.setPrimaryClip(ClipData.newPlainText("Orbit", text))
        }
    }

    /** Returns false if not connected. */
    fun sendClipboard(text: String): Boolean {
        val l = link ?: return false
        if (text.isEmpty()) return false
        lastRemoteClip = text
        l.send("orbit.clipboard", JSONObject().put("text", text))
        addClip(ClipEntry(text, fromPc = false))
        return true
    }

    private fun addClip(c: ClipEntry) {
        clips.update { list -> (listOf(c) + list.filter { it.text != c.text }).take(10) }
    }

    // ------------------------------------------------------------------ files

    fun sendFiles(uris: List<Uri>) {
        val cr = app.contentResolver
        for (uri in uris) {
            try {
                var name = uri.lastPathSegment ?: "file"
                var size = -1L
                cr.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)?.use { c ->
                    if (c.moveToFirst()) {
                        c.getString(0)?.let { name = it }
                        if (!c.isNull(1)) size = c.getLong(1)
                    }
                }
                val pfd = cr.openFileDescriptor(uri, "r") ?: continue
                if (size < 0) size = pfd.statSize
                val t = TransferItem(UUID.randomUUID().toString().replace("-", ""), name, size, outgoing = true)
                synchronized(sources) { sources[t.id] = pfd }
                transfers.update { listOf(t) + it }
                offerFile(t)
            } catch (e: Exception) {
                Log.w(TAG, "cannot read $uri", e)
            }
        }
        OrbitService.start(app)
    }

    internal fun offerFile(t: TransferItem) {
        link?.send("orbit.file.offer", JSONObject().put("id", t.id).put("name", t.name).put("size", t.size))
    }

    fun updateTransfer(id: String, f: (TransferItem) -> TransferItem) {
        transfers.update { list -> list.map { if (it.id == id) f(it) else it } }
    }

    fun clearFinished() = transfers.update { list -> list.filter { it.active } }
}
