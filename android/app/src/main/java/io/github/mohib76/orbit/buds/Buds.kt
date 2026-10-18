package io.github.mohib76.orbit.buds

import android.Manifest
import android.annotation.SuppressLint
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothSocket
import android.content.Context
import android.content.pm.PackageManager
import android.media.AudioManager
import android.os.Handler
import android.os.HandlerThread
import android.os.Looper
import android.os.SystemClock
import android.util.Log
import android.view.KeyEvent
import io.github.mohib76.orbit.Hub
import kotlinx.coroutines.flow.MutableStateFlow
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import kotlin.concurrent.thread

data class BudsState(
    val connected: Boolean = false,
    val name: String = "Earbuds",
    val mac: String? = null,
    val anc: AncMode? = null,
    /** Bitmask of AncMode codes cycled by the noise-control touch action; 0 = unknown. */
    val cycleMask: Int = 0,
    val battery: List<Int> = listOf(-1, -1, -1),
    val charging: List<Boolean> = listOf(false, false, false),
    /** Only features the buds reported, so unsupported ones never show up. */
    val features: Map<Feature, Boolean> = emptyMap(),
    val touch: Map<TouchKey, Int> = emptyMap(),
)

/** What the PC reports about its own link to the buds (and whether it is playing audio). */
data class PeerBuds(val enabled: Boolean, val state: BudsState, val playing: Boolean)

/**
 * The phone's connection to the earbuds (RFCOMM), plus the glue to the PC:
 * both sides publish their own view over the Orbit link, and when only one of them is connected to the
 * buds the other relays its commands through it.
 */
@SuppressLint("MissingPermission") // every Bluetooth call is gated on hasPermission()
object Buds {
    private const val TAG = "OrbitBuds"
    private const val RESPONSE_TIMEOUT_MS = 1000L

    private lateinit var app: Context
    private val main = Handler(Looper.getMainLooper())
    private val worker by lazy { Handler(HandlerThread("buds-worker").apply { start() }.looper) }

    @Volatile private var socket: BluetoothSocket? = null
    private var seq = 0
    private val responseLock = Object()
    private var awaiting = -1
    private var responded = false

    /** This phone's own link to the buds. */
    val local = MutableStateFlow(BudsState())
    /** The PC's view, received over Orbit. */
    val peer = MutableStateFlow<PeerBuds?>(null)
    /** Whether this phone is playing audio. */
    val playingHere = MutableStateFlow(false)

    fun init(ctx: Context) {
        if (::app.isInitialized) return
        app = ctx.applicationContext
        local.value = BudsState(mac = Hub.prefs.budsMac, name = Hub.prefs.budsName ?: "Earbuds")
        main.post(pollPlaying)
    }

    val enabled get() = Hub.prefs.budsEnabled

    fun hasPermission(ctx: Context = app) =
        ctx.checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED

    /** The state to show: ours if we're connected to the buds, else the PC's if it is. */
    fun effective(l: BudsState = local.value, p: PeerBuds? = peer.value): BudsState =
        if (l.connected || p == null || !p.state.connected) l else p.state

    private fun update(f: BudsState.() -> BudsState) = main.post {
        local.value = local.value.f()
        schedulePublish()
    }

    // ---------------------------------------------------------------- devices

    fun bondedBuds(): List<BluetoothDevice> {
        if (!hasPermission()) return emptyList()
        val devices = app.getSystemService(BluetoothManager::class.java)?.adapter?.bondedDevices ?: return emptyList()
        return devices.sortedWith(compareByDescending<BluetoothDevice> { isConnected(it) }
            .thenByDescending { it.name?.contains("buds", true) == true })
    }

    fun isConnected(d: BluetoothDevice): Boolean = try {
        d.javaClass.getMethod("isConnected").invoke(d) as Boolean
    } catch (_: Exception) { false }

    /** Picks the buds automatically: the connected "…Buds…" device, else the only one. */
    fun autoSelect(): Boolean {
        if (Hub.prefs.budsMac != null) return true
        val list = bondedBuds().filter { it.name?.contains("buds", true) == true }
        val pick = list.firstOrNull { isConnected(it) } ?: list.singleOrNull() ?: return false
        select(pick)
        return true
    }

    fun select(d: BluetoothDevice) {
        Hub.prefs.budsMac = d.address
        Hub.prefs.budsName = d.name
        local.value = BudsState(mac = d.address, name = d.name ?: "Earbuds")
        worker.post { closeSocket(); doConnect(1) }
        schedulePublish()
    }

    // ---------------------------------------------------------------- commands (main thread)

