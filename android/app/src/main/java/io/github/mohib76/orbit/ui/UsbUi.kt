package io.github.mohib76.orbit.ui

import android.content.Intent
import android.net.Uri
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CheckboxDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.ModalBottomSheet
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.mohib76.orbit.Hub
import io.github.mohib76.orbit.R
import io.github.mohib76.orbit.usb.Usb

/** The USB mode switch inside the Home connection card. Shown only while a cable is in. */
@Composable
fun UsbModeBlock() {
    val c = LocalOrbit.current
    val ctx = LocalContext.current
    val port by Usb.port.collectAsState()
    val control by Usb.control.collectAsState()
    val ui by Hub.ui.collectAsState()
    if (!port.cable) return
    var confirm by remember { mutableStateOf<String?>(null) }
    val blocked = Usb.blocked(control)
    val locked = blocked == "mirroring" || blocked == "debugOff" || blocked == "noPc"
    val switching = control.switching.ifEmpty { null }

    fun pick(mode: String) {
        if (locked || switching != null || mode == port.mode) return
        if (port.mode == Usb.TETHERING && !Hub.prefs.usbSkipConfirm) confirm = mode else Usb.request(mode)
    }

    Column(
        Modifier.fillMaxWidth().padding(top = 0.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        Box(Modifier.fillMaxWidth().height(1.dp).background(c.pcLine))
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Icon(Icons.Cable, null, Modifier.size(16.dp), tint = c.onPc)
            Text(stringResource(R.string.usb_cable), fontSize = 13.sp, fontWeight = FontWeight.ExtraBold, color = c.onPc)
        }
        Row(
            Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(c.bg).padding(4.dp).alpha(if (locked) 0.55f else 1f),
            horizontalArrangement = Arrangement.spacedBy(4.dp)
        ) {
            val labels = mapOf(Usb.CHARGING to R.string.usb_mode_charging, Usb.TETHERING to R.string.usb_mode_tethering, Usb.FILES to R.string.usb_mode_files)
            Usb.MODES.forEach { mode ->
                val sel = if (switching != null) mode == switching else mode == port.mode
                Row(
                    Modifier.weight(1f).height(44.dp).clip(RoundedCornerShape(12.dp)).background(if (sel) c.primary else Color.Transparent)
                        .selectable(selected = sel, enabled = !locked && switching == null, role = Role.RadioButton) { pick(mode) },
                    horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically
                ) {
                    if (mode == switching) { Spinner(c.pcLine, c.onPrimary); Spacer(Modifier.width(6.dp)) }
                    Text(stringResource(labels.getValue(mode)), fontSize = 13.sp, fontWeight = FontWeight.Bold, maxLines = 1,
                        color = if (sel) c.onPrimary else if (switching != null || locked) c.disabled else c.text)
                }
            }
        }
        val (icon, text, link) = when (blocked) {
            "noPc" -> Triple(Icons.Info, stringResource(R.string.usb_status_no_pc, ui.pcName), false)
            "mirroring" -> Triple(Icons.Info, stringResource(R.string.usb_status_mirroring), false)
            "debugOff" -> Triple(Icons.Info, stringResource(R.string.usb_status_debug_off), true)
            "switching" -> Triple(Icons.Switching, stringResource(R.string.usb_status_switching), false)
            else -> when (port.mode) {
                Usb.TETHERING -> Triple(Icons.Wifi, stringResource(R.string.usb_status_tethering), false)
                Usb.FILES -> Triple(Icons.Folder, stringResource(R.string.usb_status_files, ui.pcName), false)
                Usb.CHARGING -> Triple(Icons.Bolt, stringResource(R.string.usb_status_charging), false)
                else -> Triple(Icons.Info, stringResource(R.string.usb_status_other), false)
            }
        }
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Icon(icon, null, Modifier.size(14.dp), tint = c.onPc)
            Text(text, Modifier.weight(1f), fontSize = 12.sp, fontWeight = FontWeight.Bold, color = c.onPc, lineHeight = 17.sp)
            if (link) Text(stringResource(R.string.usb_how), fontSize = 12.sp, fontWeight = FontWeight.Bold, color = c.onPc,
                textDecoration = TextDecoration.Underline,
                modifier = Modifier.clickable(role = Role.Button) {
                    try { ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(AppLinks.USB_DEBUGGING))) } catch (_: Exception) { }
                })
        }
    }

    confirm?.let { target -> UsbConfirmDialog(target, ui.pcName, onDismiss = { confirm = null }) { Usb.request(target); confirm = null } }
}

@Composable
private fun Spinner(track: Color, head: Color) {
    val t = rememberInfiniteTransition(label = "spin")
    val angle by t.animateFloat(0f, 360f, infiniteRepeatable(tween(800, easing = LinearEasing), RepeatMode.Restart), label = "angle")
    Canvas(Modifier.size(12.dp).rotate(angle)) {
        val w = 2.dp.toPx()
        drawCircle(track, radius = size.minDimension / 2 - w / 2, style = Stroke(w))
        drawArc(head, -90f, 90f, false, style = Stroke(w, cap = StrokeCap.Round))
    }
}

