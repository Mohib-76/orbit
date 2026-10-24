package io.github.mohib76.orbit.ui

import android.widget.Toast
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
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
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.ProgressBarRangeInfo
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.progressBarRangeInfo
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.setProgress
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.mohib76.orbit.Conn
import io.github.mohib76.orbit.Hub
import io.github.mohib76.orbit.R
import io.github.mohib76.orbit.remote.Remote
import io.github.mohib76.orbit.remote.RemoteCommand
import kotlinx.coroutines.delay
import kotlin.math.roundToInt

// ============================================================== volume

/**
 * The PC's volume: a mute button and a slider that follows the PC. [big] is the Remote screen's 44 dp bar;
 * otherwise it's the slim row under the music controls on Home.
 */
@Composable
fun PcVolumeRow(big: Boolean) {
    val c = LocalOrbit.current
    val v by Remote.volume.collectAsState()
    val vol = v ?: return
    if (vol.level < 0) {
        Text(stringResource(R.string.remote_no_audio), fontSize = 13.sp, color = c.text2)
        return
    }
    val muteLabel = stringResource(if (vol.muted) R.string.remote_unmute else R.string.remote_mute)
    val size = if (big) 48 else 40
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(if (big) 12.dp else 10.dp)) {
        Box(
            Modifier.size(size.dp).clip(RoundedCornerShape(if (big) 16.dp else 12.dp))
                .background(if (vol.muted) c.primary else if (big) c.pc else c.surfHi)
                .clickable(role = Role.Switch) { Remote.setMuted(!vol.muted) }
                .semantics { contentDescription = muteLabel },
            contentAlignment = Alignment.Center
        ) {
            Icon(if (vol.muted) Icons.VolumeOff else Icons.Volume, null, Modifier.size(if (big) 22.dp else 18.dp),
                tint = if (vol.muted) c.onPrimary else if (big) c.onPc else c.text)
        }
        VolumeSlider(vol.level, vol.muted, big, Modifier.weight(1f))
        if (!big) Text(if (vol.muted) "–" else "${vol.level}%", Modifier.width(38.dp), fontSize = 12.sp,
            fontFamily = FontFamily.Monospace, color = c.text2, maxLines = 1)
    }
}

@Composable
private fun VolumeSlider(level: Int, muted: Boolean, big: Boolean, modifier: Modifier) {
    val c = LocalOrbit.current
    val desc = stringResource(R.string.remote_pc_volume)
    val mutedText = stringResource(R.string.remote_muted)
    fun at(x: Float, width: Int) = ((x / width).coerceIn(0f, 1f) * 100).roundToInt()
    Canvas(
        modifier.height(if (big) 44.dp else 24.dp)
            .semantics {
                contentDescription = desc
                progressBarRangeInfo = ProgressBarRangeInfo(level / 100f, 0f..1f)
                if (muted) stateDescription = mutedText
                setProgress { f -> Remote.setLevel((f * 100).roundToInt()); true }
            }
            .pointerInput(Unit) { detectTapGestures { o -> Remote.setLevel(at(o.x, size.width)) } }
            .pointerInput(Unit) {
                detectHorizontalDragGestures(onDragStart = { o -> Remote.setLevel(at(o.x, size.width)) }) { change, _ ->
                    Remote.setLevel(at(change.position.x, size.width))
                }
            }
    ) {
        val f = level / 100f
        val fill = if (muted) c.disabled else if (big) c.primary else c.text2
        if (big) {
            val r = CornerRadius(14.dp.toPx())
            drawRoundRect(c.surfHi, cornerRadius = r)
            if (f > 0) drawRoundRect(fill, size = Size(size.width * f, size.height), cornerRadius = r)
            val x = (size.width * f - 8.dp.toPx()).coerceIn(4.dp.toPx(), size.width - 8.dp.toPx())
            drawRoundRect(Color.White, Offset(x, 10.dp.toPx()), Size(4.dp.toPx(), size.height - 20.dp.toPx()), CornerRadius(2.dp.toPx()))
        } else {
            val h = 6.dp.toPx(); val y = (size.height - h) / 2
            drawRoundRect(c.surfHi, Offset(0f, y), Size(size.width, h), CornerRadius(h / 2))
            if (f > 0) drawRoundRect(fill, Offset(0f, y), Size(size.width * f, h), CornerRadius(h / 2))
            val r = 10.dp.toPx()
            val cx = (size.width * f).coerceIn(r, size.width - r)
            drawCircle(fill, r, Offset(cx, size.height / 2))
            drawCircle(c.bg, r - 2.dp.toPx(), Offset(cx, size.height / 2))
        }
    }
}

