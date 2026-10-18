package io.github.mohib76.orbit.buds

import android.Manifest
import android.annotation.SuppressLint
import android.bluetooth.BluetoothClass
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.content.Context
import android.content.pm.PackageManager
import android.media.AudioManager
import android.os.Handler
import android.os.HandlerThread
import android.os.Looper
import android.util.Log
import android.view.KeyEvent
import io.github.mohib76.orbit.Hub
import kotlinx.coroutines.flow.MutableStateFlow
import org.json.JSONObject

/**
 * The phone's side of the earbuds, for any brand: which buds, whether the OS has them connected, their
 * battery as the OS reports it, and playback. A [BudsDriver] adds the controls for buds Orbit knows.
 *
 * Both sides publish their own view over the Orbit link, and when only one of them holds the buds'
 * control channel the other relays its commands through it.
 */
@SuppressLint("MissingPermission") // every Bluetooth call is gated on hasPermission()
object Buds : BudsHost {
    private const val TAG = "OrbitBuds"
    private val HEADSET_CLASSES = setOf(BluetoothClass.Device.AUDIO_VIDEO_WEARABLE_HEADSET, BluetoothClass.Device.AUDIO_VIDEO_HEADPHONES)

    private lateinit var app: Context
    private val main = Handler(Looper.getMainLooper())
    private val worker by lazy { Handler(HandlerThread("buds-worker").apply { start() }.looper) }

