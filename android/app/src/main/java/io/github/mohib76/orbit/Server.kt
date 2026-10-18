package io.github.mohib76.orbit

import android.content.Context
import android.net.wifi.WifiManager
import android.util.Log
import org.json.JSONObject
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.OutputStream
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.Socket
import java.util.concurrent.Executors
import javax.net.ssl.SSLServerSocket
import javax.net.ssl.SSLSocket
import kotlin.concurrent.thread

/** The control channel to the PC. Writes go through a single background thread. */
class Link(private val sock: SSLSocket, val input: BufferedInputStream, val kind: String) {
    private val out: OutputStream = BufferedOutputStream(sock.outputStream, 64 * 1024)
    private val writer = Executors.newSingleThreadExecutor()
    @Volatile var closed = false; private set

    fun send(type: String, body: JSONObject = JSONObject()) {
        if (closed) return
        val bytes = Proto.encode(type, body)
        try {
            writer.execute {
                try { out.write(bytes); out.flush() } catch (e: Exception) { close() }
            }
        } catch (_: Exception) { }
    }

    fun close() {
        if (closed) return
        closed = true
        writer.shutdown()
        try { sock.close() } catch (_: Exception) { }
    }
}

/**
 * TLS server the PC connects to (control + data connections) and a UDP responder so the PC can find
 * us on a shared Wi-Fi. On USB tethering and on our hotspot the PC simply dials its gateway (us).
 */
class Server(private val ctx: Context) {
    private var server: SSLServerSocket? = null
    private var udp: DatagramSocket? = null
    private var multicastLock: WifiManager.MulticastLock? = null
    @Volatile private var running = false

    fun start() {
        if (running) return
        running = true
        thread(name = "orbit-tcp", isDaemon = true) { acceptLoop() }
        thread(name = "orbit-udp", isDaemon = true) { discoveryLoop() }
        try {
            multicastLock = ctx.getSystemService(WifiManager::class.java)
                .createMulticastLock("orbit").apply { setReferenceCounted(false); acquire() }
        } catch (_: Exception) { }
    }

    fun stop() {
        running = false
        try { server?.close() } catch (_: Exception) { }
        try { udp?.close() } catch (_: Exception) { }
        multicastLock?.release()
    }

    private fun acceptLoop() {
        while (running) {
            try {
                val ss = Identity.serverContext().serverSocketFactory.createServerSocket() as SSLServerSocket
                ss.reuseAddress = true
                ss.needClientAuth = true
                ss.bind(InetSocketAddress(Proto.PORT), 16)
                server = ss
                Log.i(Hub.TAG, "listening on ${Proto.PORT}")
                while (running) {
                    val s = ss.accept() as SSLSocket
                    thread(name = "orbit-conn", isDaemon = true) { handle(s) }
                }
            } catch (e: Exception) {
                if (running) { Log.w(Hub.TAG, "accept loop", e); Thread.sleep(2000) }
            }
        }
    }

    private fun handle(s: SSLSocket) {
        try {
            s.tcpNoDelay = true
            s.soTimeout = 15_000
            s.startHandshake()
            val clientFp = Identity.fingerprintOf(s.session.peerCertificates[0])
            val input = BufferedInputStream(s.inputStream, 64 * 1024)
            val first = Proto.decode(Proto.readLine(input) ?: return s.close())
            val pc = Hub.prefs.pc
            when (first.type) {
                "orbit.hello" -> {
                    if (pc == null || pc.fingerprint != clientFp || first.body.optString("id") != pc.id) {
                        s.outputStream.write(Proto.encode("orbit.reject")); s.close(); return
                    }
                    Hub.checkPeerVersion(first.body)
                    val link = Link(s, input, kindOf(s))
                    link.send("orbit.hello", me())
                    runControl(s, link)
                }
                "orbit.pair" -> pair(s, input, clientFp, first.body)
                "orbit.data" -> {
                    if (pc == null || pc.fingerprint != clientFp) { s.close(); return }
                    Transfers.handleData(s, input, first.body)
                }
                else -> s.close()
            }
        } catch (e: Exception) {
            Log.d(Hub.TAG, "conn: ${e.message}")
            try { s.close() } catch (_: Exception) { }
        }
    }

    @Volatile private var pairing = false