// ============================================================== Home card

const val TOOL_TOUCHPAD = "touchpad"
const val TOOL_KEYBOARD = "keyboard"
const val TOOL_SLIDES = "slides"

/** "Control <PC>" on Home: one-tap tools, and the header opens the full Remote screen. */
@Composable
fun PcControlCard(openRemote: () -> Unit, openTool: (String) -> Unit) {
    val c = LocalOrbit.current
    val ctx = LocalContext.current
    val caps by Remote.caps.collectAsState()
    val ui by Hub.ui.collectAsState()
    if (!caps.any) return
    val parts = listOfNotNull(
        stringResource(R.string.remote_volume).takeIf { caps.volume },
        stringResource(R.string.remote_touchpad_keyboard).takeIf { caps.input },
        stringResource(R.string.slides_title).takeIf { caps.slides },
        stringResource(R.string.remote_power).takeIf { caps.power.isNotEmpty() },
        stringResource(R.string.remote_commands).takeIf { caps.commands.isNotEmpty() }
    )
    val summary = parts.mapIndexed { i, s -> if (i == 0) s else s.lowercase() }.joinToString(", ")
    val tools = buildList<Triple<ImageVector, String, () -> Unit>> {
        if (caps.input) {
            add(Triple(Icons.Touchpad, stringResource(R.string.input_touchpad)) { openTool(TOOL_TOUCHPAD) })
            add(Triple(Icons.Keyboard, stringResource(R.string.input_keyboard)) { openTool(TOOL_KEYBOARD) })
        }
        if (caps.slides) add(Triple(Icons.Slides, stringResource(R.string.remote_slides_short)) { openTool(TOOL_SLIDES) })
        if (Remote.LOCK in caps.power) {
            val locking = stringResource(R.string.remote_locking, ui.pcName)
            val offline = stringResource(R.string.pc_not_connected)
            add(Triple(Icons.Lock, stringResource(R.string.remote_lock_pc)) {
                Toast.makeText(ctx, if (Remote.power(Remote.LOCK)) locking else offline, Toast.LENGTH_SHORT).show()
            })
        }
    }
    Column(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(24.dp)).background(c.surf).padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).clickable(onClick = openRemote), verticalAlignment = Alignment.CenterVertically) {
            IconChip(Icons.Remote)
            Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
                Text(stringResource(R.string.remote_control, ui.pcName), fontSize = 15.sp, fontWeight = FontWeight.ExtraBold, color = c.text,
                    maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(summary, fontSize = 12.sp, fontWeight = FontWeight.SemiBold, color = c.text2, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            Icon(Icons.Chevron, null, Modifier.size(18.dp), tint = c.text)
        }
        if (tools.isNotEmpty()) Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            // Four slots, so tiles keep their size however many there are.
            for (i in 0 until 4) {
                val t = tools.getOrNull(i)
                if (t == null) { Spacer(Modifier.weight(1f)); continue }
                Column(
                    Modifier.weight(1f).height(68.dp).clip(RoundedCornerShape(16.dp)).background(c.bg)
                        .clickable(role = Role.Button, onClick = t.third),
                    horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(5.dp, Alignment.CenterVertically)
                ) {
                    Icon(t.first, null, Modifier.size(20.dp), tint = c.text)
                    Text(t.second, fontSize = 11.sp, fontWeight = FontWeight.Bold, color = c.text, maxLines = 1)
                }
            }
        }
    }
}

