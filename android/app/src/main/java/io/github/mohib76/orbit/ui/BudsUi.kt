package io.github.mohib76.orbit.ui

import android.annotation.SuppressLint
import android.app.StatusBarManager
import android.content.ComponentName
import android.content.Context
import android.graphics.drawable.Icon
import android.os.Build
import android.widget.Toast
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.mohib76.orbit.Conn
import io.github.mohib76.orbit.Hub
import io.github.mohib76.orbit.buds.AncMode
import io.github.mohib76.orbit.buds.Buds
import io.github.mohib76.orbit.buds.BudsProtocol
import io.github.mohib76.orbit.buds.BudsState
import io.github.mohib76.orbit.buds.BudsTileService
import io.github.mohib76.orbit.buds.Feature
import io.github.mohib76.orbit.buds.TouchAction
import io.github.mohib76.orbit.buds.TouchKey

private fun modeIcon(m: AncMode) = when (m) { AncMode.OFF -> Icons.ModeOff; AncMode.TRANSPARENCY -> Icons.ModeTrans; AncMode.ANC -> Icons.ModeAnc }

private fun budsSummary(s: BudsState, playingHere: Boolean): String {
    if (!s.connected) return "Not connected"
    val parts = mutableListOf<String>()
    if (s.battery[0] >= 0) parts += "L ${s.battery[0]}%"
    if (s.battery[1] >= 0) parts += "R ${s.battery[1]}%"
    if (playingHere) parts += "playing here"
    return parts.joinToString(" · ").ifEmpty { "Connected" }
}

/** The two buds, drawn like the design's hero illustration. */
@Composable
fun BudsArt(size: Dp, fill: Color, dot: Color) {
    Canvas(Modifier.size(size)) {
        val s = this.size.width / 64f
        fun DrawScope.bud(cx: Float, stemX: Float, dotX: Float, angle: Float) = rotate(angle, Offset(cx * s, 30 * s)) {
            drawOval(fill, Offset((cx - 11) * s, 8 * s), Size(22 * s, 20 * s))
            drawRoundRect(fill, Offset(stemX * s, 22 * s), Size(9 * s, 30 * s), CornerRadius(4.5f * s))
            drawCircle(dot, 3.4f * s, Offset(dotX * s, 17 * s))
        }
        bud(22f, 17f, 20f, -14f)
        bud(42f, 38f, 44f, 14f)
    }
}

// ============================================================== Home card

@Composable
fun BudsCard(onOpen: () -> Unit, requestBt: () -> Unit) {
    val c = LocalOrbit.current
    val ctx = LocalContext.current
    val local by Buds.local.collectAsState()
    val peer by Buds.peer.collectAsState()
    val playing by Buds.playingHere.collectAsState()
    val ui by Hub.ui.collectAsState()
    val s = Buds.effective(local, peer)
    var choose by remember { mutableStateOf(false) }

    Column(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(24.dp)).background(c.surf).padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        Row(
            Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).clickable {
                when {
                    !Buds.hasPermission(ctx) -> requestBt()
                    Hub.prefs.budsMac == null && !Buds.autoSelect() -> choose = true
                    else -> onOpen()
                }
            },
            verticalAlignment = Alignment.CenterVertically
        ) {
            IconChip(Icons.Buds)
            Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
                Text(s.name, fontSize = 15.sp, fontWeight = FontWeight.ExtraBold, color = c.text, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(
                    when {
                        !Buds.hasPermission(ctx) -> "Tap to allow Bluetooth access"
                        Hub.prefs.budsMac == null -> "Tap to choose your earbuds"
                        !local.connected && s.connected -> "Through ${ui.pcName} · " + budsSummary(s, false)
                        else -> budsSummary(s, playing)
                    },
                    fontSize = 12.sp, fontWeight = FontWeight.SemiBold, color = c.text2, maxLines = 1, overflow = TextOverflow.Ellipsis
                )
            }
            Icon(Icons.Chevron, null, Modifier.size(18.dp), tint = c.text)
        }
        NoiseSegments(s, compact = true)
    }
    if (choose) BudsChooser { choose = false }
}

