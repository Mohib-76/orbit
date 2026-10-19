package io.github.mohib76.orbit.buds

import java.io.ByteArrayOutputStream
import java.util.UUID

// Protocol facts from Gadgetbridge's Oppo/realme support (see THIRD_PARTY_NOTICES.md).
// realme / OnePlus / OPPO "HeyMelody" earbuds over classic Bluetooth RFCOMM; the driver is HeyMelody.kt.

/** ANC modes and their wire values. `name` is the noise mode id on the Orbit link. */
enum class AncMode(val code: Int, val label: String) {
    OFF(0x01, "Off"),
    TRANSPARENCY(0x02, "Transparency"),
    ANC(0x08, "ANC");

    fun next() = when (this) { OFF -> TRANSPARENCY; TRANSPARENCY -> ANC; ANC -> OFF }

    companion object {
        // 0x04 / 0x10 / 0x20 are ANC light / medium / smart on other models; show them as ANC.
        fun fromCode(code: Int): AncMode? = when (code) {
            0x01 -> OFF
            0x02 -> TRANSPARENCY
            0x04, 0x08, 0x10, 0x20 -> ANC
            else -> null
        }
    }
}

/** A touch gesture on one bud: side (1 left, 2 right, 4 both) + gesture code. */
data class TouchKey(val side: Int, val gesture: Int)

enum class TouchAction(val code: Int, val label: String) {
    OFF(0x00, "None"),
    PLAY_PAUSE(0x01, "Play / pause"),
    PREVIOUS(0x05, "Previous track"),
    NEXT(0x06, "Next track"),
    VOLUME_UP(0x0B, "Volume up"),
    VOLUME_DOWN(0x0C, "Volume down"),
    VOICE_ASSISTANT(0x04, "Voice assistant"),
    NOISE_CONTROL(0x08, "Noise control"),
    GAME_MODE(0x11, "Game mode");

    companion object {
        fun fromCode(code: Int) = entries.firstOrNull { it.code == code }
        val SINGLE_BUD = listOf(PLAY_PAUSE, PREVIOUS, NEXT, VOLUME_UP, VOLUME_DOWN, VOICE_ASSISTANT, NOISE_CONTROL, OFF)
        val BOTH_BUDS = listOf(OFF, GAME_MODE)
    }
}

/** On/off features behind MISC_CONFIG. */
enum class Feature(val id: Int, val title: String, val subtitle: String) {
    GAME_MODE(6, "Game mode", "Lower latency for games and video"),
    SPATIAL(27, "360° spatial audio", "Surround-style sound"),
    BASS(29, "Bass boost", "Dynamic low-frequency enhancement"),
    VOICE(12, "Voice enhancement", "Clearer voices in calls and videos"),
    WIND(26, "Wind-noise reduction", "Cuts wind noise on the mics"),
    DUAL(17, "Dual-device connection", "Stay connected to your phone and PC at once"),
    HQ(24, "High-quality audio", "Hi-res codec when the device supports it");

    fun toggle(on: Boolean) = BudsToggle(id.toString(), title, subtitle, on)

    companion object {
        fun fromId(id: Int) = entries.firstOrNull { it.id == id }
    }
}

/** OPPOv1 framing: AA | varint(len) | 00 00 | cmd:u16le | seq | plen:u16le | payload */
object BudsProtocol {
    val UUID_VENDOR: UUID = UUID.fromString("0000079a-d102-11e1-9b23-00025b00a5a5")
    val UUID_SPP: UUID = UUID.fromString("00001101-0000-1000-8000-00805f9b34fb")

    const val RESPONSE_BIT = 0x8000
    const val BATTERY_REQ = 0x0106
    const val BATTERY_RET = 0x8106
    const val SUBSCRIPTION_SET = 0x0205
    const val SUBSCRIPTION_RET = 0x0204
    const val TOUCH_REQ = 0x0108
    const val TOUCH_RET = 0x8108
    const val TOUCH_SET = 0x0401
    const val MISC_REQ = 0x010D
    const val MISC_RET = 0x810D
    const val MISC_SET = 0x0403
    const val ANC_REQ = 0x010C
    const val ANC_RET = 0x810C
    const val ANC_SET = 0x0404
    const val ANC_ACK = 0x8404

    private const val PREAMBLE = 0xAA
    const val ANC_TYPE_MODE = 0x01
    const val ANC_TYPE_CYCLE = 0x02
    const val SUB_BATTERY = 0x01
    const val SUB_ANC = 0x03
    const val SUB_GAME_MODE = 0x05

    const val SIDE_LEFT = 0x01
    const val SIDE_RIGHT = 0x02
    const val SIDE_BOTH = 0x04
    const val TAP_2 = 0x0201
    const val TAP_3 = 0x0301
    const val HOLD = 0x0401