    private fun pair(s: SSLSocket, input: BufferedInputStream, clientFp: String, b: JSONObject) {
        // The PC tries several routes at once (USB cable, tethering, Wi-Fi); pair over just one.
        val offer = synchronized(this) {
            val o = Hub.accepted
            if (pairing || o == null || o.expires < System.currentTimeMillis() || o.pcId != b.optString("id") || o.pcFp != clientFp) null
            else { pairing = true; o }
        }
        if (offer == null) { s.outputStream.write(Proto.encode("orbit.pair.reject")); s.close(); return }
        try { pairChecked(s, input, clientFp, b, offer) } finally { pairing = false }
    }

    private fun pairChecked(s: SSLSocket, input: BufferedInputStream, clientFp: String, b: JSONObject, offer: PairOffer) {
        val myFp = Identity.fingerprint
        val nonce = Proto.nonce()
        val proof = Proto.hmac(offer.token, "phone", b.getString("nonce"), myFp, clientFp)
        s.outputStream.write(Proto.encode("orbit.pair.proof", me().put("nonce", nonce).put("proof", proof)))
        s.outputStream.flush()
        val reply = Proto.decode(Proto.readLine(input) ?: return s.close())
        val expected = Proto.hmac(offer.token, "pc", nonce, myFp, clientFp)
        if (reply.type != "orbit.pair.ok" || reply.body.optString("proof") != expected) { s.close(); return }

        Hub.checkPeerVersion(b)
        Hub.onPaired(PairedPc(offer.pcId, b.optString("name", offer.pcName), clientFp, System.currentTimeMillis()))
        Log.i(Hub.TAG, "paired with ${offer.pcName}")
        runControl(s, Link(s, input, kindOf(s)))
    }

    private fun runControl(s: SSLSocket, link: Link) {
        s.soTimeout = 50_000
        Hub.onConnected(link)
        Hub.pushSettings()
        try {
            while (!link.closed) {
                val line = Proto.readLine(link.input) ?: break
                if (line.isEmpty()) continue
                val p = Proto.decode(line)
                try { Hub.handle(link, p.type, p.body) } catch (e: Exception) { Log.w(Hub.TAG, "handle ${p.type}", e) }
            }
        } catch (e: Exception) {
            Log.d(Hub.TAG, "link closed: ${e.message}")
        } finally {
            link.close()
            Hub.onDisconnected(link)
        }
    }

    private fun me() = JSONObject()
        .put("id", Hub.prefs.deviceId)
        .put("name", Hub.prefs.deviceName)
        .put("model", Hub.prefs.model)
        .put("proto", Proto.VERSION)
        .put("app", BuildConfig.VERSION_NAME)

    /** How the PC reached us, from the interface our end of the socket is on. */
    private fun kindOf(s: Socket): String {
        if (s.inetAddress.isLoopbackAddress) return "USB cable"
        val name = try { NetworkInterface.getByInetAddress(s.localAddress)?.name ?: "" } catch (_: Exception) { "" }
        return when {
            name.startsWith("rndis") || name.startsWith("usb") || name.startsWith("ncm") -> "USB tethering"
            name.startsWith("swlan") || name.startsWith("ap") || name.contains("softap") -> "Hotspot"
            name.startsWith("wlan") -> "Wi-Fi"
            else -> "Network"
        }
    }

    private fun discoveryLoop() {
        while (running) {
            try {
                DatagramSocket(null).use { sock ->
                    sock.reuseAddress = true
                    sock.broadcast = true
                    sock.bind(InetSocketAddress(Proto.DISCOVERY_PORT))
                    udp = sock
                    val buf = ByteArray(2048)
                    while (running) {
                        val pkt = DatagramPacket(buf, buf.size)
                        sock.receive(pkt)
                        val p = try { Proto.decode(String(pkt.data, 0, pkt.length)) } catch (_: Exception) { continue }
                        if (p.type != "orbit.discover") continue
                        val from = p.body.optString("id")
                        val known = Hub.prefs.pc?.id == from || Hub.accepted?.pcId == from
                        if (!known) continue
                        val reply = Proto.encode("orbit.identity", me().put("port", Proto.PORT))
                        sock.send(DatagramPacket(reply, reply.size, pkt.address, pkt.port))
                    }
                }
            } catch (e: Exception) {
                if (running) { Log.d(Hub.TAG, "udp: ${e.message}"); Thread.sleep(2000) }
            }
        }
    }
}