@Composable
fun NoiseSegments(s: BudsState, compact: Boolean) {
    val c = LocalOrbit.current
    val on = s.connected
    Row(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(if (compact) 16.dp else 28.dp))
            .background(if (compact) c.bg else c.surf).padding(if (compact) 4.dp else 6.dp).alpha(if (on) 1f else 0.5f),
        horizontalArrangement = Arrangement.spacedBy(4.dp)
    ) {
        AncMode.entries.forEach { m ->
            val sel = m == s.anc
            Column(
                Modifier.weight(1f).height(if (compact) 44.dp else 88.dp)
                    .clip(RoundedCornerShape(if (compact) 12.dp else 22.dp))
                    .background(if (sel) c.primary else Color.Transparent)
                    .selectable(selected = sel, enabled = true, role = Role.RadioButton) { Buds.setAnc(m) },
                horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center
            ) {
                val fg = if (sel) c.onPrimary else c.text
                if (!compact) { Icon(modeIcon(m), null, Modifier.size(24.dp), tint = fg); Spacer(Modifier.height(6.dp)) }
                Text(m.label, fontSize = 13.sp, fontWeight = if (sel) FontWeight.ExtraBold else FontWeight.Bold, color = fg, maxLines = 1)
            }
        }
    }
}

// ============================================================== Buds screen