    fun ancReq(type: Int) = byteArrayOf(type.toByte(), 0x01)
    fun ancSet(type: Int, value: Int) = byteArrayOf(type.toByte(), 0x01, value.toByte())
    fun subscribe() = byteArrayOf(0x09, SUB_BATTERY.toByte(), SUB_ANC.toByte(), SUB_GAME_MODE.toByte())
    fun miscReq(id: Int) = byteArrayOf(0x01, id.toByte())
    fun miscSet(id: Int, on: Boolean) = byteArrayOf(id.toByte(), if (on) 1 else 0)
    fun touchReq() = byteArrayOf(0x02, 0x03, 0x01)
    fun touchSet(key: TouchKey, action: Int) = byteArrayOf(
        0x01, key.side.toByte(), (key.gesture and 0xFF).toByte(), (key.gesture shr 8).toByte(), action.toByte()
    )

    fun encode(cmd: Int, seq: Int, payload: ByteArray): ByteArray {
        val out = ByteArrayOutputStream()
        out.write(PREAMBLE)
        var len = 7 + payload.size
        while (true) {
            val b = len and 0x7F
            len = len ushr 7
            if (len == 0) { out.write(b); break }
            out.write(b or 0x80)
        }
        out.write(0); out.write(0)
        out.write(cmd and 0xFF); out.write((cmd shr 8) and 0xFF)
        out.write(seq and 0xFF)
        out.write(payload.size and 0xFF); out.write((payload.size shr 8) and 0xFF)
        out.write(payload)
        return out.toByteArray()
    }

    /** (type, value) from an ANC_RET or ANC subscription push: [00|03, type, (01), value]. */
    fun parseAnc(payload: ByteArray): Pair<Int, Int>? {
        if (payload.size < 3) return null
        return payload[1].u() to payload.last().u()
    }

    /** Levels and charging flags (0 = left, 1 = right, 2 = case); payload = [00|01, count, (idx, level)*]. */
    fun parseBattery(payload: ByteArray): Pair<IntArray, BooleanArray> {
        val levels = intArrayOf(-1, -1, -1)
        val charging = BooleanArray(3)
        var i = 2
        while (i + 1 < payload.size) {
            val idx = payload[i].u() - 1
            val level = payload[i + 1].u() and 0x7F
            if (idx in 0..2 && !(idx == 2 && level == 0)) {
                levels[idx] = level
                charging[idx] = payload[i + 1].u() and 0x80 != 0
            }
            i += 2
        }
        return levels to charging
    }

    /** Feature id -> on, from MISC_RET [00, count, (id, value)*]. */
    fun parseMisc(payload: ByteArray): Map<Int, Boolean> {
        if (payload.size < 2 || payload[0].toInt() != 0) return emptyMap()
        val out = mutableMapOf<Int, Boolean>()
        var i = 2
        repeat(payload[1].u()) {
            if (i + 1 >= payload.size) return out
            out[payload[i].u()] = payload[i + 1].u() == 1
            i += 2
        }
        return out
    }

    /** TouchKey -> action code, from TOUCH_RET [00, count, (side, gesture:u16le, action)*]. */
    fun parseTouch(payload: ByteArray): Map<TouchKey, Int> {
        val out = mutableMapOf<TouchKey, Int>()
        if (payload.size < 2 || (payload.size - 2) % 4 != 0) return out
        for (i in 2 until payload.size step 4) {
            val gesture = payload[i + 1].u() or (payload[i + 2].u() shl 8)
            out[TouchKey(payload[i].u(), gesture)] = payload[i + 3].u()
        }
        return out
    }

    private fun Byte.u() = toInt() and 0xFF

    /** Reassembles packets from the RFCOMM stream. */
    class Parser {
        private var buf = ByteArray(0)

        fun feed(data: ByteArray, n: Int): List<Pair<Int, ByteArray>> {
            buf += data.copyOf(n)
            val out = mutableListOf<Pair<Int, ByteArray>>()
            var pos = 0
            while (pos < buf.size) {
                if (buf[pos].u() != PREAMBLE) { pos++; continue }
                var p = pos + 1
                var len = 0
                var shift = 0
                var complete = false
                while (p < buf.size) {
                    val b = buf[p++].u()
                    len = len or ((b and 0x7F) shl shift)
                    shift += 7
                    if (b and 0x80 == 0) { complete = true; break }
                }
                if (!complete || buf.size - p < len) break
                val pkt = buf.copyOfRange(p, p + len)
                pos = p + len
                if (pkt.size < 7) continue
                val cmd = pkt[2].u() or (pkt[3].u() shl 8)
                val plen = pkt[5].u() or (pkt[6].u() shl 8)
                if (7 + plen <= pkt.size) out += cmd to pkt.copyOfRange(7, 7 + plen)
            }
            buf = buf.copyOfRange(pos, buf.size)
            return out
        }
    }
}
