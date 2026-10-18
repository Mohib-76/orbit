package io.github.mohib76.orbit.buds

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.bluetooth.BluetoothDevice
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ServiceInfo
import android.graphics.drawable.Icon
import android.os.Build
import android.os.IBinder
import android.util.Log
import io.github.mohib76.orbit.Hub
import io.github.mohib76.orbit.MainActivity
import io.github.mohib76.orbit.R
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.launch

/**
 * Follows the buds' Bluetooth connection and shows the permanent noise-control notification.
 * Runs only while the Earbuds feature is on.
 */
class BudsService : Service() {
    companion object {
        const val ACTION_SET = "io.github.mohib76.orbit.buds.SET"
        const val ACTION_RECONNECT = "io.github.mohib76.orbit.buds.RECONNECT"
        const val ACTION_REPOST = "io.github.mohib76.orbit.buds.REPOST"
        const val EXTRA_MODE = "mode"
        private const val CHANNEL = "buds"
        private const val NOTIF_ID = 20

        fun start(ctx: Context) {
            if (!Hub.prefs.budsEnabled || !Buds.hasPermission(ctx)) return
            try { ctx.startForegroundService(Intent(ctx, BudsService::class.java)) }
            catch (e: Exception) { Log.w("OrbitBuds", "service start refused: ${e.message}") }
        }

        fun stop(ctx: Context) = ctx.stopService(Intent(ctx, BudsService::class.java))
    }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var lastShown: String? = null

    private val btReceiver = object : BroadcastReceiver() {
        override fun onReceive(c: Context, i: Intent) {
            val dev: BluetoothDevice? = if (Build.VERSION.SDK_INT >= 33)
                i.getParcelableExtra(BluetoothDevice.EXTRA_DEVICE, BluetoothDevice::class.java)
            else @Suppress("DEPRECATION") i.getParcelableExtra(BluetoothDevice.EXTRA_DEVICE)
            if (dev == null || !dev.address.equals(Hub.prefs.budsMac, ignoreCase = true)) return
            when (i.action) {
                // Give the buds a moment to bring up their profiles before opening RFCOMM.
                BluetoothDevice.ACTION_ACL_CONNECTED -> Buds.connect(attempts = 3, delayMs = 2000)
                BluetoothDevice.ACTION_ACL_DISCONNECTED -> Buds.disconnect()
            }
        }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        Buds.init(this)
        getSystemService(NotificationManager::class.java).createNotificationChannel(
            NotificationChannel(CHANNEL, "Earbuds noise control", NotificationManager.IMPORTANCE_LOW).apply { setShowBadge(false) }
        )
        val filter = IntentFilter().apply {
            addAction(BluetoothDevice.ACTION_ACL_CONNECTED)
            addAction(BluetoothDevice.ACTION_ACL_DISCONNECTED)
        }
        if (Build.VERSION.SDK_INT >= 33) registerReceiver(btReceiver, filter, RECEIVER_EXPORTED)
        else registerReceiver(btReceiver, filter)

        scope.launch {
            Buds.local.combine(Buds.peer) { l, p -> Buds.effective(l, p) }.collect { s ->
                val key = "${s.connected}|${s.name}|${s.anc}|${s.battery}|${s.charging}"
                if (key != lastShown) {
                    lastShown = key
                    getSystemService(NotificationManager::class.java).notify(NOTIF_ID, build(s))
                }
                BudsTileService.refresh(this@BudsService)
            }
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (!goForeground()) return START_NOT_STICKY
        when (intent?.action) {
            ACTION_SET -> AncMode.entries.firstOrNull { it.name == intent.getStringExtra(EXTRA_MODE) }?.let { Buds.setAnc(it) }
            ACTION_REPOST -> {
                lastShown = null
                getSystemService(NotificationManager::class.java).notify(NOTIF_ID, build(Buds.effective()))
            }
            else -> Buds.connect()
        }
        return START_STICKY
    }

    override fun onDestroy() {
        scope.cancel()
        unregisterReceiver(btReceiver)
        Buds.disconnect()
        super.onDestroy()
    }

    private fun goForeground(): Boolean = try {
        startForeground(NOTIF_ID, build(Buds.effective()), ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
        true
    } catch (e: Exception) {
        Log.e("OrbitBuds", "cannot start foreground service", e)
        stopSelf()
        false
    }

    private fun servicePi(action: String, requestCode: Int, mode: AncMode? = null): PendingIntent {
        val i = Intent(this, BudsService::class.java).setAction(action)
        if (mode != null) i.putExtra(EXTRA_MODE, mode.name)
        return PendingIntent.getForegroundService(this, requestCode, i, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
    }

    private fun build(s: BudsState): Notification {
        val open = Intent(this, MainActivity::class.java).setAction(MainActivity.ACTION_BUDS)
        val b = Notification.Builder(this, CHANNEL)
            .setSmallIcon(R.drawable.buds_headphones)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setShowWhen(false)
            .setCategory(Notification.CATEGORY_SERVICE)
            .setVisibility(Notification.VISIBILITY_PUBLIC)
            .setContentIntent(PendingIntent.getActivity(this, 0, open, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT))
            // Android 14+ lets users swipe away foreground notifications; put it straight back.
            .setDeleteIntent(servicePi(ACTION_REPOST, 10))

        if (s.connected) {
            b.setContentTitle(s.anc?.let { "Noise control: ${it.label}" } ?: s.name)
            b.setContentText(batteryText(s))
            AncMode.entries.forEachIndexed { i, m ->
                val label = if (m == s.anc) "● ${m.label}" else m.label
                b.addAction(Notification.Action.Builder(null as Icon?, label, servicePi(ACTION_SET, i, m)).build())
            }
        } else {
            b.setContentTitle("${s.name} not connected")
            b.setContentText("Connect your buds, or tap Reconnect")
            b.addAction(Notification.Action.Builder(null as Icon?, "Reconnect", servicePi(ACTION_RECONNECT, 5)).build())
        }
        return b.build()
    }
}

fun batteryText(s: BudsState): String {
    val labels = listOf("L", "R", "Case")
    val parts = labels.indices.filter { s.battery[it] >= 0 }
        .map { i -> "${labels[i]} ${s.battery[i]}%${if (s.charging[i]) "⚡" else ""}" }
    return if (parts.isEmpty()) s.name else parts.joinToString(" · ")
}