@Composable
fun BudsScreen(onBack: () -> Unit) {
    val c = LocalOrbit.current
    val local by Buds.local.collectAsState()
    val peer by Buds.peer.collectAsState()
    val playingHere by Buds.playingHere.collectAsState()
    val ui by Hub.ui.collectAsState()
    val s = Buds.effective(local, peer)
    var choose by remember { mutableStateOf(false) }
    val dim = if (s.connected) 1f else 0.5f

    Column(Modifier.fillMaxSize().background(c.bg).statusBarsPadding().navigationBarsPadding()) {
        Row(Modifier.height(64.dp).padding(horizontal = 12.dp).padding(top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(48.dp).clip(RoundedCornerShape(24.dp)).clickable(onClick = onBack), contentAlignment = Alignment.Center) {
                Icon(Icons.Back, "Back", Modifier.size(22.dp), tint = c.text)
            }
            Text("Buds", fontSize = 20.sp, fontWeight = FontWeight.ExtraBold, color = c.text)
            Spacer(Modifier.weight(1f))
            Row(
                Modifier.height(40.dp).clip(RoundedCornerShape(20.dp)).border(1.dp, c.outline, RoundedCornerShape(20.dp))
                    .clickable { choose = true }.padding(horizontal = 14.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Icon(Icons.Swap, null, Modifier.size(16.dp), tint = c.primaryText)
                Spacer(Modifier.width(6.dp))
                Text("Switch", fontSize = 13.sp, fontWeight = FontWeight.Bold, color = c.primaryText)
            }
        }

        Column(Modifier.verticalScroll(rememberScrollState()).padding(horizontal = 16.dp).padding(top = 4.dp, bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp)) {
            // Hero
            Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(28.dp)).background(c.pc).padding(18.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    BudsArt(64.dp, if (c == DarkOrbit) c.text else Color.White, LightOrbit.primary)
                    Column(Modifier.padding(start = 14.dp)) {
                        Text(s.name, fontSize = 21.sp, fontWeight = FontWeight.ExtraBold, color = c.onPc)
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Dot(if (s.connected) c.green else c.amber)
                            Spacer(Modifier.width(6.dp))
                            val pcHas = peer?.state?.connected == true && ui.conn == Conn.Connected
                            Text(
                                when {
                                    local.connected && pcHas -> "Connected to 2 devices"
                                    local.connected -> "Connected to this phone"
                                    pcHas -> "Connected to ${ui.pcName}"
                                    else -> "Not connected"
                                },
                                fontSize = 13.sp, fontWeight = FontWeight.Bold, color = c.onPc
                            )
                        }
                    }
                }
                Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(c.bg).padding(6.dp)) {
                    DeviceRow(Icons.Phone, Hub.prefs.deviceName, local.connected, playingHere) { Buds.playOn(phone = true) }
                    if (ui.conn == Conn.Connected) {
                        val p = peer
                        DeviceRow(Icons.Laptop, ui.pcName, p?.state?.connected == true, p?.playing == true) { Buds.playOn(phone = false) }
                    }
                }
            }

            // Battery
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                listOf("Left", "Right", "Case").forEachIndexed { i, label ->
                    val level = if (s.connected) s.battery[i] else -1
                    Column(Modifier.weight(1f).clip(RoundedCornerShape(20.dp)).background(c.surf).padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(label, fontSize = 12.sp, fontWeight = FontWeight.Bold, color = c.text2)
                        Text(if (level < 0) "—" else "$level%", fontSize = 24.sp, fontWeight = FontWeight.ExtraBold, color = if (level < 0) c.text2 else c.text)
                        Progress(level.coerceAtLeast(0) / 100f, c.green)
                        Text(
                            when { level < 0 && i == 2 -> "Open lid"; level < 0 -> "—"; s.charging[i] -> "Charging"; else -> if (i == 2) "In case" else "In use" },
                            fontSize = 11.sp, fontWeight = FontWeight.SemiBold, color = c.text2, maxLines = 1
                        )
                    }
                }
            }

            SectionLabel("Noise control")
            NoiseSegments(s, compact = false)
            if (s.cycleMask != 0 && TouchAction.NOISE_CONTROL.code in s.touch.values) CycleRow(s)

            val supported = Feature.entries.filter { it in s.features }
            if (supported.isNotEmpty()) {
                SectionLabel("Sound")
                Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(24.dp)).background(c.surf).alpha(dim)) {
                    supported.forEachIndexed { i, f ->
                        if (i > 0) Box(Modifier.fillMaxWidth().height(1.dp).background(c.surfHi))
                        Row(Modifier.fillMaxWidth().clickable { Buds.setFeature(f, s.features[f] != true) }.padding(horizontal = 16.dp, vertical = 12.dp),
                            verticalAlignment = Alignment.CenterVertically) {
                            Column(Modifier.weight(1f).padding(end = 12.dp)) {
                                Text(f.title, fontSize = 15.sp, fontWeight = FontWeight.Bold, color = c.text)
                                Text(f.subtitle, fontSize = 12.sp, color = c.text2, lineHeight = 16.sp)
                            }
                            OrbitSwitch(s.features[f] == true, f.title) { Buds.setFeature(f, it) }
                        }
                    }
                }
            }

            if (s.touch.isNotEmpty()) {
                SectionLabel("Tap actions")
                TapGrid(s)
            }

            Row(Modifier.padding(horizontal = 4.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Sync, null, Modifier.size(18.dp), tint = c.text2)
                Spacer(Modifier.width(12.dp))
                Text("Settings live on the buds, so changes here also show on ${ui.pcName.ifEmpty { "your PC" }}.", fontSize = 13.sp, color = c.text2, lineHeight = 18.sp)
            }
        }
    }
    if (choose) BudsChooser { choose = false }
}

@Composable
private fun Progress(value: Float, color: Color) {
    val c = LocalOrbit.current
    Box(Modifier.fillMaxWidth().height(6.dp).clip(RoundedCornerShape(3.dp)).background(c.surfHi)) {
        Box(Modifier.fillMaxWidth(value.coerceIn(0f, 1f)).height(6.dp).clip(RoundedCornerShape(3.dp)).background(color))
    }
}

