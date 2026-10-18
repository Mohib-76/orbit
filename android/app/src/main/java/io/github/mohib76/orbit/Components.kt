package io.github.mohib76.orbit

import android.app.Activity
import android.app.Application
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.service.quicksettings.TileService
import android.widget.Toast

class OrbitApp : Application() {
    override fun onCreate() {
        super.onCreate()
        Hub.init(this)
        OrbitService.start(this)
        io.github.mohib76.orbit.buds.BudsService.start(this)
    }
}

class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        OrbitService.start(context)
        io.github.mohib76.orbit.buds.BudsService.start(context)
    }
}

/** Accept / Decline buttons on an incoming-file notification. */
class ActionReceiver : BroadcastReceiver() {
    companion object {
        const val ACCEPT = "io.github.mohib76.orbit.ACCEPT"
        const val DECLINE = "io.github.mohib76.orbit.DECLINE"
    }

    override fun onReceive(context: Context, intent: Intent) {
        val id = intent.getStringExtra("id") ?: return
        when (intent.action) {
            ACCEPT -> Transfers.accept(id)
            DECLINE -> Transfers.decline(id)
        }
        context.getSystemService(NotificationManager::class.java).cancel(id.hashCode())
    }
}

/**
 * Android only lets the app that has focus read the clipboard, so "Send clipboard" opens this
 * invisible activity for a moment, reads the clipboard once it has focus, and closes.
 */
class ClipSendActivity : Activity() {
    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (!hasFocus) return
        val cm = getSystemService(ClipboardManager::class.java)
        val text = cm.primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(this)?.toString()
        val msg = when {
            text.isNullOrEmpty() -> "Clipboard is empty"
            Hub.sendClipboard(text) -> "Sent to ${Hub.ui.value.pcName}"
            else -> "PC not connected"
        }
        Toast.makeText(this, msg, Toast.LENGTH_SHORT).show()
        finish()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        overridePendingTransition(0, 0)
    }
}

/** Quick Settings tile: one tap sends the clipboard to the PC. */
class ClipboardTile : TileService() {
    // The Intent overload is the only one before Android 14; it's only used there.
    @android.annotation.SuppressLint("StartActivityAndCollapseDeprecated")
    override fun onClick() {
        val intent = Intent(this, ClipSendActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        if (Build.VERSION.SDK_INT >= 34) {
            startActivityAndCollapse(PendingIntent.getActivity(this, 0, intent, PendingIntent.FLAG_IMMUTABLE))
        } else {
            @Suppress("DEPRECATION")
            startActivityAndCollapse(intent)
        }
    }
}
