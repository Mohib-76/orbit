package io.github.mohib76.orbit.mirror

import android.content.ComponentName
import android.content.Context
import android.graphics.drawable.Icon
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService
import android.widget.Toast
import io.github.mohib76.orbit.Conn
import io.github.mohib76.orbit.Hub
import io.github.mohib76.orbit.R

/** Quick Settings tile: shows when the PC is mirroring; tap stops it, or asks the PC to start. */
class MirrorTileService : TileService() {
    companion object {
        fun refresh(ctx: Context) = try {
            requestListeningState(ctx, ComponentName(ctx, MirrorTileService::class.java))
        } catch (_: Exception) { }
    }

    override fun onStartListening() = bind()

    override fun onClick() {
        val s = Mirror.state.value
        when {
            s.active -> Mirror.stop()
            !Mirror.requestStart() -> Toast.makeText(this, "Your PC isn't connected", Toast.LENGTH_SHORT).show()
            else -> Toast.makeText(this, "Opening the mirror on ${Hub.ui.value.pcName}", Toast.LENGTH_SHORT).show()
        }
        bind()
    }

    private fun bind() {
        val tile = qsTile ?: return
        val s = Mirror.state.value
        val connected = Hub.ui.value.conn == Conn.Connected
        tile.icon = Icon.createWithResource(this, R.drawable.ic_mirror)
        tile.label = "Mirror"
        tile.state = when {
            s.active -> Tile.STATE_ACTIVE
            connected -> Tile.STATE_INACTIVE
            else -> Tile.STATE_UNAVAILABLE
        }
        tile.subtitle = when {
            s.active -> "On · ${Hub.ui.value.pcName}" + if (s.viewOnly) " · view only" else ""
            connected -> "Off"
            else -> "PC not connected"
        }
        tile.updateTile()
    }
}