@Composable
private fun DeviceRow(icon: ImageVector, name: String, connected: Boolean, playing: Boolean, onPlayHere: () -> Unit) {
    val c = LocalOrbit.current
    Row(Modifier.fillMaxWidth().height(56.dp).padding(horizontal = 10.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(36.dp).clip(RoundedCornerShape(12.dp)).background(c.surf), contentAlignment = Alignment.Center) {
            Icon(icon, null, Modifier.size(18.dp), tint = c.primaryText)
        }
        Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
            Text(name, fontSize = 14.sp, fontWeight = FontWeight.Bold, color = c.text, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(
                when { playing -> "Playing audio"; connected -> "Connected · standby"; else -> "Buds not connected here" },
                fontSize = 12.sp, fontWeight = FontWeight.SemiBold, color = if (playing) c.greenInk else c.text2
            )
        }
        if (playing) PlayingBars(c.primaryText)
        else if (connected) Box(
            Modifier.height(36.dp).clip(RoundedCornerShape(18.dp)).background(c.primary).clickable(onClick = onPlayHere).padding(horizontal = 14.dp),
            contentAlignment = Alignment.Center
        ) { Text("Play here", fontSize = 12.sp, fontWeight = FontWeight.Bold, color = c.onPrimary) }
    }
}

@Composable
private fun PlayingBars(color: Color) = Row(Modifier.height(16.dp).padding(end = 8.dp), verticalAlignment = Alignment.Bottom,
    horizontalArrangement = Arrangement.spacedBy(2.dp)) {
    listOf(8, 16, 11).forEach { h -> Box(Modifier.size(3.dp, h.dp).clip(RoundedCornerShape(2.dp)).background(color)) }
}

@Composable
private fun CycleRow(s: BudsState) {
    val c = LocalOrbit.current
    val ctx = LocalContext.current
    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(c.surf).padding(14.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Text("Touch “Noise control” cycles through", fontSize = 13.sp, fontWeight = FontWeight.Bold, color = c.text)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            AncMode.entries.forEach { m ->
                val on = s.cycleMask and m.code != 0
                Box(
                    Modifier.clip(RoundedCornerShape(10.dp)).background(if (on) c.pc else c.bg)
                        .border(1.dp, if (on) c.pc else c.outline, RoundedCornerShape(10.dp))
                        .clickable {
                            val mask = s.cycleMask xor m.code
                            if (Integer.bitCount(mask) < 2) Toast.makeText(ctx, "Keep at least two modes in the cycle", Toast.LENGTH_SHORT).show()
                            else Buds.setCycle(mask)
                        }.padding(horizontal = 14.dp, vertical = 8.dp)
                ) { Text(m.label, fontSize = 13.sp, fontWeight = FontWeight.Bold, color = if (on) c.onPc else c.text) }
            }
        }
    }
}

@Composable
private fun TapGrid(s: BudsState) {
    val c = LocalOrbit.current
    val gestures = listOf(BudsProtocol.TAP_2 to "Double tap", BudsProtocol.TAP_3 to "Triple tap", BudsProtocol.HOLD to "Touch & hold")
    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(24.dp)).background(c.surf).padding(start = 16.dp, end = 16.dp, top = 14.dp, bottom = 16.dp)
        .alpha(if (s.connected) 1f else 0.5f), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Spacer(Modifier.width(92.dp))
            Text("Left", Modifier.weight(1f), fontSize = 12.sp, fontWeight = FontWeight.ExtraBold, color = c.text2)
            Text("Right", Modifier.weight(1f), fontSize = 12.sp, fontWeight = FontWeight.ExtraBold, color = c.text2)
        }
        for ((g, title) in gestures) {
            val l = TouchKey(BudsProtocol.SIDE_LEFT, g)
            val r = TouchKey(BudsProtocol.SIDE_RIGHT, g)
            if (l !in s.touch && r !in s.touch) continue
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(title, Modifier.width(92.dp), fontSize = 13.sp, fontWeight = FontWeight.Bold, color = c.text)
                TapCell(s, l, TouchAction.SINGLE_BUD, Modifier.weight(1f))
                TapCell(s, r, TouchAction.SINGLE_BUD, Modifier.weight(1f))
            }
        }
        val both = TouchKey(BudsProtocol.SIDE_BOTH, BudsProtocol.HOLD)
        if (both in s.touch) Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            Text("Hold both", Modifier.width(92.dp), fontSize = 13.sp, fontWeight = FontWeight.Bold, color = c.text)
            TapCell(s, both, TouchAction.BOTH_BUDS, Modifier.weight(1f))
            Spacer(Modifier.weight(1f))
        }
    }
}

