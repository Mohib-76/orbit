package io.github.mohib76.orbit.buds

import android.content.ComponentName
import android.content.Context
import android.graphics.drawable.Icon
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService
import io.github.mohib76.orbit.Hub
import io.github.mohib76.orbit.R

/** Quick Settings tile: one tap cycles Off → Transparency → ANC. */
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
        if (!s.connected) {
            BudsService.start(this)
            Buds.connect()
            return
        }
        Buds.cycleAnc()
        bind()
    }

    private fun bind() {
        val tile = qsTile ?: return
        val s = Buds.effective()
        val on = Hub.prefs.budsEnabled && s.connected
        tile.icon = Icon.createWithResource(this, when (if (on) s.anc else null) {
            AncMode.ANC -> R.drawable.buds_mode_anc
            AncMode.TRANSPARENCY -> R.drawable.buds_mode_transparency
            AncMode.OFF -> R.drawable.buds_mode_off
            null -> R.drawable.buds_bud
        })
        tile.state = when {
            !Hub.prefs.budsEnabled -> Tile.STATE_UNAVAILABLE
            on && s.anc != null && s.anc != AncMode.OFF -> Tile.STATE_ACTIVE
            else -> Tile.STATE_INACTIVE
        }
        tile.label = if (on && s.anc != null) "Buds · ${s.anc.label}" else "Noise control"
        tile.subtitle = when {
            !Hub.prefs.budsEnabled -> "Turned off in Orbit"
            on -> "Tap to cycle mode"
            else -> "Not connected"
        }
        tile.updateTile()
    }
}