// ============================================================== Remote screen

private sealed interface Confirm {
    data class Power(val op: String) : Confirm
    data class Command(val cmd: RemoteCommand) : Confirm
}

@Composable
fun RemoteScreen(onBack: () -> Unit, openTool: (String) -> Unit) {
    val c = LocalOrbit.current
    val ctx = LocalContext.current
    val ui by Hub.ui.collectAsState()
    val caps by Remote.caps.collectAsState()
    val result by Remote.result.collectAsState()
    var confirm by remember { mutableStateOf<Confirm?>(null) }
    val connected = ui.conn == Conn.Connected
    val pc = ui.pcName

    val notConnected = stringResource(R.string.pc_not_connected)
    val powerToasts = mapOf(
        Remote.LOCK to stringResource(R.string.remote_locking, pc),
        Remote.SLEEP to stringResource(R.string.remote_sleeping, pc),
        Remote.SHUTDOWN to stringResource(R.string.remote_shutting_down, pc),
        Remote.RESTART to stringResource(R.string.remote_restarting, pc)
    )
    fun power(op: String) =
        Toast.makeText(ctx, if (Remote.power(op)) powerToasts[op] else notConnected, Toast.LENGTH_SHORT).show()
    fun run(cmd: RemoteCommand) { if (!Remote.run(cmd)) Toast.makeText(ctx, notConnected, Toast.LENGTH_SHORT).show() }

    // A refused power button (switched off on the PC meanwhile) explains itself.
    LaunchedEffect(result) {
        val r = result ?: return@LaunchedEffect
        if (!r.ok && r.op != "command" && r.error.isNotEmpty()) Toast.makeText(ctx, r.error, Toast.LENGTH_LONG).show()
    }

    Column(Modifier.fillMaxSize().background(c.bg).statusBarsPadding()) {
        Row(Modifier.height(64.dp).padding(start = 12.dp, end = 12.dp, top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(48.dp).clip(CircleShape).clickable(role = Role.Button, onClick = onBack).semantics { contentDescription = "Back" },
                contentAlignment = Alignment.Center) { Icon(Icons.Back, null, Modifier.size(22.dp), tint = c.text) }
            Column(Modifier.padding(start = 4.dp)) {
                Text(stringResource(R.string.remote_control, pc), fontSize = 20.sp, fontWeight = FontWeight.ExtraBold, color = c.text,
                    maxLines = 1, overflow = TextOverflow.Ellipsis)
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Dot(if (connected) c.green else c.amber, 7.dp)
                    Spacer(Modifier.width(6.dp))
                    Text(if (connected) stringResource(R.string.remote_connected, ui.link) else stringResource(R.string.remote_not_connected),
                        fontSize = 12.sp, fontWeight = FontWeight.Bold, color = if (connected) c.greenInk else c.text2)
                }
            }
        }

        Column(
            Modifier.fillMaxWidth().verticalScroll(rememberScrollState()).padding(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 24.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            when {
                !connected -> Note(stringResource(R.string.remote_offline, pc))
                !caps.any -> Note(stringResource(R.string.remote_nothing, pc))
            }

            if (caps.volume) Column(
                Modifier.fillMaxWidth().clip(RoundedCornerShape(24.dp)).background(c.surf).padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                val vol by Remote.volume.collectAsState()
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(stringResource(R.string.remote_pc_volume), Modifier.weight(1f), fontSize = 15.sp, fontWeight = FontWeight.ExtraBold, color = c.text)
                    vol?.takeIf { it.level >= 0 }?.let {
                        Text(if (it.muted) stringResource(R.string.remote_muted) else "${it.level}%", fontSize = 13.sp,
                            fontFamily = FontFamily.Monospace, color = c.text2)
                    }
                }
                PcVolumeRow(big = true)
                Text(stringResource(R.string.remote_volume_note), fontSize = 12.sp, color = c.text2)
            }

            val tiles = buildList {
                if (caps.input) {
                    add(Triple(Icons.Touchpad, R.string.input_touchpad, TOOL_TOUCHPAD))
                    add(Triple(Icons.Keyboard, R.string.input_keyboard, TOOL_KEYBOARD))
                }
                if (caps.slides) add(Triple(Icons.Slides, R.string.slides_title, TOOL_SLIDES))
            }
            tiles.chunked(2).forEach { row ->
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    row.forEach { (icon, label, tool) ->
                        Column(
                            Modifier.weight(1f).height(104.dp).clip(RoundedCornerShape(22.dp)).background(c.surf)
                                .clickable(enabled = connected, role = Role.Button) { openTool(tool) }.padding(16.dp),
                            verticalArrangement = Arrangement.SpaceBetween
                        ) {
                            IconChip(icon, 40.dp, c.pc, c.onPc)
                            Text(stringResource(label), fontSize = 15.sp, fontWeight = FontWeight.ExtraBold, color = c.text)
                        }
                    }
                    if (row.size == 1) Spacer(Modifier.weight(1f))
                }
            }
            if (connected && caps.inputKnown && !caps.input) Note(stringResource(R.string.remote_tools_off, pc))

            if (caps.power.isNotEmpty()) {
                SectionLabel(stringResource(R.string.remote_power))
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    caps.power.forEach { op ->
                        val danger = op in Remote.CONFIRM
                        val (icon, label) = when (op) {
                            Remote.LOCK -> Icons.Lock to R.string.remote_lock
                            Remote.SLEEP -> Icons.Moon to R.string.remote_sleep
                            Remote.SHUTDOWN -> Icons.Power to R.string.remote_shutdown
                            else -> Icons.Restart to R.string.remote_restart
                        }
                        Column(
                            Modifier.weight(1f).height(76.dp).clip(RoundedCornerShape(20.dp)).background(if (danger) c.dangerSoft else c.surf)
                                .clickable(enabled = connected, role = Role.Button) { if (danger) confirm = Confirm.Power(op) else power(op) },
                            horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(6.dp, Alignment.CenterVertically)
                        ) {
                            Icon(icon, null, Modifier.size(22.dp), tint = if (danger) c.danger else c.text)
                            Text(stringResource(label), fontSize = 13.sp, fontWeight = FontWeight.Bold, color = if (danger) c.danger else c.text, maxLines = 1)
                        }
                    }
                }
            }

            if (caps.commands.isNotEmpty()) {
                Row(Modifier.padding(start = 4.dp, end = 4.dp, top = 6.dp), verticalAlignment = Alignment.Bottom) {
                    Text(stringResource(R.string.remote_your_commands), Modifier.weight(1f), fontSize = 13.sp, fontWeight = FontWeight.ExtraBold,
                        color = c.primaryText)
                    Text(stringResource(R.string.remote_set_up_on_pc), fontSize = 12.sp, color = c.text2)
                }
                Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(24.dp)).background(c.surf).padding(6.dp)) {
                    caps.commands.forEach { cmd ->
                        CommandRow(cmd, result?.takeIf { it.op == "command" && it.id == cmd.id }, connected, pc) {
                            if (cmd.ask) confirm = Confirm.Command(cmd) else run(cmd)
                        }
                    }
                }
                Row(Modifier.padding(horizontal = 4.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    Icon(Icons.Shield, null, Modifier.padding(top = 1.dp).size(16.dp), tint = c.primaryText)
                    Text(stringResource(R.string.remote_commands_note, pc), fontSize = 12.sp, color = c.text2, lineHeight = 18.sp)
                }
            }
        }
    }

    confirm?.let { cf ->
        when (cf) {
            is Confirm.Power -> ConfirmSheet(
                icon = if (cf.op == Remote.SHUTDOWN) Icons.Power else Icons.Restart, danger = true,
                title = stringResource(if (cf.op == Remote.SHUTDOWN) R.string.remote_shutdown_confirm else R.string.remote_restart_confirm, pc),
                body = stringResource(R.string.remote_power_confirm_body, pc),
                action = stringResource(if (cf.op == Remote.SHUTDOWN) R.string.remote_shutdown else R.string.remote_restart),
                onDismiss = { confirm = null }
            ) { confirm = null; power(cf.op) }
            is Confirm.Command -> ConfirmSheet(
                icon = Icons.Terminal, danger = false,
                title = stringResource(R.string.remote_run_confirm, cf.cmd.name),
                body = stringResource(R.string.remote_run_confirm_body, pc),
                action = stringResource(R.string.remote_run),
                onDismiss = { confirm = null }
            ) { confirm = null; run(cf.cmd) }
        }
    }
}

