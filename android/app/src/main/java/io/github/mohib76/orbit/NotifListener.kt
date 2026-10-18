package io.github.mohib76.orbit

import android.app.Notification
import android.content.ComponentName
import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.provider.Settings
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import android.util.Base64
import org.json.JSONObject
import java.io.ByteArrayOutputStream

/** Mirrors phone notifications to the PC and dismisses them when the PC asks. */
class NotifListener : NotificationListenerService() {
    companion object {
        @Volatile var instance: NotifListener? = null

        fun isEnabled(ctx: Context): Boolean {
            val flat = Settings.Secure.getString(ctx.contentResolver, "enabled_notification_listeners") ?: return false
            val me = ComponentName(ctx, NotifListener::class.java).flattenToString()
            return flat.split(':').any { it == me }
        }
    }

    private val iconsSent = HashSet<String>()
    private val labels = HashMap<String, String>()

    override fun onListenerConnected() {
        instance = this
        OrbitService.start(this)
    }
    override fun onListenerDisconnected() { instance = null }

    override fun onNotificationPosted(sbn: StatusBarNotification) {
        val l = Hub.link ?: return
        send(l, sbn, silent = false)
    }

    override fun onNotificationRemoved(sbn: StatusBarNotification) {
        if (!shouldMirror(sbn)) return
        Hub.link?.send("orbit.notification.removed", JSONObject().put("key", sbn.key))
    }

    /** On connect: send what's already in the shade, quietly (no toasts on the PC). */
    fun sendActive(l: Link) {
        iconsSent.clear()
        try { activeNotifications?.sortedBy { it.postTime }?.forEach { send(l, it, silent = true) } } catch (_: Exception) { }
    }

    fun dismiss(key: String) {
        try { cancelNotification(key) } catch (_: Exception) { }
    }

    private fun shouldMirror(sbn: StatusBarNotification): Boolean {
        val n = sbn.notification
        if (!Hub.prefs.mirrorNotifications) return false
        if (sbn.packageName == packageName) return false
        if (n.flags and Notification.FLAG_ONGOING_EVENT != 0) return false
        if (n.flags and Notification.FLAG_GROUP_SUMMARY != 0) return false
        if (n.flags and Notification.FLAG_FOREGROUND_SERVICE != 0) return false
        if (sbn.packageName in Hub.prefs.disabledApps) return false
        return true
    }

    private fun send(l: Link, sbn: StatusBarNotification, silent: Boolean) {
        if (!shouldMirror(sbn)) return
        val n = sbn.notification
        val ex = n.extras
        val title = ex.getCharSequence(Notification.EXTRA_TITLE)?.toString() ?: ""
        val text = (ex.getCharSequence(Notification.EXTRA_BIG_TEXT) ?: ex.getCharSequence(Notification.EXTRA_TEXT))?.toString() ?: ""
        if (title.isBlank() && text.isBlank()) return
        val pkg = sbn.packageName
        val body = JSONObject()
            .put("key", sbn.key)
            .put("pkg", pkg)
            .put("app", label(pkg))
            .put("title", title)
            .put("text", text)
            .put("time", sbn.postTime)
            .put("silent", silent || (n.flags and Notification.FLAG_ONLY_ALERT_ONCE != 0 && sbn.isUpdate()))
        if (iconsSent.add(pkg)) icon(pkg)?.let { body.put("icon", it) }
        l.send("orbit.notification", body)
    }

    private fun StatusBarNotification.isUpdate(): Boolean = notification.`when` != postTime

    private fun label(pkg: String): String = labels.getOrPut(pkg) {
        try { packageManager.getApplicationLabel(packageManager.getApplicationInfo(pkg, 0)).toString() } catch (_: Exception) { pkg }
    }

    private fun icon(pkg: String): String? = try {
        val d = packageManager.getApplicationIcon(pkg)
        val bmp = Bitmap.createBitmap(96, 96, Bitmap.Config.ARGB_8888)
        d.setBounds(0, 0, 96, 96)
        d.draw(Canvas(bmp))
        val out = ByteArrayOutputStream()
        bmp.compress(Bitmap.CompressFormat.PNG, 100, out)
        Base64.encodeToString(out.toByteArray(), Base64.NO_WRAP)
    } catch (_: Exception) { null }
}
