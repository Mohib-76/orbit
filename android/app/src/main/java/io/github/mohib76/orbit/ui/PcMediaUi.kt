package io.github.mohib76.orbit.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.mohib76.orbit.Hub
import io.github.mohib76.orbit.R
import io.github.mohib76.orbit.media.Media
import io.github.mohib76.orbit.media.MediaSnapshot
import io.github.mohib76.orbit.remote.Remote
import kotlinx.coroutines.delay

private val LiveRed = Color(0xFFD93A2E)
private val Palette = listOf(Color(0xFF1A9E5C), Color(0xFFC25A00), Color(0xFF3E5C76), Color(0xFF6A3FD9), Color(0xFF0E7490), Color(0xFFC9372C))

private fun tint(app: String) = Palette[Math.floorMod(app.hashCode(), Palette.size)]
private fun initial(app: String) = app.firstOrNull()?.uppercaseChar()?.toString() ?: "♪"

@Composable
private fun AppBadge(app: String, size: Int, radius: Int, font: Int) =
    Box(Modifier.size(size.dp).clip(RoundedCornerShape(radius.dp)).background(tint(app)), contentAlignment = Alignment.Center) {
        Text(initial(app), color = Color.White, fontSize = font.sp, fontWeight = FontWeight.ExtraBold)
    }

/** "Playing on <PC>": the PC's current player, on Home under the connection card. */
@Composable
fun PcMediaCard() {
    val c = LocalOrbit.current
    val snap by Media.peer.collectAsState()
    val pick by Media.pick.collectAsState()
    val artVersion by Media.artVersion.collectAsState()
    val settingsVersion by Hub.settingsVersion.collectAsState()
    val ui by Hub.ui.collectAsState()
    val show = remember(settingsVersion) { Hub.prefs.showPcMedia }
    val p = if (show) snap.pick(pick) else null
    if (p == null) return

    var now by remember { mutableLongStateOf(android.os.SystemClock.elapsedRealtime()) }
    LaunchedEffect(p.playing, snap.receivedAt) {
        while (p.playing) { now = android.os.SystemClock.elapsedRealtime(); delay(500) }
        now = android.os.SystemClock.elapsedRealtime()
    }
    val pos = snap.positionNow(p, now)
    val control = snap.controllable

    Column(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(24.dp)).background(c.surf).padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp)
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(stringResource(R.string.media_playing_on, ui.pcName), Modifier.weight(1f), fontSize = 12.sp, fontWeight = FontWeight.ExtraBold,
                color = c.text2, maxLines = 1, overflow = TextOverflow.Ellipsis)
            if (snap.players.size > 1) Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                snap.players.forEach { o ->
                    val sel = o.id == p.id
                    Row(
                        Modifier.height(30.dp).clip(RoundedCornerShape(15.dp))
                            .then(if (sel) Modifier.background(c.pc) else Modifier.border(1.dp, c.disabled, RoundedCornerShape(15.dp)))
                            .clickable(role = Role.Tab) { Media.choose(o.id) }
                            .padding(start = 6.dp, end = 10.dp),
                        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)
                    ) {
                        AppBadge(o.app, 18, 5, 9)
                        Text(o.app, fontSize = 12.sp, fontWeight = FontWeight.Bold, color = if (sel) c.onPc else c.text2, maxLines = 1)
                    }
                }
            }
        }

        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
            val art = remember(p.art, artVersion) { Media.art(p.art)?.asImageBitmap() }
            if (art != null) Image(art, null, Modifier.size(72.dp).clip(RoundedCornerShape(14.dp)), contentScale = ContentScale.Crop)
            else Box(Modifier.size(72.dp).clip(RoundedCornerShape(14.dp)).background(c.pc), contentAlignment = Alignment.Center) { AppBadge(p.app, 36, 10, 16) }
            Column(Modifier.weight(1f)) {
                Text(p.title.ifEmpty { p.app }, fontSize = 17.sp, fontWeight = FontWeight.ExtraBold, color = c.text, maxLines = 1, overflow = TextOverflow.Ellipsis)
                val sub = listOf(p.artist, p.album).filter { it.isNotBlank() }.joinToString(" · ")
                if (sub.isNotEmpty()) Text(sub, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = c.text2, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Row(Modifier.padding(top = 4.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    AppBadge(p.app, 16, 4, 8)
                    Text(stringResource(R.string.media_app_on_pc, p.app), fontSize = 12.sp, fontWeight = FontWeight.Bold, color = c.text2, maxLines = 1)
                }
            }
        }

        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
            val bar = if (p.live) LiveRed else if (p.playing) c.primary else c.muted
            SeekBar(if (p.live) 1f else if (p.len > 0) pos.toFloat() / p.len else 0f, bar, c.surfHi, control && p.canSeek) { f ->
                Media.command("seek", (f * p.len).toLong())
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(MediaSnapshot.clock(pos), Modifier.weight(1f), fontSize = 11.sp, fontFamily = FontFamily.Monospace, color = c.text2)
                if (p.live) Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(5.dp)) {
                    Box(Modifier.size(7.dp).clip(CircleShape).background(LiveRed))
                    Text(stringResource(R.string.media_live), fontSize = 11.sp, fontWeight = FontWeight.ExtraBold, color = LiveRed)
                } else Text(MediaSnapshot.clock(p.len), fontSize = 11.sp, fontFamily = FontFamily.Monospace, color = c.text2)
            }
        }

        if (control) Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(20.dp, Alignment.CenterHorizontally),
            verticalAlignment = Alignment.CenterVertically) {
            RoundIcon(Icons.Prev, stringResource(R.string.media_previous), 48, 24, Color.Transparent, if (p.canPrev) c.text else c.disabled, p.canPrev) {
                Media.command("prev")
            }
            val label = stringResource(if (p.playing) R.string.media_pause else R.string.media_play)
            RoundIcon(if (p.playing) Icons.Pause else Icons.Play, label, 64, 22, c.primary, c.onPrimary, if (p.playing) p.canPause else p.canPlay) {
                Media.command(if (p.playing) "pause" else "play")
            }
            RoundIcon(Icons.Next, stringResource(R.string.media_next), 48, 24, Color.Transparent, if (p.canNext) c.text else c.disabled, p.canNext) {
                Media.command("next")
            }
        }

        val caps by Remote.caps.collectAsState()
        val vol by Remote.volume.collectAsState()
        if (caps.volume && vol != null) Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Box(Modifier.fillMaxWidth().height(1.dp).background(c.surfHi))
            PcVolumeRow(big = false)
        }
    }
}

