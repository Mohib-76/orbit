package io.github.mohib76.orbit.mirror

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.drawable.Icon
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.util.Log
import io.github.mohib76.orbit.Hub
import io.github.mohib76.orbit.Link
import io.github.mohib76.orbit.MainActivity
import io.github.mohib76.orbit.R
import kotlinx.coroutines.flow.MutableStateFlow
import org.json.JSONObject

/** What the PC told us about mirroring. */
data class MirrorUi(val active: Boolean = false, val viewOnly: Boolean = false, val transport: String = "", val since: Long = 0)

/** What wireless mirroring needs on this phone. */
data class DevState(val devOptions: Boolean, val wirelessDebugging: Boolean, val wifi: Boolean, val usbDebugging: Boolean, val canToggle: Boolean)

/**
 * Screen mirroring, phone side. The PC runs the stream through adb (scrcpy's server); this side makes
 * sure it never happens silently: a LIVE notification with Stop / View only, and the Mirror tile.
 */
object Mirror {
    private const val TAG = "Orbit"
    private const val CHANNEL = "mirror"
    private const val SETUP_CHANNEL = "mirror_setup"
    private const val NOTIF_ID = 30
    private const val SETUP_NOTIF_ID = 31
    const val ACTION_STOP = "io.github.mohib76.orbit.mirror.STOP"
    const val ACTION_VIEW_ONLY = "io.github.mohib76.orbit.mirror.VIEW_ONLY"
    const val ACTION_REPOST = "io.github.mohib76.orbit.mirror.REPOST"

    val state = MutableStateFlow(MirrorUi())
    /** The PC asked for the setup screen ("Open on phone"). MainActivity shows it. */
    val setupRequested = MutableStateFlow(false)
    private val main = Handler(Looper.getMainLooper())

    private val app: Context get() = Hub.app
    private val pcName: String get() = Hub.prefs.pc?.name ?: "your PC"

    // ------------------------------------------------------------------ packets from the PC

    fun onState(b: JSONObject) {
        val was = state.value
        val active = b.optBoolean("active")
        state.value = MirrorUi(active, b.optBoolean("viewOnly"), b.optString("transport"),
            if (active) (if (was.active) was.since else System.currentTimeMillis()) else 0)
        main.post { refresh() }
    }

    /** The PC asks us to get wireless debugging ready, and what's still missing. */
    fun onPrepare(l: Link) {
        var s = devState(app)
        if (s.devOptions && s.wifi && !s.wirelessDebugging && s.canToggle) {
            try {
                Settings.Global.putInt(app.contentResolver, "adb_wifi_enabled", 1)
                Log.i(TAG, "mirror: turned wireless debugging back on")
                s = s.copy(wirelessDebugging = true)
            } catch (e: Exception) { Log.w(TAG, "mirror: cannot turn on wireless debugging", e) }
        }
        sendInfo(l, s)
    }

    fun onSetupRequested(l: Link) {
        sendInfo(l, devState(app))
        setupRequested.value = true
        // Starting an activity from the background is usually blocked, so also offer a notification.
        try {
            app.startActivity(Intent(app, MainActivity::class.java).setAction(MainActivity.ACTION_MIRROR_SETUP)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        } catch (_: Exception) { }
        val open = PendingIntent.getActivity(app, 3,
            Intent(app, MainActivity::class.java).setAction(MainActivity.ACTION_MIRROR_SETUP), PendingIntent.FLAG_IMMUTABLE)
        // Its own high-importance channel so it shows as a heads-up banner the user can tap.
        app.getSystemService(NotificationManager::class.java).createNotificationChannel(
            NotificationChannel(SETUP_CHANNEL, "Mirroring setup requests", NotificationManager.IMPORTANCE_HIGH).apply {
                description = "When your PC asks this phone to set up screen mirroring"
                setShowBadge(false)
            })
        notify(SETUP_NOTIF_ID, Notification.Builder(app, SETUP_CHANNEL)
            .setSmallIcon(R.drawable.ic_mirror)
            .setContentTitle("$pcName asked to set up mirroring")
            .setContentText("Tap to continue. It takes two taps in Settings.")
            .setAutoCancel(true)
            .setContentIntent(open)
            .build())
    }

    fun setupShown() {
        setupRequested.value = false
        app.getSystemService(NotificationManager::class.java).cancel(SETUP_NOTIF_ID)
    }

    private fun sendInfo(l: Link, s: DevState) {
        l.send("orbit.mirror.info", JSONObject()
            .put("sdk", Build.VERSION.SDK_INT)
            .put("devOptions", s.devOptions)
            .put("wirelessDebugging", s.wirelessDebugging)
            .put("wifi", s.wifi)
            .put("usbDebugging", s.usbDebugging)
            .put("canToggle", s.canToggle))
    }

    fun onLinkGone() {
        // The PC stops the stream when it loses Orbit; don't leave a stale LIVE notice.
        if (state.value.active) {
            state.value = MirrorUi()
            main.post { refresh() }
        }
    }

    // ------------------------------------------------------------------ actions

    fun stop() {
        val s = state.value
        if (!s.active) return
        Hub.link?.send("orbit.mirror.cmd", JSONObject().put("op", "stop"))
        // If the PC doesn't confirm, cut wireless adb ourselves (needs WRITE_SECURE_SETTINGS).
        main.postDelayed({
            if (!state.value.active) return@postDelayed
            val d = devState(app)
            if (state.value.transport == "Wi-Fi" && d.canToggle) {
                Log.i(TAG, "mirror: PC didn't stop; restarting wireless debugging to cut the stream")
                Settings.Global.putInt(app.contentResolver, "adb_wifi_enabled", 0)
                main.postDelayed({ Settings.Global.putInt(app.contentResolver, "adb_wifi_enabled", 1) }, 2000)
                state.value = MirrorUi()
                refresh()
            }
        }, 3000)
    }

    fun setViewOnly(on: Boolean) {
        Hub.link?.send("orbit.mirror.cmd", JSONObject().put("op", "viewOnly").put("on", on))
    }

    /** From the tile: ask the PC to open its mirror window. False if the PC isn't connected. */
    fun requestStart(): Boolean {
        val l = Hub.link ?: return false
        l.send("orbit.mirror.cmd", JSONObject().put("op", "start"))
        return true
    }

    // ------------------------------------------------------------------ system state

    fun devState(ctx: Context): DevState {
        val cr = ctx.contentResolver
        val cm = ctx.getSystemService(ConnectivityManager::class.java)
        @Suppress("DEPRECATION")
        val wifi = cm.allNetworks.any { cm.getNetworkCapabilities(it)?.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) == true }
        return DevState(
            devOptions = Settings.Global.getInt(cr, Settings.Global.DEVELOPMENT_SETTINGS_ENABLED, 0) == 1,
            wirelessDebugging = Settings.Global.getInt(cr, "adb_wifi_enabled", 0) == 1,
            wifi = wifi,
            usbDebugging = Settings.Global.getInt(cr, Settings.Global.ADB_ENABLED, 0) == 1,
            canToggle = ctx.checkSelfPermission(Manifest.permission.WRITE_SECURE_SETTINGS) == PackageManager.PERMISSION_GRANTED
        )
    }

