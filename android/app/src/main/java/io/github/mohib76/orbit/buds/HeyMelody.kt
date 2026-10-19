package io.github.mohib76.orbit.buds

import android.annotation.SuppressLint
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothSocket
import android.os.SystemClock
import android.util.Log
import org.json.JSONObject
import java.io.IOException
import kotlin.concurrent.thread

/**
 * realme / OnePlus / OPPO buds (the realme Link and HeyMelody apps' protocol) over RFCOMM.
 * Framing and commands are in [BudsProtocol].
 */
@SuppressLint("MissingPermission") // Buds only creates drivers once BLUETOOTH_CONNECT is granted
class HeyMelody(private val host: BudsHost) : BudsDriver {
    companion object {
        const val ID = "heymelody"
        private const val TAG = "OrbitBuds"
        private const val RESPONSE_TIMEOUT_MS = 1000L
        private val NAME_HINT = Regex("realme|oneplus|oppo|enco", RegexOption.IGNORE_CASE)

        val NOISE_MODES = AncMode.entries.map { NoiseMode(it.name, it.label) }
        private val TOGGLE_ORDER = Feature.entries.map { it.id.toString() }

        /** The buds advertise the vendor service, or (service list not cached yet) look like these brands. */
        fun matches(d: BluetoothDevice): Boolean =
            d.uuids?.any { it.uuid == BudsProtocol.UUID_VENDOR } == true || NAME_HINT.containsMatchIn(d.name ?: "")
    }

    override val id = ID

    @Volatile private var socket: BluetoothSocket? = null
    private var seq = 0
    private val responseLock = Object()
    private var awaiting = -1
    private var responded = false

    override val isOpen get() = socket?.isConnected == true

    override fun open(device: BluetoothDevice): Boolean {
        if (isOpen) return true
        // Plain SPP only for buds that look like these brands, so other devices never get our packets.
        val uuids = if (NAME_HINT.containsMatchIn(device.name ?: "")) listOf(BudsProtocol.UUID_VENDOR, BudsProtocol.UUID_SPP)
            else listOf(BudsProtocol.UUID_VENDOR)
        var s: BluetoothSocket? = null
        for (uuid in uuids) {
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
        if (s == null) return false

        socket = s
        seq = 0
        startReader(s)
        return true
    }

    override fun refresh() {
        send(BudsProtocol.ANC_REQ, BudsProtocol.ancReq(BudsProtocol.ANC_TYPE_MODE))
        send(BudsProtocol.BATTERY_REQ, ByteArray(0))
        send(BudsProtocol.SUBSCRIPTION_SET, BudsProtocol.subscribe())
        send(BudsProtocol.TOUCH_REQ, BudsProtocol.touchReq())
        send(BudsProtocol.ANC_REQ, BudsProtocol.ancReq(BudsProtocol.ANC_TYPE_CYCLE))
        // One request per feature: unsupported ids get no answer and simply stay hidden.
        Feature.entries.forEach { send(BudsProtocol.MISC_REQ, BudsProtocol.miscReq(it.id)) }
    }

    override fun close() {
        val s = socket ?: return
        socket = null
        runCatching { s.close() }
        synchronized(responseLock) { responseLock.notifyAll() }
        host.closed(this)
    }

    override fun execute(op: JSONObject) {
        when (op.optString("op")) {
            "anc" -> AncMode.entries.firstOrNull { it.name == op.optString("mode") }?.let {
                send(BudsProtocol.ANC_SET, BudsProtocol.ancSet(BudsProtocol.ANC_TYPE_MODE, it.code))
            }
            "cycle" -> {
                send(BudsProtocol.ANC_SET, BudsProtocol.ancSet(BudsProtocol.ANC_TYPE_CYCLE, op.optInt("mask")))
                send(BudsProtocol.ANC_REQ, BudsProtocol.ancReq(BudsProtocol.ANC_TYPE_CYCLE))
            }
            "feature" -> op.optString("id").toIntOrNull()?.let { id ->
                send(BudsProtocol.MISC_SET, BudsProtocol.miscSet(id, op.optBoolean("on")))
                send(BudsProtocol.MISC_REQ, BudsProtocol.miscReq(id))
            }
            "touch" -> {
                send(BudsProtocol.TOUCH_SET, BudsProtocol.touchSet(TouchKey(op.optInt("side"), op.optInt("gesture")), op.optInt("action")))
                send(BudsProtocol.TOUCH_REQ, BudsProtocol.touchReq())
            }
        }
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
            close()
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
                    handlePacket(cmd, payload)
                }
            }
        } catch (_: IOException) { }
        if (socket === s) close()
    }

    private fun handlePacket(cmd: Int, payload: ByteArray) {
        when (cmd) {
            BudsProtocol.ANC_RET -> applyAnc(payload)
            BudsProtocol.BATTERY_RET -> applyBattery(payload)
            BudsProtocol.MISC_RET -> {
                val known = BudsProtocol.parseMisc(payload).mapNotNull { (id, on) -> Feature.fromId(id)?.toggle(on) }
                if (known.isNotEmpty()) host.update { mergeToggles(known, TOGGLE_ORDER) }
            }
            BudsProtocol.TOUCH_RET -> {
                val t = BudsProtocol.parseTouch(payload)
                if (t.isNotEmpty()) host.update { copy(touch = touch + t) }
            }
            BudsProtocol.SUBSCRIPTION_RET -> when (payload.firstOrNull()?.toInt()) {
                BudsProtocol.SUB_ANC -> applyAnc(payload)
                BudsProtocol.SUB_BATTERY -> applyBattery(payload)
                BudsProtocol.SUB_GAME_MODE -> if (payload.size >= 2) {
                    host.update { mergeToggles(listOf(Feature.GAME_MODE.toggle(payload[1].toInt() == 1)), TOGGLE_ORDER) }
                }
            }
            // A refused change: ask what the mode really is.
            BudsProtocol.ANC_ACK -> if (payload.firstOrNull()?.toInt() != 0) {
                host.post { send(BudsProtocol.ANC_REQ, BudsProtocol.ancReq(BudsProtocol.ANC_TYPE_MODE)) }
            }
        }
    }

    private fun applyAnc(payload: ByteArray) {
        val (type, value) = BudsProtocol.parseAnc(payload) ?: return
        when (type) {
            // Buds that answer have noise control; the others never show the modes.
            BudsProtocol.ANC_TYPE_MODE -> AncMode.fromCode(value)?.let { m -> host.update { copy(noise = m.name, noiseModes = NOISE_MODES) } }
            BudsProtocol.ANC_TYPE_CYCLE -> host.update { copy(cycleMask = value) }
        }
    }

    private fun applyBattery(payload: ByteArray) {
        val (levels, charging) = BudsProtocol.parseBattery(payload)
        host.update { copy(battery = levels.toList(), charging = charging.toList()) }
    }
}
