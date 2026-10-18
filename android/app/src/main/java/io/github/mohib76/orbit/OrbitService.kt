package io.github.mohib76.orbit

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ServiceInfo
import android.net.Uri
import android.os.BatteryManager
import android.os.IBinder
import android.os.PowerManager
import org.json.JSONObject

/**
 * Keeps Orbit reachable: runs the TLS server, reports battery, and owns the persistent
 * "Connected to …" notification with quick actions.
 */
class OrbitService : Service() {
    companion object {
        @Volatile var instance: OrbitService? = null
        private const val CH_STATUS = "status"
        private const val CH_FILES = "files"
        private const val ID_STATUS = 1
        private const val ID_PROGRESS = 2

        fun start(ctx: Context) {
            if (Hub.prefs.pc == null && Hub.accepted == null) return
            // Background starts can be refused on Android 12+; the next foreground moment retries.
            try { ctx.startForegroundService(Intent(ctx, OrbitService::class.java)) }
            catch (e: Exception) { android.util.Log.w(Hub.TAG, "service start refused: ${e.message}") }
        }
    }

    private lateinit var server: Server
    private lateinit var nm: NotificationManager
    private var wake: PowerManager.WakeLock? = null
    private var lastBattery = ""

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        instance = this
        nm = getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(NotificationChannel(CH_STATUS, "Connection status", NotificationManager.IMPORTANCE_MIN).apply {
            setShowBadge(false)
        })
        nm.createNotificationChannel(NotificationChannel(CH_FILES, "Files", NotificationManager.IMPORTANCE_DEFAULT))
        startForeground(ID_STATUS, statusNotification(), ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
        server = Server(this).also { it.start() }
        registerReceiver(battery, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        refreshNotification()
        return START_STICKY
    }

    override fun onDestroy() {
        instance = null
        try { unregisterReceiver(battery) } catch (_: Exception) { }
        server.stop()
        wake?.let { if (it.isHeld) it.release() }
        super.onDestroy()
    }

    fun onLinkUp(l: Link) {
        lastBattery = ""
        registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED))?.let { sendBattery(it) }
        refreshNotification()
    }

    // ------------------------------------------------ battery

    private val battery = object : BroadcastReceiver() {
        override fun onReceive(c: Context, i: Intent) = sendBattery(i)
    }

    private fun sendBattery(i: Intent) {
        val l = Hub.link ?: return
        val level = i.getIntExtra(BatteryManager.EXTRA_LEVEL, -1) * 100 / maxOf(1, i.getIntExtra(BatteryManager.EXTRA_SCALE, 100))
        val status = i.getIntExtra(BatteryManager.EXTRA_STATUS, -1)
        val charging = status == BatteryManager.BATTERY_STATUS_CHARGING || status == BatteryManager.BATTERY_STATUS_FULL
        val key = "$level/$charging"
        if (key == lastBattery) return
        lastBattery = key
        val bm = getSystemService(BatteryManager::class.java)
        val remaining = if (charging) bm.computeChargeTimeRemaining() else -1L
        l.send("orbit.battery", JSONObject().put("level", level).put("charging", charging).put("chargeTimeMs", remaining))
    }

    // ------------------------------------------------ notifications

    private fun pi(intent: Intent, code: Int) =
        PendingIntent.getActivity(this, code, intent, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)

    private fun statusNotification(): Notification {
        val ui = Hub.ui.value
        val title = when (ui.conn) {
            Conn.Connected -> "Connected to ${ui.pcName}"
            Conn.Waiting -> if (Hub.prefs.pc == null) "Waiting for your PC…" else "Looking for ${ui.pcName}"
            Conn.NotPaired -> "Orbit"
        }
        val text = when (ui.conn) {
            Conn.Connected -> ui.link
            else -> "Connects over USB, your hotspot or Wi-Fi"
        }
        val open = pi(Intent(this, MainActivity::class.java), 0)
        val b = Notification.Builder(this, CH_STATUS)
            .setSmallIcon(R.drawable.ic_orbit)
            .setContentTitle(title)
            .setContentText(text)
            .setContentIntent(open)
            .setOngoing(true)
            .setShowWhen(false)
        if (ui.conn == Conn.Connected) {
            b.addAction(Notification.Action.Builder(null, "Send clipboard",
                pi(Intent(this, ClipSendActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK), 1)).build())
            b.addAction(Notification.Action.Builder(null, "Send file",
                pi(Intent(this, MainActivity::class.java).setAction(MainActivity.ACTION_PICK_FILES), 2)).build())
        }
        return b.build()
    }

    fun refreshNotification() {
        nm.notify(ID_STATUS, statusNotification())
        val running = Hub.transfers.value.any { it.state == TState.Running }
        if (running) {
            if (wake == null) wake = getSystemService(PowerManager::class.java)
                .newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "orbit:transfer").apply { setReferenceCounted(false) }
            wake?.acquire(30 * 60_000L)
        } else {
            wake?.let { if (it.isHeld) it.release() }
            nm.cancel(ID_PROGRESS)
        }
    }

    private var lastProgress = 0L
    fun progress(name: String, done: Long, size: Long, outgoing: Boolean) {
        val now = System.currentTimeMillis()
        if (now - lastProgress < 700 && done < size) return
        lastProgress = now
        if (wake?.isHeld != true) refreshNotification()
        val pct = if (size > 0) (done * 100 / size).toInt() else 0
        nm.notify(ID_PROGRESS, Notification.Builder(this, CH_STATUS)
            .setSmallIcon(R.drawable.ic_orbit)
            .setContentTitle(if (outgoing) "Sending $name" else "Receiving $name")
            .setContentText("$pct%")
            .setProgress(100, pct, false)
            .setOngoing(true).setOnlyAlertOnce(true)
            .setContentIntent(pi(Intent(this, MainActivity::class.java).setAction(MainActivity.ACTION_FILES), 3))
            .build())
        if (done >= size) nm.cancel(ID_PROGRESS)
    }

    fun notifyReceived(name: String, uri: Uri?) {
        val view = if (uri != null) Intent(Intent.ACTION_VIEW).setDataAndType(uri, contentResolver.getType(uri))
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        else Intent(this, MainActivity::class.java)
        nm.notify(name.hashCode(), Notification.Builder(this, CH_FILES)
            .setSmallIcon(R.drawable.ic_orbit)
            .setContentTitle("Received $name")
            .setContentText("From ${Hub.ui.value.pcName} · saved to Download/Orbit")
            .setContentIntent(pi(view, name.hashCode()))
            .setAutoCancel(true)
            .build())
    }

    fun notifyOffer(id: String, name: String, size: Long) {
        fun action(a: String) = PendingIntent.getBroadcast(this, (id + a).hashCode(),
            Intent(this, ActionReceiver::class.java).setAction(a).putExtra("id", id),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        nm.notify(id.hashCode(), Notification.Builder(this, CH_FILES)
            .setSmallIcon(R.drawable.ic_orbit)
            .setContentTitle("${Hub.ui.value.pcName} wants to send a file")
            .setContentText("$name · ${formatSize(size)}")
            .addAction(Notification.Action.Builder(null, "Accept", action(ActionReceiver.ACCEPT)).build())
            .addAction(Notification.Action.Builder(null, "Decline", action(ActionReceiver.DECLINE)).build())
            .setAutoCancel(true)
            .build())
    }
}

fun formatSize(b: Long): String = when {
    b < 1024 -> "$b B"
    b < 1024 * 1024 -> "%.1f KB".format(b / 1024.0)
    b < 1024L * 1024 * 1024 -> "%.1f MB".format(b / 1048576.0)
    else -> "%.2f GB".format(b / 1073741824.0)
}
