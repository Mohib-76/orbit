package io.github.mohib76.orbit

import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.InputStream
import java.security.SecureRandom
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/**
 * Wire format (after KDE Connect): one JSON object per line, {"type": "...", "body": {...}}.
 * This phone is the TLS server on [PORT]; the PC dials in. Files move on separate connections.
 */
object Proto {
    /** Bump for incompatible wire changes. Sent as "proto" in orbit.hello / orbit.pair. */
    const val VERSION = 1

    const val PORT = 47800
    const val DISCOVERY_PORT = 47801

    fun encode(type: String, body: JSONObject = JSONObject()): ByteArray =
        (JSONObject().put("type", type).put("body", body).toString() + "\n").toByteArray(Charsets.UTF_8)

    data class Packet(val type: String, val body: JSONObject)

    fun decode(line: String): Packet {
        val o = JSONObject(line)
        return Packet(o.optString("type"), o.optJSONObject("body") ?: JSONObject())
    }

    /** Reads one '\n'-terminated UTF-8 line without over-reading (raw bytes may follow). */
    fun readLine(input: InputStream, max: Int = 4 * 1024 * 1024): String? {
        val buf = ByteArrayOutputStream(256)
        while (true) {
            val b = input.read()
            if (b < 0) return if (buf.size() == 0) null else buf.toString("UTF-8")
            if (b == '\n'.code) return buf.toString("UTF-8")
            buf.write(b)
            if (buf.size() > max) throw IllegalStateException("line too long")
        }
    }

    fun hmac(token: String, vararg parts: String): String {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(token.toByteArray(Charsets.UTF_8), "HmacSHA256"))
        return mac.doFinal(parts.joinToString("|").toByteArray(Charsets.UTF_8)).joinToString("") { "%02x".format(it) }
    }

    fun nonce(): String = ByteArray(16).also { SecureRandom().nextBytes(it) }.joinToString("") { "%02x".format(it) }
}
