package io.github.mohib76.orbit

import android.content.Context
import org.bouncycastle.asn1.x500.X500Name
import org.bouncycastle.cert.jcajce.JcaX509CertificateConverter
import org.bouncycastle.cert.jcajce.JcaX509v3CertificateBuilder
import org.bouncycastle.operator.jcajce.JcaContentSignerBuilder
import java.io.File
import java.math.BigInteger
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.cert.Certificate
import java.security.cert.X509Certificate
import java.util.Date
import javax.net.ssl.KeyManagerFactory
import javax.net.ssl.SSLContext
import javax.net.ssl.X509TrustManager

/**
 * This phone's long-lived TLS identity: a self-signed RSA certificate kept in app storage.
 * The PC pins its SHA-256 fingerprint at pairing, and we pin the PC's.
 */
object Identity {
    private const val ALIAS = "orbit"
    private val PASS = "orbit".toCharArray()
    private lateinit var keyStore: KeyStore

    fun init(ctx: Context) {
        val file = File(ctx.filesDir, "identity.p12")
        keyStore = KeyStore.getInstance("PKCS12")
        if (file.exists()) {
            try {
                file.inputStream().use { keyStore.load(it, PASS) }
                if (keyStore.containsAlias(ALIAS)) return
            } catch (_: Exception) { }
        }
        keyStore.load(null, null)
        val kp = KeyPairGenerator.getInstance("RSA").apply { initialize(2048) }.generateKeyPair()
        val now = System.currentTimeMillis()
        val name = X500Name("CN=Orbit Phone")
        val builder = JcaX509v3CertificateBuilder(
            name, BigInteger(64, SecureRandom()), Date(now - 86_400_000L),
            Date(now + 20L * 365 * 86_400_000L), name, kp.public
        )
        val signer = JcaContentSignerBuilder("SHA256withRSA").build(kp.private)
        val cert = JcaX509CertificateConverter().getCertificate(builder.build(signer))
        keyStore.setKeyEntry(ALIAS, kp.private, PASS, arrayOf<Certificate>(cert))
        file.outputStream().use { keyStore.store(it, PASS) }
    }

    val certificate: X509Certificate get() = keyStore.getCertificate(ALIAS) as X509Certificate
    val fingerprint: String get() = fingerprintOf(certificate)

    fun fingerprintOf(cert: Certificate): String =
        MessageDigest.getInstance("SHA-256").digest(cert.encoded).joinToString("") { "%02x".format(it) }

    /** Server context: present our cert, accept any client cert; we check its fingerprint after the handshake. */
    fun serverContext(): SSLContext {
        val kmf = KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm())
        kmf.init(keyStore, PASS)
        val acceptAll = object : X509TrustManager {
            override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) {}
            override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {}
            override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
        }
        return SSLContext.getInstance("TLS").apply { init(kmf.keyManagers, arrayOf(acceptAll), SecureRandom()) }
    }
}