    /** Worker thread only. */
    private var driver: BudsDriver? = null

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
        worker.post { refreshOs() }
    }

    val enabled get() = Hub.prefs.budsEnabled

    fun hasPermission(ctx: Context = app) =
        ctx.checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED

    /** The state to show: whoever holds the controls, else whoever has the buds connected. */
    fun effective(l: BudsState = local.value, p: PeerBuds? = peer.value): BudsState = when {
        l.control -> l
        p != null && p.state.control -> p.state
        l.connected -> l
        p != null && p.state.connected -> p.state
        else -> l
    }

    // ---------------------------------------------------------------- BudsHost

    override fun update(f: BudsState.() -> BudsState) {
        main.post {
            val next = local.value.f()
            if (next != local.value) {
                local.value = next
                schedulePublish()
            }
        }
    }

    override fun closed(driver: BudsDriver) {
        worker.post { if (driver === this.driver) update { copy(control = false) } }
    }

    override fun post(block: () -> Unit) {
        worker.post(block)
    }

    // ---------------------------------------------------------------- devices

    fun bondedBuds(): List<BluetoothDevice> {
        if (!hasPermission()) return emptyList()
        val devices = app.getSystemService(BluetoothManager::class.java)?.adapter?.bondedDevices ?: return emptyList()
        return devices.sortedWith(compareByDescending<BluetoothDevice> { isConnected(it) }.thenByDescending { isHeadset(it) })
    }

    fun isConnected(d: BluetoothDevice): Boolean = try {
        d.javaClass.getMethod("isConnected").invoke(d) as Boolean
    } catch (_: Exception) { false }

    /** Earbuds or headphones by their Bluetooth class (or, failing that, their name). */
    private fun isHeadset(d: BluetoothDevice) =
        d.bluetoothClass?.deviceClass in HEADSET_CLASSES || d.name?.contains("buds", true) == true

    /** Picks the buds automatically: the connected earbuds, else the only paired ones. */
    fun autoSelect(): Boolean {
        if (Hub.prefs.budsMac != null) return true
        val list = bondedBuds().filter { isHeadset(it) }
        val pick = list.filter { isConnected(it) }.singleOrNull() ?: list.singleOrNull() ?: return false
        select(pick)
        return true
    }

    fun select(d: BluetoothDevice) {
        Hub.prefs.budsMac = d.address
        Hub.prefs.budsName = d.name
        local.value = BudsState(mac = d.address, name = d.name ?: "Earbuds")
        worker.post {
            driver?.close()
            driver = null
            refreshOs()
            doConnect(1)
        }
        schedulePublish()
    }

    private fun device(): BluetoothDevice? {
        if (!hasPermission()) return null
        val mac = Hub.prefs.budsMac ?: return null
        val adapter = app.getSystemService(BluetoothManager::class.java)?.adapter ?: return null
        return if (adapter.isEnabled) adapter.getRemoteDevice(mac) else null
    }

    // ---------------------------------------------------------------- what the OS knows (any buds)

    /** Connection and battery as the OS sees them. Worker thread. */
    private fun refreshOs() {
        val d = device() ?: return
        val osConnected = isConnected(d)
        val level = if (osConnected) osBattery(d) else -1
        update { copy(connected = osConnected || control, batteryMain = level) }
    }

    /** The headset's standard battery report (HFP), which Android keeps per device. Hidden API, so reflection. */
    private fun osBattery(d: BluetoothDevice): Int = try {
        (d.javaClass.getMethod("getBatteryLevel").invoke(d) as Int).takeIf { it in 0..100 } ?: -1
    } catch (e: Exception) {
        Log.w(TAG, "battery level unavailable: ${e.message}")
        -1
    }

    /** ACL connect / disconnect of the chosen buds (from BudsService). */
    fun onOsConnection(connected: Boolean) {
        if (connected) {
            worker.post { refreshOs() }
            // Give the buds a moment to bring up their profiles before opening RFCOMM.
            connect(attempts = 3, delayMs = 2000)
        } else {
            worker.post { driver?.close() }
            update { copy(connected = false, batteryMain = -1) }
        }
    }

    /** BluetoothDevice.ACTION_BATTERY_LEVEL_CHANGED for the chosen buds. */
    fun onOsBattery(level: Int) = update { copy(batteryMain = level.takeIf { it in 0..100 } ?: -1) }

    // ---------------------------------------------------------------- control channel (worker thread)

    fun connect(attempts: Int = 1, delayMs: Long = 0) {
        if (!enabled) return
        worker.postDelayed({ doConnect(attempts) }, delayMs)
    }

    fun disconnect() = worker.post { driver?.close() }

    private fun doConnect(attempts: Int) {
        if (!enabled || driver?.isOpen == true) return
        val device = device() ?: return
        val d = driver ?: BudsDrivers.forDevice(device, this).also { driver = it }
        if (local.value.driver != d?.id) update { copy(driver = d?.id) }
        if (d == null) {
            refreshOs()
            return
        }
        if (d.open(device)) {
            Log.i(TAG, "${d.id} control open")
            update { copy(control = true, connected = true, name = device.name ?: name, mac = device.address) }
            d.refresh()
        } else if (attempts > 1) {
            worker.postDelayed({ doConnect(attempts - 1) }, 3000)
        }
    }

    // ---------------------------------------------------------------- commands (main thread)

    fun setAnc(mode: String) = route(JSONObject().put("op", "anc").put("mode", mode))

    fun cycleAnc() { effective().nextNoise()?.let { setAnc(it) } }

    fun setCycle(mask: Int) = route(JSONObject().put("op", "cycle").put("mask", mask))

    // Numeric ids go as numbers: older PCs read them with GetValue<int>().
    fun setFeature(id: String, on: Boolean) =
        route(JSONObject().put("op", "feature").put("id", id.toIntOrNull() ?: id).put("on", on))

    fun setTouch(key: TouchKey, action: TouchAction) =
        route(JSONObject().put("op", "touch").put("side", key.side).put("gesture", key.gesture).put("action", action.code))

    /** Run it here if we hold the controls; else ask the PC if it does; else try to open them here. */
    private fun route(op: JSONObject) {
        val p = peer.value
        if (!local.value.control && p != null && p.state.control && Hub.link != null) {
            peer.value = p.copy(state = p.state.apply(op))
            Hub.link?.send("orbit.buds.cmd", op)
        } else runLocal(op)
    }

    private fun runLocal(op: JSONObject) {
        if (local.value.control) update { apply(op) } // optimistic; the buds' answer corrects it
        worker.post {
            if (driver?.isOpen != true) doConnect(1)
            driver?.takeIf { it.isOpen }?.execute(op)
        }
    }

    /** A command relayed from the PC. */
    fun onPeerCommand(op: JSONObject) {
        if (local.value.control) runLocal(op)
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
        Hub.link?.send("orbit.buds.state", local.value.toJson().put("enabled", enabled).put("playing", playingHere.value))
    }

    fun onPeerState(b: JSONObject) {
        val s = BudsState.fromJson(b)
        peer.value = PeerBuds(b.optBoolean("enabled"), s, b.optBoolean("playing"))
        // If we don't know which buds yet, the PC's pick is a good guess.
        if (Hub.prefs.budsMac == null && s.mac != null && hasPermission()) {
            bondedBuds().firstOrNull { it.address.equals(s.mac, true) }?.let { select(it) }
        }
    }

    fun onPeerGone() { peer.value = null }
}