@Composable
private fun RoundIcon(icon: androidx.compose.ui.graphics.vector.ImageVector, label: String, size: Int, radius: Int, bg: Color, fg: Color,
                      enabled: Boolean, onClick: () -> Unit) =
    Box(
        Modifier.size(size.dp).clip(RoundedCornerShape(radius.dp)).background(bg)
            .clickable(enabled = enabled, role = Role.Button, onClick = onClick)
            .semantics { contentDescription = label },
        contentAlignment = Alignment.Center
    ) { Icon(icon, null, Modifier.size((size * 0.4f).dp), tint = fg) }

/** A 6 dp seek bar with a thumb when seeking is possible. Tap or drag to seek. */
@Composable
private fun SeekBar(value: Float, fill: Color, track: Color, canSeek: Boolean, onSeek: (Float) -> Unit) {
    var drag by remember { mutableStateOf<Float?>(null) }
    val shown = (drag ?: value).coerceIn(0f, 1f)
    val desc = stringResource(R.string.media_seek)
    Canvas(
        Modifier.fillMaxWidth().height(16.dp).semantics { contentDescription = desc }
            .then(if (canSeek) Modifier
                .pointerInput(Unit) { detectTapGestures { o -> onSeek((o.x / size.width).coerceIn(0f, 1f)) } }
                .pointerInput(Unit) {
                    detectHorizontalDragGestures(
                        onDragStart = { o -> drag = (o.x / size.width).coerceIn(0f, 1f) },
                        onDragEnd = { drag?.let(onSeek); drag = null },
                        onDragCancel = { drag = null }
                    ) { change, _ -> drag = (change.position.x / size.width).coerceIn(0f, 1f) }
                } else Modifier)
    ) {
        val h = 6.dp.toPx(); val y = (size.height - h) / 2
        drawRoundRect(track, Offset(0f, y), Size(size.width, h), CornerRadius(h / 2))
        val x = shown * size.width
        if (x > 0) drawRoundRect(fill, Offset(0f, y), Size(x, h), CornerRadius(h / 2))
        if (canSeek) drawCircle(fill, 8.dp.toPx(), Offset(x.coerceIn(8.dp.toPx(), size.width - 8.dp.toPx()), size.height / 2))
    }
}

