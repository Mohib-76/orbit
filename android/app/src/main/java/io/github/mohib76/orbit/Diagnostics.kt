package io.github.mohib76.orbit

import android.app.ActivityManager
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.Process
import java.net.Inet4Address
import java.net.NetworkInterface

/**
 * "Export diagnostics" (shares this app's recent log, with names and addresses removed) and
 * "Reset Orbit". Logs never contain notification text or clipboard contents to begin with.
 */
object Diagnostics {
    private const val MAX_CHARS = 200_000

    fun share(ctx: Context) {
        val text = redact(buildString {
            appendLine("Orbit ${BuildConfig.VERSION_NAME} (protocol ${Proto.VERSION}) for Android")
            appendLine("PC app: ${Hub.peerAppVersion.ifEmpty { "unknown" }}")
            appendLine("Android ${Build.VERSION.RELEASE} (SDK ${Build.VERSION.SDK_INT}) · ${Build.MANUFACTURER} ${Build.MODEL}")
            appendLine("State: ${Hub.ui.value.conn} · link ${Hub.ui.value.link}")
            appendLine("Version warning: ${Hub.versionWarning.value}")
            appendLine()
            append(ownLog())
        })
        val send = Intent(Intent.ACTION_SEND).setType("text/plain")
            .putExtra(Intent.EXTRA_SUBJECT, "Orbit diagnostics")
            .putExtra(Intent.EXTRA_TEXT, text)
        ctx.startActivity(Intent.createChooser(send, "Share diagnostics").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
    }

    /** This app's own log lines (apps may read their own logcat without any permission). */
    private fun ownLog(): String = try {
        val p = Runtime.getRuntime().exec(arrayOf("logcat", "-d", "-v", "time", "--pid=${Process.myPid()}"))
        val out = p.inputStream.bufferedReader().readText()
        if (out.length > MAX_CHARS) out.takeLast(MAX_CHARS) else out
    } catch (e: Exception) { "(could not read log: ${e.message})" }

    private fun redact(text: String): String {
        var t = text
        val words = listOfNotNull(Hub.prefs.deviceName, Hub.prefs.pc?.name, Hub.prefs.budsName, Hub.ui.value.pcName)
            .filter { it.length >= 3 }.distinct().sortedByDescending { it.length }
        for (w in words) t = t.replace(w, "<redacted>", ignoreCase = true)
        t = t.replace(Regex("\\b([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\\b"), "<mac>")
        t = t.replace(Regex("\\b(?!127\\.0\\.0\\.1\\b)\\d{1,3}(\\.\\d{1,3}){3}\\b"), "<ip>")
        t = t.replace(Regex("\\b[0-9a-f]{64}\\b"), "<fingerprint>")
        return t
    }

    /** Deletes everything Orbit stored (pairing, keys, settings, received-file staging). The app closes. */
    fun reset(ctx: Context) {
        ctx.getSystemService(ActivityManager::class.java).clearApplicationUserData()
    }

    /** This phone's IPv4 addresses on Wi-Fi / hotspot / USB, for "Phone address" on the PC. */
    fun localAddresses(): List<String> = try {
        NetworkInterface.getNetworkInterfaces().toList()
            .filter { it.isUp && !it.isLoopback && !it.name.startsWith("rmnet") && !it.name.startsWith("dummy") }
            .flatMap { ni -> ni.inetAddresses.toList().filterIsInstance<Inet4Address>().map { it.hostAddress ?: "" } }
            .filter { it.isNotEmpty() }
    } catch (_: Exception) { emptyList() }
}