    fun connect(attempts: Int = 1, delayMs: Long = 0) {
        if (!enabled) return
        worker.postDelayed({ doConnect(attempts) }, delayMs)
    }

    fun disconnect() = worker.post { closeSocket() }

    fun setAnc(mode: AncMode) = route(JSONObject().put("op", "anc").put("mode", mode.name)) {
        if (local.value.connected) update { copy(anc = mode) } // optimistic; ACK / push corrects it
        command { send(BudsProtocol.ANC_SET, BudsProtocol.ancSet(BudsProtocol.ANC_TYPE_MODE, mode.code)) }
    }

    fun cycleAnc() = setAnc((effective().anc ?: AncMode.OFF).next())

    fun setCycle(mask: Int) = route(JSONObject().put("op", "cycle").put("mask", mask)) {
        update { copy(cycleMask = mask) }
        command {
            send(BudsProtocol.ANC_SET, BudsProtocol.ancSet(BudsProtocol.ANC_TYPE_CYCLE, mask))
            send(BudsProtocol.ANC_REQ, BudsProtocol.ancReq(BudsProtocol.ANC_TYPE_CYCLE))
        }
    }

    fun setFeature(f: Feature, on: Boolean) = route(JSONObject().put("op", "feature").put("id", f.id).put("on", on)) {
        update { copy(features = features + (f to on)) }
        command {
            send(BudsProtocol.MISC_SET, BudsProtocol.miscSet(f.id, on))
            send(BudsProtocol.MISC_REQ, BudsProtocol.miscReq(f.id))
        }
    }

    fun setTouch(key: TouchKey, action: TouchAction) =
        route(JSONObject().put("op", "touch").put("side", key.side).put("gesture", key.gesture).put("action", action.code)) {
            update { copy(touch = touch + (key to action.code)) }
            command {
                send(BudsProtocol.TOUCH_SET, BudsProtocol.touchSet(key, action.code))
                send(BudsProtocol.TOUCH_REQ, BudsProtocol.touchReq())
            }
        }

    /** Run locally if we hold the buds; otherwise ask the PC to do it; otherwise try to connect. */
    private inline fun route(remote: JSONObject, localAction: () -> Unit) {
        val p = peer.value
        when {
            local.value.connected -> localAction()
            p != null && p.state.connected && Hub.link != null -> {
                peer.value = p.copy(state = p.state.applyOptimistic(remote))
                Hub.link?.send("orbit.buds.cmd", remote)
            }
            else -> { connect(); localAction() }
        }
    }

    private fun BudsState.applyOptimistic(op: JSONObject): BudsState = when (op.optString("op")) {
        "anc" -> copy(anc = runCatching { AncMode.valueOf(op.getString("mode")) }.getOrNull() ?: anc)
        "feature" -> Feature.fromId(op.getInt("id"))?.let { copy(features = features + (it to op.getBoolean("on"))) } ?: this
        "touch" -> copy(touch = touch + (TouchKey(op.getInt("side"), op.getInt("gesture")) to op.getInt("action")))
        "cycle" -> copy(cycleMask = op.getInt("mask"))
        else -> this
    }

    /** A command relayed from the PC. */
    fun onPeerCommand(op: JSONObject) {
        if (!local.value.connected) return
        when (op.optString("op")) {
            "anc" -> runCatching { AncMode.valueOf(op.getString("mode")) }.getOrNull()?.let { setAnc(it) }
            "feature" -> Feature.fromId(op.getInt("id"))?.let { setFeature(it, op.getBoolean("on")) }
            "touch" -> TouchAction.fromCode(op.getInt("action"))?.let { setTouch(TouchKey(op.getInt("side"), op.getInt("gesture")), it) }
            "cycle" -> setCycle(op.getInt("mask"))
        }
    }

    // ---------------------------------------------------------------- media handoff ("Play here")

    /** Moves playback to this phone (true) or to the PC (false). The buds follow whichever plays. */
    fun playOn(phone: Boolean) {
        Hub.link?.send("orbit.media", JSONObject().put("action", if (phone) "pause" else "play"))
        mediaKey(if (phone) KeyEvent.KEYCODE_MEDIA_PLAY else KeyEvent.KEYCODE_MEDIA_PAUSE)
        main.postDelayed(pollOnce, 800)
    }

    fun onPeerMedia(action: String) =
        mediaKey(if (action == "play") KeyEvent.KEYCODE_MEDIA_PLAY else KeyEvent.KEYCODE_MEDIA_PAUSE)

