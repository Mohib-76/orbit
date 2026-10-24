package io.github.mohib76.orbit.remote

import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import io.github.mohib76.orbit.Hub
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.update
import org.json.JSONObject

/** A button the PC's owner set up in Settings › Phone remote. */
data class RemoteCommand(val id: String, val name: String, val ask: Boolean)

/** What the PC lets this phone do (`orbit.remote.caps`). Empty until the PC says. */
data class RemoteCaps(
    val volume: Boolean = false,
    val power: List<String> = emptyList(),
    val commands: List<RemoteCommand> = emptyList(),
    /** The PC asks before opening a link from this phone (so "Opened on…" would be wrong). */
    val linksAsk: Boolean = false
) {
    val any: Boolean get() = volume || power.isNotEmpty() || commands.isNotEmpty()
}

/** The PC's master volume (`orbit.volume.state`); `level` -1 = no playback device. */
data class PcVolume(val level: Int, val muted: Boolean)

/** Outcome of the last command or power button: shown as "Done just now" or the PC's error on its row. */
data class RemoteResult(val op: String, val id: String?, val ok: Boolean, val error: String, val at: Long = SystemClock.elapsedRealtime())

/** Phone remote: the PC's volume, power buttons and the user's own commands. The PC decides what's allowed. */
object Remote {
    const val LOCK = "lock"
    const val SLEEP = "sleep"
    const val SHUTDOWN = "shutdown"
    const val RESTART = "restart"
    /** Power buttons that always ask first on this phone. */
    val CONFIRM = setOf(SHUTDOWN, RESTART)

    val caps = MutableStateFlow(RemoteCaps())
    val volume = MutableStateFlow<PcVolume?>(null)
    val result = MutableStateFlow<RemoteResult?>(null)

    private val main = Handler(Looper.getMainLooper())

    fun onCaps(b: JSONObject) {
        val power = b.optJSONArray("power")?.let { a -> (0 until a.length()).map { a.optString(it) } }.orEmpty()
        val commands = b.optJSONArray("commands")?.let { a ->
            (0 until a.length()).mapNotNull { i ->
                val o = a.optJSONObject(i) ?: return@mapNotNull null
                RemoteCommand(o.optString("id"), o.optString("name"), o.optBoolean("ask")).takeIf { it.id.isNotEmpty() }
            }
        }.orEmpty()
        val volumeAllowed = b.optBoolean("volume")
        caps.value = RemoteCaps(volumeAllowed, power.filter { it in listOf(LOCK, SLEEP, SHUTDOWN, RESTART) }, commands, b.optBoolean("linksAsk"))
        if (!volumeAllowed) volume.value = null
    }

    fun onVolume(b: JSONObject) {
        // While the user drags the slider, the PC's echoes of earlier positions would make it jump back.
        if (SystemClock.elapsedRealtime() - lastSetAt < 400) return
        volume.value = PcVolume(b.optInt("level", -1), b.optBoolean("muted"))
    }

    fun onResult(b: JSONObject) {
        result.value = RemoteResult(b.optString("op"), b.optString("id").ifEmpty { null }, b.optBoolean("ok"), b.optString("error"))
    }

    fun onPeerGone() {
        caps.value = RemoteCaps()
        volume.value = null
        result.value = null
    }

    // ---------------------------------------------------------------- volume

    @Volatile private var lastSetAt = 0L
    private var pendingLevel: Int? = null
    private var lastSent = 0L

    /** Called for every slider move; sends at most every 60 ms, and always the last position. */
    fun setLevel(level: Int) {
        val l = level.coerceIn(0, 100)
        lastSetAt = SystemClock.elapsedRealtime()
        volume.update { (it ?: PcVolume(l, false)).copy(level = l, muted = if (l > 0) false else it?.muted ?: false) }
        pendingLevel = l
        val wait = 60 - (SystemClock.elapsedRealtime() - lastSent)
        main.removeCallbacks(flush)
        if (wait <= 0) flush.run() else main.postDelayed(flush, wait)
    }

    private val flush = Runnable {
        val l = pendingLevel ?: return@Runnable
        pendingLevel = null
        lastSent = SystemClock.elapsedRealtime()
        Hub.link?.send("orbit.volume.set", JSONObject().put("level", l))
    }

    fun setMuted(muted: Boolean) {
        lastSetAt = SystemClock.elapsedRealtime()
        volume.update { it?.copy(muted = muted) }
        Hub.link?.send("orbit.volume.set", JSONObject().put("muted", muted))
    }

    // ---------------------------------------------------------------- power and commands

    /** Returns false when the PC isn't connected. */
    fun power(op: String): Boolean {
        val l = Hub.link ?: return false
        result.value = null
        l.send("orbit.remote.run", JSONObject().put("op", op))
        return true
    }

    fun run(cmd: RemoteCommand): Boolean {
        val l = Hub.link ?: return false
        result.value = null
        l.send("orbit.remote.run", JSONObject().put("op", "command").put("id", cmd.id))
        return true
    }
}
