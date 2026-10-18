package io.github.mohib76.orbit

import io.github.mohib76.orbit.buds.AncMode
import io.github.mohib76.orbit.buds.BudsProtocol
import io.github.mohib76.orbit.buds.TouchKey
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream

class ProtoTest {
    @Test fun encodeDecodeRoundTrip() {
        val bytes = Proto.encode("orbit.clipboard", JSONObject().put("text", "hello\nworld"))
        val line = String(bytes)
        assertTrue(line.endsWith("\n"))
        assertEquals(1, line.count { it == '\n' })
        val p = Proto.decode(line.trimEnd('\n'))
        assertEquals("orbit.clipboard", p.type)
        assertEquals("hello\nworld", p.body.getString("text"))
    }

    @Test fun readLineDoesNotOverRead() {
        val input = ByteArrayInputStream("{\"type\":\"a\"}\nRAW".toByteArray())
        assertEquals("{\"type\":\"a\"}", Proto.readLine(input))
        assertEquals("RAW", String(input.readBytes()))
    }

    @Test fun hmacIsStableLowercaseHex() {
        val mac = Proto.hmac("token", "pc", "n", "a", "b")
        assertEquals(64, mac.length)
        assertEquals(mac.lowercase(), mac)
        assertEquals(mac, Proto.hmac("token", "pc", "n", "a", "b"))
        assertTrue(mac != Proto.hmac("token", "phone", "n", "a", "b"))
    }
}

class BudsProtocolTest {
    private fun hex(b: ByteArray) = b.joinToString("") { "%02X".format(it) }
    private fun bytes(h: String) = h.chunked(2).map { it.toInt(16).toByte() }.toByteArray()

    @Test fun encodesAncSetLikeTheReference() {
        val pkt = BudsProtocol.encode(BudsProtocol.ANC_SET, 0, BudsProtocol.ancSet(BudsProtocol.ANC_TYPE_MODE, AncMode.ANC.code))
        assertEquals("AA0A00000404000300010108", hex(pkt))
    }

    @Test fun parserReassemblesSplitPackets() {
        val a = BudsProtocol.encode(BudsProtocol.BATTERY_RET, 1, byteArrayOf(0, 3, 1, 90, 2, (0x80 or 85).toByte(), 3, 40))
        val b = BudsProtocol.encode(BudsProtocol.ANC_RET, 2, byteArrayOf(0, 1, 1, 2))
        val stream = a + b
        val parser = BudsProtocol.Parser()
        assertTrue(parser.feed(stream.copyOfRange(0, 5), 5).isEmpty())
        val rest = stream.copyOfRange(5, stream.size)
        val out = parser.feed(rest, rest.size)
        assertEquals(2, out.size)
        assertEquals(BudsProtocol.BATTERY_RET, out[0].first)
        assertEquals(BudsProtocol.ANC_RET, out[1].first)
    }

    @Test fun parsesBattery() {
        val (levels, charging) = BudsProtocol.parseBattery(byteArrayOf(0, 3, 1, 90, 2, (0x80 or 85).toByte(), 3, 0))
        assertEquals(listOf(90, 85, -1), levels.toList())
        assertFalse(charging[0])
        assertTrue(charging[1])
    }

    @Test fun parsesTouchConfig() {
        val t = BudsProtocol.parseTouch(bytes("0006010102000101030001010400020102000201030002010400"))
        assertEquals(6, t.size)
        assertEquals(0, t[TouchKey(BudsProtocol.SIDE_LEFT, BudsProtocol.TAP_2)])
    }

    @Test fun ancCodes() {
        assertEquals(AncMode.OFF, AncMode.fromCode(0x01))
        assertEquals(AncMode.ANC, AncMode.fromCode(0x10))
        assertNull(AncMode.fromCode(0x40))
        assertEquals(AncMode.OFF, AncMode.ANC.next())
    }
}