    /** Opens Developer options scrolled to (and highlighting) Wireless debugging. */
    fun openWirelessDebugging(ctx: Context) {
        val key = "toggle_adb_wireless"
        val i = Intent(Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS)
            .putExtra(":settings:fragment_args_key", key)
            .putExtra(":settings:show_fragment_args", Bundle().apply { putString(":settings:fragment_args_key", key) })
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        try { ctx.startActivity(i) } catch (_: Exception) { ctx.startActivity(Intent(Settings.ACTION_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
    }

    fun openAboutPhone(ctx: Context) =
        ctx.startActivity(Intent(Settings.ACTION_DEVICE_INFO_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))

    // ------------------------------------------------------------------ notification and tile

    private fun ensureChannel() {
        app.getSystemService(NotificationManager::class.java).createNotificationChannel(
            NotificationChannel(CHANNEL, "Screen mirroring", NotificationManager.IMPORTANCE_DEFAULT).apply {
                description = "Shown whenever your PC can see this phone's screen"
                setSound(null, null)
                enableVibration(false)
                setShowBadge(false)
            })
    }

    private fun notify(id: Int, n: Notification) = try {
        app.getSystemService(NotificationManager::class.java).notify(id, n)
    } catch (e: SecurityException) { Log.w(TAG, "mirror notification: ${e.message}") }

    fun refresh() {
        val s = state.value
        val nm = app.getSystemService(NotificationManager::class.java)
        if (s.active) {
            ensureChannel()
            notify(NOTIF_ID, build(s))
        } else nm.cancel(NOTIF_ID)
        MirrorTileService.refresh(app)
    }

    private fun pi(action: String, code: Int, on: Boolean? = null): PendingIntent {
        val i = Intent(app, MirrorReceiver::class.java).setAction(action)
        if (on != null) i.putExtra("on", on)
        return PendingIntent.getBroadcast(app, code, i, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
    }

    private fun build(s: MirrorUi): Notification {
        val text = if (s.viewOnly) "View only: $pcName can watch but not tap or type" else "$pcName can also tap and type on this phone"
        val open = PendingIntent.getActivity(app, 4, Intent(app, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
        return Notification.Builder(app, CHANNEL)
            .setSmallIcon(R.drawable.ic_mirror)
            .setColor(0xFFD93A2E.toInt())
            .setSubText("LIVE")
            .setContentTitle("Your screen is visible on $pcName")
            .setContentText(text)
            .setStyle(Notification.BigTextStyle().bigText("$text.\nBanking and password screens show as blank on the PC."))
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setUsesChronometer(true)
            .setWhen(s.since)
            .setShowWhen(true)
            .setCategory(Notification.CATEGORY_STATUS)
            .setVisibility(Notification.VISIBILITY_PUBLIC)
            .setContentIntent(open)
            // Android 14+ lets users swipe ongoing notifications away; put it back while mirroring.
            .setDeleteIntent(pi(ACTION_REPOST, 12))
            .addAction(Notification.Action.Builder(null as Icon?, "Stop", pi(ACTION_STOP, 10)).build())
            .addAction(Notification.Action.Builder(null as Icon?, if (s.viewOnly) "Allow control" else "View only",
                pi(ACTION_VIEW_ONLY, 11, !s.viewOnly)).build())
            .build()
    }
}

/** Stop / View only buttons on the mirroring notification. */
class MirrorReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        when (intent.action) {
            Mirror.ACTION_STOP -> Mirror.stop()
            Mirror.ACTION_VIEW_ONLY -> Mirror.setViewOnly(intent.getBooleanExtra("on", true))
            Mirror.ACTION_REPOST -> Mirror.refresh()
        }
    }
}
