package io.github.mohib76.orbit.buds

import android.content.ComponentName
import android.content.Context
import android.graphics.Color
import android.graphics.drawable.ColorDrawable
import android.graphics.drawable.Icon
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService
import android.view.ViewGroup
import androidx.activity.ComponentDialog
import androidx.compose.ui.platform.ComposeView
import io.github.mohib76.orbit.Hub
import io.github.mohib76.orbit.R
import io.github.mohib76.orbit.ui.BudsTilePicker
import io.github.mohib76.orbit.ui.OrbitTheme

/** Quick Settings tile: a tap opens a small picker with the buds' noise modes. */
class BudsTileService : TileService() {
    companion object {
        fun refresh(ctx: Context) = try {
            requestListeningState(ctx, ComponentName(ctx, BudsTileService::class.java))
        } catch (_: Exception) { }
    }

    override fun onStartListening() {
        Buds.init(this)
        bind()
    }

    override fun onClick() {
        Buds.init(this)
        if (!Hub.prefs.budsEnabled) return
        val s = Buds.effective()
        if (!s.control) {
            BudsService.start(this)
            Buds.connect()
            return
        }
        if (s.noiseModes.isNotEmpty()) showPicker()
    }

    private fun showPicker() {
        val dialog = ComponentDialog(this, android.R.style.Theme_DeviceDefault_Dialog_NoActionBar)
        dialog.setContentView(ComposeView(dialog.context).apply {
            setContent { OrbitTheme { BudsTilePicker { dialog.dismiss() } } }
        })
        dialog.window?.apply {
            setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))
            setLayout(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
        }
        showDialog(dialog)
    }

    private fun bind() {
        val tile = qsTile ?: return
        val s = Buds.effective()
        val on = Hub.prefs.budsEnabled && s.control && s.noiseModes.isNotEmpty()
        val mode = if (on) s.noise else null
        tile.icon = Icon.createWithResource(this, when (mode) {
            null -> R.drawable.buds_bud
            "OFF" -> R.drawable.buds_mode_off
            "TRANSPARENCY" -> R.drawable.buds_mode_transparency
            else -> R.drawable.buds_mode_anc
        })
        tile.state = when {
            !Hub.prefs.budsEnabled -> Tile.STATE_UNAVAILABLE
            mode != null && mode != "OFF" -> Tile.STATE_ACTIVE
            else -> Tile.STATE_INACTIVE
        }
        tile.label = s.noiseLabel?.takeIf { on }?.let { "Buds · $it" } ?: "Noise control"
        tile.subtitle = when {
            !Hub.prefs.budsEnabled -> "Turned off in Orbit"
            on -> "Tap to choose a mode"
            s.control || (s.connected && s.driver == null) -> "Not on these buds"
            s.connected -> "Tap to reconnect"
            else -> "Not connected"
        }
        tile.updateTile()
    }
}