private fun modeName(mode: String) = when (mode) {
    Usb.CHARGING -> R.string.usb_charging_only
    Usb.FILES -> R.string.usb_file_sharing
    else -> R.string.usb_tethering
}

/** "Turn off USB tethering?": leaving tethering cuts the laptop's internet when it comes through this phone. */
@Composable
fun UsbConfirmDialog(target: String, pcName: String, onDismiss: () -> Unit, onConfirm: () -> Unit) {
    val c = LocalOrbit.current
    var dontAsk by remember { mutableStateOf(false) }
    val label = stringResource(modeName(target))
    AlertDialog(
        onDismissRequest = onDismiss,
        icon = { IconChip(Icons.Wifi, 40.dp, c.amber.copy(alpha = 0.15f), c.amber) },
        title = { Text(stringResource(R.string.usb_confirm_title), fontWeight = FontWeight.ExtraBold) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Text(stringResource(R.string.usb_confirm_body, pcName, label), fontSize = 14.sp, color = c.text2, lineHeight = 20.sp)
                Text(stringResource(R.string.usb_confirm_hint), Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(c.surf).padding(12.dp),
                    fontSize = 13.sp, color = c.text2)
                Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(8.dp)).clickable { dontAsk = !dontAsk }, verticalAlignment = Alignment.CenterVertically) {
                    Checkbox(dontAsk, { dontAsk = it }, colors = CheckboxDefaults.colors(checkedColor = c.primary))
                    Text(stringResource(R.string.dont_ask_again), fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = c.text2)
                }
            }
        },
        confirmButton = {
            TextButton(onClick = { if (dontAsk) Hub.prefs.usbSkipConfirm = true; onConfirm() }) {
                Text(stringResource(R.string.usb_confirm_switch, label), fontWeight = FontWeight.Bold, color = c.primaryText)
            }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.cancel), color = c.text2) } },
        containerColor = c.bg
    )
}

/** Settings → "When the cable is connected": a bottom sheet with the three modes. Synced with the PC. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun UsbDefaultSheet(pcName: String, onDismiss: () -> Unit) {
    val c = LocalOrbit.current
    var current by remember { mutableStateOf(Hub.prefs.usbMode) }
    ModalBottomSheet(onDismissRequest = onDismiss, containerColor = c.bg) {
        Column(Modifier.padding(horizontal = 16.dp).padding(bottom = 24.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(stringResource(R.string.usb_when_connected), Modifier.padding(horizontal = 8.dp), fontSize = 20.sp,
                fontWeight = FontWeight.ExtraBold, color = c.text)
            Text(stringResource(R.string.usb_also_changes, pcName), Modifier.padding(horizontal = 8.dp).padding(bottom = 8.dp), fontSize = 13.sp, color = c.text2)
            listOf(
                Triple(Usb.CHARGING, R.string.usb_charging_only, R.string.usb_desc_charging),
                Triple(Usb.TETHERING, R.string.usb_tethering, R.string.usb_desc_tethering),
                Triple(Usb.FILES, R.string.usb_file_sharing, R.string.usb_desc_files)
            ).forEach { (mode, title, desc) ->
                val sel = mode == current
                Row(
                    Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(if (sel) c.pc else Color.Transparent)
                        .selectable(sel, role = Role.RadioButton) { current = mode; Usb.setDefault(mode); onDismiss() }
                        .padding(horizontal = 12.dp, vertical = 14.dp),
                    horizontalArrangement = Arrangement.spacedBy(14.dp)
                ) {
                    Box(Modifier.padding(top = 1.dp).size(20.dp).clip(CircleShape).border(2.dp, if (sel) c.primary else c.text2, CircleShape),
                        contentAlignment = Alignment.Center) {
                        if (sel) Box(Modifier.size(10.dp).clip(CircleShape).background(c.primary))
                    }
                    Column(Modifier.weight(1f)) {
                        Text(stringResource(title), fontSize = 15.sp, fontWeight = FontWeight.ExtraBold, color = if (sel) c.onPc else c.text)
                        Text(stringResource(desc), fontSize = 13.sp, color = if (sel) c.onPc else c.text2, lineHeight = 18.sp)
                    }
                }
            }
            Row(Modifier.padding(horizontal = 8.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Icon(Icons.Info, null, Modifier.size(14.dp), tint = c.text2)
                Text(stringResource(R.string.usb_needs_debugging), fontSize = 12.sp, color = c.text2)
            }
        }
    }
}

/** Display name of a USB mode for Settings rows. */
@Composable
fun usbModeName(mode: String) = stringResource(modeName(mode))