@Composable
private fun Note(text: String) {
    val c = LocalOrbit.current
    Text(text, Modifier.fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(c.surf).padding(16.dp),
        fontSize = 14.sp, color = c.text2, lineHeight = 20.sp)
}

@Composable
private fun CommandRow(cmd: RemoteCommand, result: io.github.mohib76.orbit.remote.RemoteResult?, enabled: Boolean, pc: String, onRun: () -> Unit) {
    val c = LocalOrbit.current
    // "Done just now" (or the PC's error) shows for a moment after the PC answers.
    var shown by remember { mutableStateOf<io.github.mohib76.orbit.remote.RemoteResult?>(null) }
    LaunchedEffect(result) {
        shown = result
        if (result != null) { delay(if (result.ok) 2400 else 6000); shown = null }
    }
    val done = shown?.ok == true
    val failed = shown?.ok == false
    val note = when {
        done -> stringResource(R.string.remote_done)
        failed -> shown?.error.orEmpty()
        cmd.ask -> stringResource(R.string.remote_asks_first)
        else -> stringResource(R.string.remote_runs_on, pc)
    }
    Row(
        Modifier.fillMaxWidth().defaultMinSize(minHeight = 56.dp).clip(RoundedCornerShape(18.dp))
            .clickable(enabled = enabled, role = Role.Button, onClick = onRun).padding(10.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        IconChip(Icons.Terminal, 40.dp, c.bg, c.primaryText)
        Column(Modifier.weight(1f)) {
            Text(cmd.name, fontSize = 15.sp, fontWeight = FontWeight.Bold, color = c.text, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(note, fontSize = 12.sp, fontWeight = FontWeight.SemiBold, maxLines = 2, overflow = TextOverflow.Ellipsis,
                color = if (done) c.greenInk else if (failed) c.danger else c.text2)
        }
        Box(Modifier.size(36.dp).clip(CircleShape).background(if (done) c.green else c.primary), contentAlignment = Alignment.Center) {
            Icon(if (done) Icons.Check else Icons.Play, null, Modifier.size(14.dp), tint = if (done) Color.White else c.onPrimary)
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ConfirmSheet(
    icon: ImageVector, danger: Boolean, title: String, body: String, action: String,
    onDismiss: () -> Unit, onConfirm: () -> Unit
) {
    val c = LocalOrbit.current
    ModalBottomSheet(onDismissRequest = onDismiss, containerColor = c.bg) {
        Column(Modifier.padding(horizontal = 20.dp).padding(bottom = 28.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            IconChip(icon, 48.dp, if (danger) c.dangerSoft else c.pc, if (danger) c.danger else c.onPc)
            Text(title, fontSize = 22.sp, fontWeight = FontWeight.ExtraBold, color = c.text)
            Text(body, fontSize = 14.sp, color = c.text2, lineHeight = 20.sp)
            PillButton(action, modifier = Modifier.fillMaxWidth().padding(top = 6.dp),
                bg = if (danger) c.danger else c.primary, fg = if (danger) c.onDanger else c.onPrimary, onClick = onConfirm)
            PillButton(stringResource(R.string.cancel), modifier = Modifier.fillMaxWidth(), bg = Color.Transparent, fg = c.primaryText, onClick = onDismiss)
        }
    }
}