    private fun mediaKey(code: Int) {
        val am = app.getSystemService(AudioManager::class.java)
        am.dispatchMediaKeyEvent(KeyEvent(KeyEvent.ACTION_DOWN, code))
        am.dispatchMediaKeyEvent(KeyEvent(KeyEvent.ACTION_UP, code))
    }

    private val pollOnce = Runnable {
        val now = app.getSystemService(AudioManager::class.java).isMusicActive
        if (now != playingHere.value) { playingHere.value = now; schedulePublish() }
    }

    private val pollPlaying: Runnable = object : Runnable {
        override fun run() {
            if (enabled && Hub.link != null) pollOnce.run()
            main.postDelayed(this, 3000)
        }
    }

    // ---------------------------------------------------------------- sync with the PC

    private val publishNow = Runnable { publish() }

    fun schedulePublish() {
        main.removeCallbacks(publishNow)
        main.postDelayed(publishNow, 150)
    }

    fun publish() {
        Hub.link?.send("orbit.buds.state", toJson(local.value).put("enabled", enabled).put("playing", playingHere.value))
    }

    fun onPeerState(b: JSONObject) {
        val s = fromJson(b)
        peer.value = PeerBuds(b.optBoolean("enabled"), s, b.optBoolean("playing"))
        // If we don't know which buds yet, the PC's pick is a good guess.
        if (Hub.prefs.budsMac == null && s.mac != null && hasPermission()) {
            bondedBuds().firstOrNull { it.address.equals(s.mac, true) }?.let { select(it) }
        }
    }

    fun onPeerGone() { peer.value = null }

    fun toJson(s: BudsState): JSONObject = JSONObject()
        .put("connected", s.connected).put("name", s.name).put("mac", s.mac ?: JSONObject.NULL)
        .put("anc", s.anc?.name ?: JSONObject.NULL).put("cycle", s.cycleMask)
        .put("battery", JSONArray(s.battery)).put("charging", JSONArray(s.charging))
        .put("features", JSONObject().apply { s.features.forEach { (f, on) -> put(f.id.toString(), on) } })
        .put("touch", JSONArray().apply { s.touch.forEach { (k, a) -> put(JSONArray(listOf(k.side, k.gesture, a))) } })

    fun fromJson(o: JSONObject): BudsState {
        val bat = o.optJSONArray("battery")
        val chg = o.optJSONArray("charging")
        val feats = o.optJSONObject("features")
        val touch = o.optJSONArray("touch")
        return BudsState(
            connected = o.optBoolean("connected"),
            name = o.optString("name", "Earbuds"),
            mac = o.optString("mac").takeIf { it.isNotEmpty() && it != "null" },
            anc = o.optString("anc").let { n -> AncMode.entries.firstOrNull { it.name == n } },
            cycleMask = o.optInt("cycle"),
            battery = (0..2).map { bat?.optInt(it, -1) ?: -1 },
            charging = (0..2).map { chg?.optBoolean(it) ?: false },
            features = buildMap { feats?.keys()?.forEach { k -> Feature.fromId(k.toInt())?.let { put(it, feats.getBoolean(k)) } } },
            touch = buildMap {
                if (touch != null) for (i in 0 until touch.length()) {
                    val t = touch.getJSONArray(i)
                    put(TouchKey(t.getInt(0), t.getInt(1)), t.getInt(2))
                }
            }
        )
    }

    // ---------------------------------------------------------------- socket (worker thread)

    private fun command(block: () -> Unit) = worker.post {
        if (socket == null) doConnect(1)
        if (socket != null) block()
    }

    private fun doConnect(attempts: Int) {
        if (!enabled || socket?.isConnected == true || !hasPermission()) return
        val mac = Hub.prefs.budsMac ?: return
        val adapter = app.getSystemService(BluetoothManager::class.java)?.adapter ?: return
        if (!adapter.isEnabled) return
        val device = adapter.getRemoteDevice(mac)
        val name = device.name

        var s: BluetoothSocket? = null
        for (uuid in listOf(BudsProtocol.UUID_VENDOR, BudsProtocol.UUID_SPP)) {
            val candidate = device.createRfcommSocketToServiceRecord(uuid)
            try {
                candidate.connect()
                s = candidate
                Log.i(TAG, "connected via $uuid")
                break
            } catch (e: IOException) {
                Log.w(TAG, "connect via $uuid failed: ${e.message}")
                runCatching { candidate.close() }
            }
        }
        if (s == null) {
            if (attempts > 1) worker.postDelayed({ doConnect(attempts - 1) }, 3000)
            return
        }

        socket = s
        seq = 0
        update { copy(connected = true, name = name ?: this.name, mac = mac) }
        startReader(s)

        send(BudsProtocol.ANC_REQ, BudsProtocol.ancReq(BudsProtocol.ANC_TYPE_MODE))
        send(BudsProtocol.BATTERY_REQ, ByteArray(0))
        send(BudsProtocol.SUBSCRIPTION_SET, BudsProtocol.subscribe())
        send(BudsProtocol.TOUCH_REQ, BudsProtocol.touchReq())
        send(BudsProtocol.ANC_REQ, BudsProtocol.ancReq(BudsProtocol.ANC_TYPE_CYCLE))
        Feature.entries.forEach { send(BudsProtocol.MISC_REQ, BudsProtocol.miscReq(it.id)) }
    }