@Composable
private fun TapCell(s: BudsState, key: TouchKey, options: List<TouchAction>, modifier: Modifier) {
    val c = LocalOrbit.current
    var open by remember { mutableStateOf(false) }
    val code = s.touch[key]
    Box(modifier) {
        if (code == null) return@Box
        Row(
            Modifier.fillMaxWidth().height(48.dp).clip(RoundedCornerShape(14.dp)).background(if (open) c.pc else c.surfHi)
                .clickable { open = true }.padding(horizontal = 10.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text(TouchAction.fromCode(code)?.label ?: "Custom", Modifier.weight(1f), fontSize = 13.sp, fontWeight = FontWeight.SemiBold,
                color = c.text, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Icon(Icons.Chevron, null, Modifier.size(14.dp).rotate(90f), tint = c.text2)
        }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }, containerColor = c.bg) {
            options.forEach { a ->
                DropdownMenuItem(
                    text = { Text(a.label, fontWeight = if (a.code == code) FontWeight.ExtraBold else FontWeight.Normal) },
                    onClick = { Buds.setTouch(key, a); open = false },
                    leadingIcon = { if (a.code == code) Icon(Icons.Check, null, Modifier.size(18.dp), tint = c.primaryText) else Spacer(Modifier.size(18.dp)) }
                )
            }
        }
    }
}

// ============================================================== choosing buds

@SuppressLint("MissingPermission")
@Composable
fun BudsChooser(onDone: () -> Unit) {
    val c = LocalOrbit.current
    val devices = remember { Buds.bondedBuds() }
    var picked by remember { mutableStateOf(devices.indexOfFirst { it.address == Hub.prefs.budsMac }.coerceAtLeast(0)) }
    AlertDialog(
        onDismissRequest = onDone,
        title = { Text("Choose your earbuds", fontWeight = FontWeight.ExtraBold) },
        text = {
            if (devices.isEmpty()) Text("No paired Bluetooth devices. Pair your buds in Bluetooth settings first.")
            else Column(Modifier.verticalScroll(rememberScrollState())) {
                devices.forEachIndexed { i, d ->
                    Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).selectable(i == picked, role = Role.RadioButton) { picked = i }
                        .padding(vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                        RadioButton(selected = i == picked, onClick = { picked = i })
                        Column {
                            Text(d.name ?: d.address, fontWeight = FontWeight.Bold, color = c.text)
                            Text(if (Buds.isConnected(d)) "Connected" else "Paired", fontSize = 12.sp, color = c.text2)
                        }
                    }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = { devices.getOrNull(picked)?.let { Buds.select(it) }; onDone() }, enabled = devices.isNotEmpty()) {
                Text("Use these", fontWeight = FontWeight.Bold)
            }
        },
        dismissButton = { TextButton(onClick = onDone) { Text("Cancel") } },
        containerColor = c.bg
    )
}

fun requestBudsTile(ctx: Context) {
    if (Build.VERSION.SDK_INT < 33) {
        Toast.makeText(ctx, "Edit Quick Settings and drag in “Noise control”", Toast.LENGTH_LONG).show(); return
    }
    ctx.getSystemService(StatusBarManager::class.java).requestAddTileService(
        ComponentName(ctx, BudsTileService::class.java), "Noise control",
        Icon.createWithResource(ctx, io.github.mohib76.orbit.R.drawable.buds_mode_anc), ctx.mainExecutor
    ) { r ->
        if (r == StatusBarManager.TILE_ADD_REQUEST_RESULT_TILE_ALREADY_ADDED)
            Toast.makeText(ctx, "The tile is already in Quick Settings", Toast.LENGTH_SHORT).show()
    }
}
