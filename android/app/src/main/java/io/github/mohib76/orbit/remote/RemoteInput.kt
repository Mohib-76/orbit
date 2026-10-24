package io.github.mohib76.orbit.remote

import android.os.Handler
import android.os.Looper
import io.github.mohib76.orbit.Hub
import kotlinx.coroutines.flow.MutableStateFlow
import org.json.JSONArray
import org.json.JSONObject
import kotlin.math.roundToInt

/**
 * The touchpad, keyboard and slides remote (`orbit.input`, `orbit.slides`). Pointer moves and scrolls are
 * gathered and sent at most every 16 ms, so a fast swipe doesn't flood the link.
 */
object RemoteInput {
    /** The app in front on the PC ("Google Chrome"), while a screen asked for it. Empty = unknown. */
    val focusApp = MutableStateFlow("")

    /** Set by the slides remote while it's open and the user wants the volume keys: true = handled. */
    @Volatile var volumeKeys: ((up: Boolean) -> Boolean)? = null

    private val main = Handler(Looper.getMainLooper())
    private var dx = 0f
    private var dy = 0f
    private var scrollY = 0f
    private var scrollX = 0f
    private var scheduled = false

    private fun send(type: String, body: JSONObject) { Hub.link?.send(type, body) }

    fun onFocus(b: JSONObject) { focusApp.value = b.optString("app") }

    /** Asks the PC for its foreground app while a remote screen is open. */
    fun watchFocus(on: Boolean) {
        if (!on) focusApp.value = ""
        send("orbit.focus.watch", JSONObject().put("on", on))
    }

    fun onPeerGone() { focusApp.value = "" }

    // ---------------------------------------------------------------- pointer

    fun move(x: Float, y: Float) { dx += x; dy += y; schedule() }

    /** Scroll in wheel notches (1 = one click of a mouse wheel); positive = wheel up, so content follows the fingers. */
    fun scroll(notchesY: Float, notchesX: Float = 0f) { scrollY += notchesY; scrollX += notchesX; schedule() }

    private fun schedule() {
        if (scheduled) return
        scheduled = true
        main.postDelayed(flush, 16)
    }

    private val flush = Runnable {
        scheduled = false
        val mx = dx.toInt(); val my = dy.toInt()
        dx -= mx; dy -= my
        if (mx != 0 || my != 0) send("orbit.input", JSONObject().put("op", "move").put("dx", mx).put("dy", my))
        val sy = (scrollY * 120).roundToInt(); val sx = (scrollX * 120).roundToInt()
        if (sy != 0 || sx != 0) {
            scrollY -= sy / 120f; scrollX -= sx / 120f
            send("orbit.input", JSONObject().put("op", "scroll").put("dy", sy).put("dx", sx))
        }
    }

    fun button(button: String, action: String = "click") =
        send("orbit.input", JSONObject().put("op", "button").put("button", button).put("action", action))

    // ---------------------------------------------------------------- keyboard

    fun text(text: String) {
        if (text.isEmpty()) return
        text.chunked(1000).forEach { send("orbit.input", JSONObject().put("op", "text").put("text", it)) }
    }

    /** [key] null presses only the modifiers (Win alone opens Start). */
    fun key(key: String?, mods: Collection<String> = emptyList()) {
        val b = JSONObject().put("op", "key").put("mods", JSONArray(mods.toList()))
        if (key != null) b.put("key", key)
        send("orbit.input", b)
    }

    // ---------------------------------------------------------------- slides

    fun slides(op: String) = send("orbit.slides", JSONObject().put("op", op))
}