    /** Writes one command and waits (bounded) for its response: the buds handle one at a time. */
    private fun send(cmd: Int, payload: ByteArray) {
        val s = socket ?: return
        val pkt = BudsProtocol.encode(cmd, seq++, payload)
        synchronized(responseLock) { awaiting = cmd or BudsProtocol.RESPONSE_BIT; responded = false }
        try {
            s.outputStream.write(pkt)
            s.outputStream.flush()
        } catch (e: IOException) {
            Log.w(TAG, "write failed: ${e.message}")
            closeSocket()
            return
        }
        synchronized(responseLock) {
            val deadline = SystemClock.uptimeMillis() + RESPONSE_TIMEOUT_MS
            while (!responded && socket != null) {
                val left = deadline - SystemClock.uptimeMillis()
                if (left <= 0) break
                responseLock.wait(left)
            }
            awaiting = -1
        }
    }

    private fun startReader(s: BluetoothSocket) = thread(name = "buds-reader", isDaemon = true) {
        val parser = BudsProtocol.Parser()
        val buf = ByteArray(1024)
        try {
            val input = s.inputStream
            while (true) {
                val n = input.read(buf)
                if (n < 0) break
                for ((cmd, payload) in parser.feed(buf, n)) {
                    synchronized(responseLock) {
                        if (cmd == awaiting) { responded = true; responseLock.notifyAll() }
                    }
                    main.post { handlePacket(cmd, payload) }
                }
            }
        } catch (_: IOException) { }
        worker.post { if (socket === s) closeSocket() }
    }

    private fun closeSocket() {
        val s = socket ?: return
        socket = null
        runCatching { s.close() }
        synchronized(responseLock) { responseLock.notifyAll() }
        update { copy(connected = false) }
    }

    // ---------------------------------------------------------------- incoming (main thread)

    private fun handlePacket(cmd: Int, payload: ByteArray) {
        when (cmd) {
            BudsProtocol.ANC_RET -> applyAnc(payload)
            BudsProtocol.BATTERY_RET -> applyBattery(payload)
            BudsProtocol.MISC_RET -> {
                val known = BudsProtocol.parseMisc(payload).mapNotNull { (id, on) -> Feature.fromId(id)?.let { it to on } }
                if (known.isNotEmpty()) update { copy(features = features + known) }
            }
            BudsProtocol.TOUCH_RET -> {
                val t = BudsProtocol.parseTouch(payload)
                if (t.isNotEmpty()) update { copy(touch = touch + t) }
            }
            BudsProtocol.SUBSCRIPTION_RET -> when (payload.firstOrNull()?.toInt()) {
                BudsProtocol.SUB_ANC -> applyAnc(payload)
                BudsProtocol.SUB_BATTERY -> applyBattery(payload)
                BudsProtocol.SUB_GAME_MODE -> if (payload.size >= 2) {
                    update { copy(features = features + (Feature.GAME_MODE to (payload[1].toInt() == 1))) }
                }
            }
            BudsProtocol.ANC_ACK -> if (payload.firstOrNull()?.toInt() != 0) {
                worker.post { send(BudsProtocol.ANC_REQ, BudsProtocol.ancReq(BudsProtocol.ANC_TYPE_MODE)) }
            }
        }
    }

    private fun applyAnc(payload: ByteArray) {
        val (type, value) = BudsProtocol.parseAnc(payload) ?: return
        when (type) {
            BudsProtocol.ANC_TYPE_MODE -> AncMode.fromCode(value)?.let { m -> update { copy(anc = m) } }
            BudsProtocol.ANC_TYPE_CYCLE -> update { copy(cycleMask = value) }
        }
    }

    private fun applyBattery(payload: ByteArray) {
        val (levels, charging) = BudsProtocol.parseBattery(payload)
        update { copy(battery = levels.toList(), charging = charging.toList()) }
    }
}
