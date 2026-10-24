package io.github.mohib76.orbit.ui

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.PowerManager
import android.provider.Settings
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.graphics.drawable.toBitmap
import io.github.mohib76.orbit.ClipSendActivity
import io.github.mohib76.orbit.Conn
import io.github.mohib76.orbit.Hub
import io.github.mohib76.orbit.R
import io.github.mohib76.orbit.NotifListener
import io.github.mohib76.orbit.PairOffer
import io.github.mohib76.orbit.TState
import io.github.mohib76.orbit.TransferItem
import io.github.mohib76.orbit.Transfers
import io.github.mohib76.orbit.formatSize
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext

// ============================================================== shared helpers

private fun ago(t: Long): String {
    val m = (System.currentTimeMillis() - t) / 60_000
    return when { m < 1 -> "just now"; m < 60 -> "$m min ago"; else -> "${m / 60} h ago" }
}

private fun duration(since: Long): String {
    val m = (System.currentTimeMillis() - since) / 60_000
    return when { m < 1 -> "a moment"; m < 60 -> "$m min"; else -> "${m / 60} h ${m % 60} min" }
}

fun Context.ignoringBatteryOptimizations() =
    getSystemService(PowerManager::class.java).isIgnoringBatteryOptimizations(packageName)

fun Context.openNotificationAccess() =
    startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))

@android.annotation.SuppressLint("BatteryLife")
fun Context.askBatteryExemption() = try {
    startActivity(Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:$packageName")))
} catch (_: Exception) {
    startActivity(Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS))
}

fun Context.sendClipboardNow() = startActivity(Intent(this, ClipSendActivity::class.java))

// ============================================================== onboarding

@Composable
fun OnboardingScreen(resumeTick: Int, needsNotifPermission: Boolean, requestNotifPermission: () -> Unit, onDone: () -> Unit) {
    val c = LocalOrbit.current
    val ctx = LocalContext.current
    val listener = remember(resumeTick) { NotifListener.isEnabled(ctx) }
    val battery = remember(resumeTick) { ctx.ignoringBatteryOptimizations() }

    Column(Modifier.fillMaxSize().background(c.bg).statusBarsPadding().navigationBarsPadding().verticalScroll(rememberScrollState()).padding(24.dp)) {
        Spacer(Modifier.height(24.dp))
        OrbitLogo(56.dp)
        Spacer(Modifier.height(24.dp))
        Text("Your phone and PC,\nin one orbit", fontSize = 30.sp, fontWeight = FontWeight.ExtraBold, lineHeight = 36.sp, color = c.text)
        Spacer(Modifier.height(10.dp))
        Text("Clipboard, notifications, files and battery, shared over your USB cable, your hotspot or Wi-Fi. Encrypted, with no cloud.",
            fontSize = 15.sp, color = c.text2, lineHeight = 21.sp)
        Spacer(Modifier.height(28.dp))
        Text("A few permissions", fontSize = 16.sp, fontWeight = FontWeight.ExtraBold, color = c.text)
        Spacer(Modifier.height(8.dp))
        PermissionRow(Icons.Bell, "Notifications", "Shows the connection and file progress", !needsNotifPermission, requestNotifPermission)
        PermissionRow(Icons.Bell, "Notification access", "Mirrors your notifications to the PC", listener) { ctx.openNotificationAccess() }
        PermissionRow(Icons.Battery, "Stay connected", "Lets Orbit run when the screen is off", battery) { ctx.askBatteryExemption() }
        Spacer(Modifier.height(28.dp))
        PillButton("Continue", modifier = Modifier.fillMaxWidth(), onClick = onDone)
        Spacer(Modifier.height(8.dp))
        Text("You can change these later in Settings.", fontSize = 12.sp, color = c.text2, modifier = Modifier.align(Alignment.CenterHorizontally))
    }
}

@Composable
private fun PermissionRow(icon: ImageVector, title: String, desc: String, granted: Boolean, onGrant: () -> Unit) {
    val c = LocalOrbit.current
    Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        IconChip(icon)
        Column(Modifier.weight(1f).padding(horizontal = 14.dp)) {
            Text(title, fontSize = 15.sp, fontWeight = FontWeight.Bold, color = c.text)
            Text(desc, fontSize = 12.sp, color = c.text2)
        }
        if (granted) Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Check, null, Modifier.size(18.dp), tint = c.greenInk)
            Spacer(Modifier.width(4.dp))
            Text("Done", color = c.greenInk, fontWeight = FontWeight.Bold, fontSize = 13.sp)
        } else PillButton("Grant", filled = false, onClick = onGrant)
    }
}

// ============================================================== pairing

@Composable
fun PairScreen(onScan: () -> Unit) {
    val c = LocalOrbit.current
    val ui by Hub.ui.collectAsState()
    val waiting = ui.conn == Conn.Waiting && Hub.accepted != null
    Column(
        Modifier.fillMaxSize().background(c.bg).statusBarsPadding().navigationBarsPadding().verticalScroll(rememberScrollState()).padding(24.dp),
    ) {
        Spacer(Modifier.height(24.dp))
        OrbitLogo(48.dp)
        Spacer(Modifier.height(24.dp))
        Text(if (waiting) "Connecting to ${ui.pcName}…" else "Pair with your PC", fontSize = 28.sp, fontWeight = FontWeight.ExtraBold, color = c.text, lineHeight = 34.sp)
        Spacer(Modifier.height(10.dp))
        Text(
            if (waiting) "Keep Orbit open on the PC. Connect with a USB cable, turn on your hotspot, or use the same Wi-Fi. This finishes on its own."
            else "Open Orbit on your Windows PC. It shows a QR code.",
            fontSize = 15.sp, color = c.text2, lineHeight = 21.sp
        )
        Spacer(Modifier.height(28.dp))
        PillButton("Scan QR code", Icons.Qr, Modifier.fillMaxWidth(), onClick = onScan)
        Spacer(Modifier.height(16.dp))
        Tile(Modifier.fillMaxWidth()) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                IconChip(Icons.Usb)
                Column(Modifier.padding(start = 14.dp)) {
                    Text("Plugged in with USB?", fontWeight = FontWeight.Bold, color = c.text, fontSize = 15.sp)
                    Text("Turn on USB debugging, then click Pair over USB on the PC. No scanning needed.", fontSize = 13.sp, color = c.text2, lineHeight = 18.sp)
                }
            }
        }
        Spacer(Modifier.height(16.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Lock, null, Modifier.size(14.dp), tint = c.text2)
            Spacer(Modifier.width(6.dp))
            Text("Encrypted, with keys pinned at pairing. Nothing leaves your devices.", fontSize = 12.sp, color = c.text2)
        }
    }
}

@Composable
fun PairDialog(o: PairOffer) {
    val c = LocalOrbit.current
    AlertDialog(
        onDismissRequest = { Hub.offer.value = null },
        icon = { IconChip(Icons.Laptop, 48.dp, c.pc, c.onPc) },
        title = { Text("Pair with ${o.pcName}?", fontWeight = FontWeight.ExtraBold) },
        text = { Text("It will see your notifications, clipboard and battery, and can send you files. You can unpair at any time.", lineHeight = 20.sp) },
        confirmButton = { TextButton(onClick = { Hub.acceptOffer(o) }) { Text("Pair", fontWeight = FontWeight.Bold) } },
        dismissButton = { TextButton(onClick = { Hub.offer.value = null }) { Text("Cancel") } },
        containerColor = c.bg
    )
}

// ============================================================== main tabs

@Composable
fun MainScreen(
    tab: Int, onTab: (Int) -> Unit, resumeTick: Int, pickFiles: () -> Unit,
    showBuds: Boolean, onShowBuds: (Boolean) -> Unit, requestBt: () -> Unit
) {
    val c = LocalOrbit.current
    var filter by remember { mutableStateOf(false) }
    var about by remember { mutableStateOf(false) }
    var remote by remember { mutableStateOf(false) }
    if (remote) {
        RemoteScreen { remote = false }
        androidx.activity.compose.BackHandler { remote = false }
        return
    }
    if (filter) {
        AppFilterScreen { filter = false }
        androidx.activity.compose.BackHandler { filter = false }
        return
    }
    if (about) {
        AboutScreen { about = false }
        androidx.activity.compose.BackHandler { about = false }
        return
    }
    if (showBuds && Hub.prefs.budsEnabled) {
        BudsScreen { onShowBuds(false) }
        androidx.activity.compose.BackHandler { onShowBuds(false) }
        return
    }
    Column(Modifier.fillMaxSize().background(c.bg)) {
        Box(Modifier.weight(1f).statusBarsPadding()) {
            when (tab) {
                0 -> HomeTab(pickFiles, { onTab(1) }, { onTab(2) }, { filter = true }, { onShowBuds(true) }, requestBt) { remote = true }
                1 -> ClipboardTab()
                2 -> FilesTab(pickFiles)
                else -> SettingsTab(resumeTick, { filter = true }, requestBt) { about = true }
            }
        }
        BottomNav(tab, onTab)
    }
}

@Composable
private fun BottomNav(tab: Int, onTab: (Int) -> Unit) {
    val c = LocalOrbit.current
    val items = listOf("Home" to Icons.Home, "Clipboard" to Icons.Clip, "Files" to Icons.Folder, "Settings" to Icons.Settings)
    Row(Modifier.fillMaxWidth().background(c.nav).navigationBarsPadding().height(80.dp).padding(top = 12.dp)) {
        items.forEachIndexed { i, (label, icon) ->
            val sel = i == tab
            Column(
                Modifier.weight(1f).clickable { onTab(i) },
                horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(4.dp)
            ) {
                Box(
                    Modifier.size(64.dp, 32.dp).clip(RoundedCornerShape(16.dp)).background(if (sel) c.navInd else c.nav),
                    contentAlignment = Alignment.Center
                ) {
                    Icon(if (sel && i == 0) Icons.HomeFilled else icon, label, Modifier.size(22.dp), tint = if (sel) c.onPc else c.text2)
                }
                Text(label, fontSize = 12.sp, fontWeight = if (sel) FontWeight.ExtraBold else FontWeight.SemiBold, color = if (sel) c.text else c.text2)
            }
        }
    }
}

// ---------------------------------------------------------------- home

@Composable
private fun HomeTab(
    pickFiles: () -> Unit, openClipboard: () -> Unit, openFiles: () -> Unit, openFilter: () -> Unit,
    openBuds: () -> Unit, requestBt: () -> Unit, openRemote: () -> Unit
) {
    val c = LocalOrbit.current
    val ctx = LocalContext.current
    val ui by Hub.ui.collectAsState()
    val settingsVersion by Hub.settingsVersion.collectAsState()
    val budsOn = remember(settingsVersion) { Hub.prefs.budsEnabled }
    val clips by Hub.clips.collectAsState()
    val transfers by Hub.transfers.collectAsState()
    var mirror by remember { mutableStateOf(Hub.prefs.mirrorNotifications) }
    var tick by remember { mutableStateOf(0) }
    LaunchedEffect(Unit) { while (true) { delay(30_000); tick++ } }

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
        Row(Modifier.height(64.dp).padding(horizontal = 20.dp).padding(top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            OrbitLogo(28.dp)
            Spacer(Modifier.width(10.dp))
            Text("orbit", fontSize = 22.sp, fontWeight = FontWeight.ExtraBold, letterSpacing = (-0.8).sp, color = c.text)
        }
        Column(Modifier.padding(horizontal = 16.dp).padding(bottom = 16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            val versionWarning by Hub.versionWarning.collectAsState()
            if (versionWarning.isNotEmpty()) {
                Text(versionWarning, Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(c.surf).padding(14.dp),
                    fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = c.amber, lineHeight = 18.sp)
            }
            // Hero
            Column(
                Modifier.fillMaxWidth().clip(RoundedCornerShape(28.dp)).background(c.pc).padding(20.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    IconChip(Icons.Laptop, 52.dp, c.bg, c.primaryText)
                    Column(Modifier.padding(start = 14.dp)) {
                        Text(ui.pcName, fontSize = 22.sp, fontWeight = FontWeight.ExtraBold, color = c.onPc, maxLines = 1)
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Dot(if (ui.conn == Conn.Connected) c.green else c.amber)
                            Spacer(Modifier.width(6.dp))
                            key(tick) {
                                Text(
                                    if (ui.conn == Conn.Connected) "Connected for ${duration(ui.since)} · ${ui.link}"
                                    else "Looking for your PC… plug in USB or turn on your hotspot",
                                    fontSize = 13.sp, fontWeight = FontWeight.Bold, color = c.onPc, lineHeight = 17.sp
                                )
                            }
                        }
                    }
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    PillButton("Send clipboard", Icons.Clip, Modifier.weight(1f)) { ctx.sendClipboardNow() }
                    PillButton("Send file", Icons.Upload, Modifier.weight(1f), bg = c.bg, fg = c.primaryText, onClick = pickFiles)
                }
                UsbModeBlock()
            }

            PcMediaCard()

            if (budsOn) BudsCard(openBuds, requestBt)

            PcControlCard(openRemote)

            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Tile(Modifier.weight(1f).heightIn(min = 168.dp), onClick = openClipboard) {
                    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        IconChip(Icons.Clip)
                        Text("Clipboard", fontSize = 16.sp, fontWeight = FontWeight.ExtraBold, color = c.text)
                        val last = clips.firstOrNull()
                        Ellipsized(if (last == null) "Nothing synced yet" else "“${last.text}” ${if (last.fromPc) "from PC" else "to PC"} · ${ago(last.time)}", lines = 3)
                        Text(if (Hub.prefs.clipboardAutoReceive) "Auto-receive on" else "Auto-receive off", fontSize = 12.sp,
                            fontWeight = FontWeight.Bold, color = if (Hub.prefs.clipboardAutoReceive) c.greenInk else c.text2)
                    }
                }
                Tile(Modifier.weight(1f).heightIn(min = 168.dp)) {
                    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.Top) {
                            IconChip(Icons.Bell)
                            Spacer(Modifier.weight(1f))
                            OrbitSwitch(mirror, "Mirror notifications") { mirror = it; Hub.prefs.mirrorNotifications = it }
                        }
                        Text("Notifications", fontSize = 16.sp, fontWeight = FontWeight.ExtraBold, color = c.text)
                        Ellipsized(if (!NotifListener.isEnabled(ctx)) "Needs notification access" else if (mirror) "Mirroring to PC" else "Paused")
                        Text("Choose apps ›", fontSize = 12.sp, fontWeight = FontWeight.Bold, color = c.primaryText,
                            modifier = Modifier.clickable { if (NotifListener.isEnabled(ctx)) openFilter() else ctx.openNotificationAccess() })
                    }
                }
            }

            Tile(Modifier.fillMaxWidth(), onClick = openFiles) {
                Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        IconChip(Icons.Folder)
                        Text("Files", Modifier.padding(start = 12.dp), fontSize = 16.sp, fontWeight = FontWeight.ExtraBold, color = c.text)
                        Spacer(Modifier.weight(1f))
                        val running = transfers.count { it.state == TState.Running }
                        Text(if (running > 0) "$running sending" else "Share any file to Orbit", fontSize = 12.sp, fontWeight = FontWeight.Bold, color = c.text2)
                    }
                    transfers.firstOrNull()?.let { t ->
                        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                            Row {
                                Text("${t.name} ${if (t.outgoing) "→ PC" else "← PC"}", Modifier.weight(1f), fontSize = 13.sp,
                                    fontWeight = FontWeight.Bold, color = c.text, maxLines = 1)
                                Text(if (t.state == TState.Running) "${(t.progress * 100).toInt()}%" else t.state.name, fontSize = 13.sp, color = c.text2)
                            }
                            Progress(t.progress)
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun key(k: Any, content: @Composable () -> Unit) = androidx.compose.runtime.key(k) { content() }

// ---------------------------------------------------------------- clipboard

@Composable
private fun ClipboardTab() {
    val c = LocalOrbit.current
    val ctx = LocalContext.current
    val clips by Hub.clips.collectAsState()
    var auto by remember { mutableStateOf(Hub.prefs.clipboardAutoReceive) }
    LazyColumn(contentPadding = PaddingValues(bottom = 16.dp)) {
        item { Title("Clipboard") }
        item {
            Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Tile(Modifier.fillMaxWidth()) {
                    SettingRow("Auto-receive", "Text you copy on the PC lands on this clipboard") {
                        OrbitSwitch(auto, "Auto-receive") { auto = it; Hub.prefs.clipboardAutoReceive = it }
                    }
                }
                PillButton("Send clipboard to PC", Icons.Upload, Modifier.fillMaxWidth()) { ctx.sendClipboardNow() }
                Text("Tip: add the “Send clipboard” tile to Quick Settings, or share any text to Orbit.",
                    fontSize = 12.sp, color = c.text2, modifier = Modifier.padding(horizontal = 4.dp))
                SectionLabel("Recent")
                if (clips.isEmpty()) Text("Nothing synced yet.", fontSize = 13.sp, color = c.text2, modifier = Modifier.padding(4.dp))
            }
        }
        items(clips) { e ->
            Tile(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 5.dp), onClick = {
                ctx.getSystemService(ClipboardManager::class.java).setPrimaryClip(ClipData.newPlainText("Orbit", e.text))
            }) {
                Column {
                    Ellipsized(e.text, 14, c.text, lines = 4)
                    Spacer(Modifier.height(6.dp))
                    Text("${if (e.fromPc) "PC → Phone" else "Phone → PC"} · ${ago(e.time)} · tap to copy", fontSize = 12.sp, color = c.text2)
                }
            }
        }
    }
}

// ---------------------------------------------------------------- files

@Composable
private fun FilesTab(pickFiles: () -> Unit) {
    val c = LocalOrbit.current
    val transfers by Hub.transfers.collectAsState()
    LazyColumn(contentPadding = PaddingValues(bottom = 16.dp)) {
        item {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Title("Files", Modifier.weight(1f))
                if (transfers.any { !it.active }) TextButton(onClick = { Hub.clearFinished() }, modifier = Modifier.padding(end = 8.dp, top = 10.dp)) {
                    Text("Clear finished", color = c.primaryText, fontWeight = FontWeight.Bold)
                }
            }
        }
        item {
            Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                PillButton("Send files to PC", Icons.Upload, Modifier.fillMaxWidth(), onClick = pickFiles)
                Text("Files from the PC are saved to Download/Orbit. You can also share any file to Orbit from another app.",
                    fontSize = 12.sp, color = c.text2, modifier = Modifier.padding(horizontal = 4.dp))
                if (transfers.isEmpty()) Text("No transfers yet.", fontSize = 13.sp, color = c.text2, modifier = Modifier.padding(4.dp))
            }
        }
        items(transfers, key = { it.id }) { t -> TransferRow(t) }
    }
}

@Composable
private fun TransferRow(t: TransferItem) {
    val c = LocalOrbit.current
    val ctx = LocalContext.current
    Tile(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 5.dp)) {
        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(40.dp).clip(RoundedCornerShape(12.dp)).background(c.surfHi), contentAlignment = Alignment.Center) {
                    Text(t.name.substringAfterLast('.', "file").take(4).uppercase(), fontSize = 10.sp, fontWeight = FontWeight.ExtraBold, color = c.primaryText)
                }
                Column(Modifier.weight(1f).padding(horizontal = 12.dp)) {
                    Ellipsized(t.name, 14, c.text, bold = true)
                    Text(detail(t), fontSize = 12.sp, color = c.text2)
                }
                when {
                    t.state == TState.Waiting -> {
                        TextButton(onClick = { Transfers.accept(t.id) }) { Text("Accept", fontWeight = FontWeight.Bold, color = c.primaryText) }
                    }
                    t.state == TState.Done && t.savedUri != null -> TextButton(onClick = {
                        try {
                            ctx.startActivity(Intent(Intent.ACTION_VIEW).setDataAndType(t.savedUri, ctx.contentResolver.getType(t.savedUri))
                                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION))
                        } catch (_: Exception) { }
                    }) { Text("Open", fontWeight = FontWeight.Bold, color = c.primaryText) }
                    t.state == TState.Failed || t.state == TState.Paused ->
                        TextButton(onClick = { Transfers.retry(t) }) { Text("Retry", fontWeight = FontWeight.Bold, color = c.primaryText) }
                }
                if (t.active) Box(Modifier.size(40.dp).clip(RoundedCornerShape(20.dp)).clickable {
                    if (t.state == TState.Waiting) Transfers.decline(t.id) else Transfers.cancel(t.id)
                }, contentAlignment = Alignment.Center) {
                    Icon(Icons.Close, "Cancel", Modifier.size(16.dp), tint = c.text2)
                }
            }
            if (t.active) Progress(t.progress)
        }
    }
}

private fun detail(t: TransferItem): String = when (t.state) {
    TState.Running -> "${(t.progress * 100).toInt()}% · ${formatSize(t.speed.toLong())}/s"
    TState.Queued -> "Queued · ${formatSize(t.size)}"
    TState.Waiting -> "Waiting for you · ${formatSize(t.size)}"
    TState.Paused -> "Paused at ${(t.progress * 100).toInt()}% · resumes when connected"
    TState.Done -> "${formatSize(t.size)} · ${if (t.outgoing) "sent to PC" else "saved to Download/Orbit"}"
    TState.Failed -> "Failed"
    TState.Cancelled -> "Cancelled"
}

// ---------------------------------------------------------------- settings

@Composable
private fun SettingsTab(resumeTick: Int, openFilter: () -> Unit, requestBt: () -> Unit, openAbout: () -> Unit) {
    val c = LocalOrbit.current
    val ctx = LocalContext.current
    val ui by Hub.ui.collectAsState()
    val settingsVersion by Hub.settingsVersion.collectAsState()
    val p = Hub.prefs
    var autoClip by remember { mutableStateOf(p.clipboardAutoReceive) }
    var mirror by remember { mutableStateOf(p.mirrorNotifications) }
    var autoAccept by remember { mutableStateOf(p.autoAcceptFiles) }
    val usbDefault = remember(settingsVersion) { p.usbMode }
    var usbSheet by remember { mutableStateOf(false) }
    var showPcMedia by remember(settingsVersion) { mutableStateOf(p.showPcMedia) }
    var pcControl by remember(settingsVersion) { mutableStateOf(p.pcControlsMedia) }
    val budsOn = remember(settingsVersion) { p.budsEnabled }
    val budsLocal by io.github.mohib76.orbit.buds.Buds.local.collectAsState()
    var chooseBuds by remember { mutableStateOf(false) }
    var renaming by remember { mutableStateOf(false) }
    var confirmUnpair by remember { mutableStateOf(false) }
    var confirmReset by remember { mutableStateOf(false) }
    var chooseTheme by remember { mutableStateOf(false) }
    val theme by Hub.theme.collectAsState()
    val addresses = remember(resumeTick) { io.github.mohib76.orbit.Diagnostics.localAddresses() }
    val listener = remember(resumeTick) { NotifListener.isEnabled(ctx) }
    val battery = remember(resumeTick) { ctx.ignoringBatteryOptimizations() }

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
        Title("Settings")
        Column(Modifier.padding(horizontal = 16.dp).padding(bottom = 16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            // Paired PC
            Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(24.dp)).background(c.surf).padding(14.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    IconChip(Icons.Laptop, 44.dp, c.pc, c.onPc)
                    Column(Modifier.padding(start = 12.dp)) {
                        Text(ui.pcName, fontSize = 15.sp, fontWeight = FontWeight.ExtraBold, color = c.text)
                        Text(
                            (if (ui.conn == Conn.Connected) "Connected · ${ui.link}" else "Not connected") +
                                (p.pc?.let { " · paired " + java.text.SimpleDateFormat("d MMM", java.util.Locale.getDefault()).format(java.util.Date(it.pairedAt)) } ?: ""),
                            fontSize = 12.sp, fontWeight = FontWeight.SemiBold, color = if (ui.conn == Conn.Connected) c.greenInk else c.text2
                        )
                    }
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    PillButton("Rename", modifier = Modifier.weight(1f), filled = false) { renaming = true }
                    PillButton("Unpair", modifier = Modifier.weight(1f), filled = false, fg = c.danger) { confirmUnpair = true }
                }
            }

            Column {
                SectionLabel(stringResource(R.string.usb_section))
                SettingRow(stringResource(R.string.usb_when_connected), stringResource(R.string.usb_synced_with, ui.pcName),
                    onClick = { usbSheet = true }) {
                    Text(usbModeName(usbDefault), fontSize = 13.sp, fontWeight = FontWeight.Bold, color = c.primaryText)
                    Spacer(Modifier.width(4.dp))
                    Icon(Icons.Chevron, null, Modifier.size(18.dp), tint = c.text2)
                }
            }

            Column {
                SectionLabel(stringResource(R.string.media_section))
                SettingRow(stringResource(R.string.media_show_pc), stringResource(R.string.media_show_pc_desc)) {
                    val label = stringResource(R.string.media_show_pc)
                    OrbitSwitch(showPcMedia, label) { showPcMedia = it; io.github.mohib76.orbit.media.Media.setShowPc(it) }
                }
                SettingRow(stringResource(R.string.media_pc_control), stringResource(R.string.media_pc_control_desc, ui.pcName)) {
                    val label = stringResource(R.string.media_pc_control)
                    OrbitSwitch(pcControl, label) { pcControl = it; io.github.mohib76.orbit.media.Media.setPcControl(it) }
                }
            }

            Column {
                SectionLabel("Connection")
                SettingRow("Developer options", "Open to turn on USB debugging", onClick = {
                    try { ctx.startActivity(Intent(Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS)) } catch (_: Exception) { }
                }) { Icon(Icons.Chevron, null, Modifier.size(18.dp), tint = c.text2) }
                SettingRow("Wireless screen mirroring", "Set up mirroring to your PC without a cable", onClick = {
                    ctx.startActivity(Intent(ctx, io.github.mohib76.orbit.MainActivity::class.java)
                        .setAction(io.github.mohib76.orbit.MainActivity.ACTION_MIRROR_SETUP).putExtra("fromPc", false))
                }) { Icon(Icons.Chevron, null, Modifier.size(18.dp), tint = c.text2) }
                SettingRow("Stay connected in the background", if (battery) "Battery optimization is off for Orbit" else "Tap to let Orbit run with the screen off",
                    onClick = if (battery) null else ({ ctx.askBatteryExemption() })) {
                    if (battery) Icon(Icons.Check, null, Modifier.size(18.dp), tint = c.greenInk) else Icon(Icons.Chevron, null, Modifier.size(18.dp), tint = c.text2)
                }
                SettingRow("This phone's address",
                    if (addresses.isEmpty()) "Not on a network right now"
                    else addresses.joinToString(" · ") + "\nIf your PC can't find this phone, enter this in Orbit on the PC (Settings → Phone address).")
            }

            Column {
                SectionLabel("Earbuds")
                SettingRow("Earbuds", "Battery and playback for any buds, here and on ${ui.pcName}. Noise control, sound and tap settings for realme, OnePlus and OPPO buds") {
                    OrbitSwitch(budsOn, "Earbuds") { on ->
                        if (on && !io.github.mohib76.orbit.buds.Buds.hasPermission(ctx)) requestBt()
                        else Hub.setBudsEnabled(on)
                    }
                }
                if (budsOn) {
                    SettingRow("Your buds", p.budsName ?: "Not chosen yet", onClick = {
                        if (io.github.mohib76.orbit.buds.Buds.hasPermission(ctx)) chooseBuds = true else requestBt()
                    }) {
                        Text(if (budsLocal.connected) "Connected" else "Change", fontSize = 13.sp, fontWeight = FontWeight.Bold,
                            color = if (budsLocal.connected) c.greenInk else c.primaryText)
                    }
                    SettingRow("Quick Settings tile", "One tap cycles Off → Transparency → ANC", onClick = { requestBudsTile(ctx) }) {
                        Icon(Icons.Plus, null, Modifier.size(18.dp), tint = c.text2)
                    }
                }
            }

            Column {
                SectionLabel("Clipboard")
                SettingRow("Auto-receive", "Put text copied on the PC on this clipboard") {
                    OrbitSwitch(autoClip, "Auto-receive clipboard") { autoClip = it; p.clipboardAutoReceive = it }
                }
            }

            Column {
                SectionLabel("Notifications & files")
                SettingRow("Notification access", if (listener) "Granted" else "Needed to mirror notifications",
                    onClick = { ctx.openNotificationAccess() }) {
                    if (listener) Icon(Icons.Check, null, Modifier.size(18.dp), tint = c.greenInk) else Icon(Icons.Chevron, null, Modifier.size(18.dp), tint = c.text2)
                }
                SettingRow("Mirror notifications", "Show this phone's notifications on the PC") {
                    OrbitSwitch(mirror, "Mirror notifications") { mirror = it; p.mirrorNotifications = it }
                }
                SettingRow("Mirrored apps", "${p.disabledApps.size} apps turned off", onClick = openFilter) {
                    Icon(Icons.Chevron, null, Modifier.size(18.dp), tint = c.text2)
                }
                SettingRow("Auto-accept from ${ui.pcName}", "Saved to Download/Orbit") {
                    OrbitSwitch(autoAccept, "Auto-accept files") { autoAccept = it; p.autoAcceptFiles = it }
                }
            }

            Column {
                SectionLabel("Appearance")
                SettingRow("Theme", when (theme) { "light" -> "Light"; "dark" -> "Dark"; else -> "System default" }, onClick = { chooseTheme = true }) {
                    Icon(Icons.Chevron, null, Modifier.size(18.dp), tint = c.text2)
                }
            }

            Column {
                SectionLabel("Privacy & about")
                SettingRow("Encryption", "On · TLS with keys pinned at pairing · no cloud")
                SettingRow("This phone", "${p.deviceName} · ${p.model}")
                SettingRow("About Orbit", "Version ${appVersion(ctx)} · licenses", onClick = openAbout) {
                    Icon(Icons.Chevron, null, Modifier.size(18.dp), tint = c.text2)
                }
                SettingRow("Export diagnostics", "Share a log for bug reports, with names and addresses removed",
                    onClick = { io.github.mohib76.orbit.Diagnostics.share(ctx) }) {
                    Icon(Icons.Chevron, null, Modifier.size(18.dp), tint = c.text2)
                }
                SettingRow("Reset Orbit", "Forget the PC, keys and settings on this phone", titleColor = c.danger, onClick = { confirmReset = true })
            }
        }
    }

    if (renaming) {
        var name by remember { mutableStateOf(ui.pcName) }
        AlertDialog(
            onDismissRequest = { renaming = false },
            title = { Text("Rename PC", fontWeight = FontWeight.ExtraBold) },
            text = {
                OutlinedTextField(name, { name = it }, singleLine = true, label = { Text("Name") },
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done))
            },
            confirmButton = { TextButton(onClick = { if (name.isNotBlank()) Hub.renamePc(name.trim()); renaming = false }) { Text("Save") } },
            dismissButton = { TextButton(onClick = { renaming = false }) { Text("Cancel") } },
            containerColor = c.bg
        )
    }
    if (chooseBuds) BudsChooser { chooseBuds = false }
    if (usbSheet) UsbDefaultSheet(ui.pcName) { usbSheet = false }
    if (chooseTheme) {
        AlertDialog(
            onDismissRequest = { chooseTheme = false },
            title = { Text("Theme", fontWeight = FontWeight.ExtraBold) },
            text = {
                Column {
                    listOf("system" to "System default", "light" to "Light", "dark" to "Dark").forEach { (id, label) ->
                        Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).clickable {
                            p.theme = id; Hub.theme.value = id; chooseTheme = false
                        }.padding(vertical = 12.dp, horizontal = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                            Text(label, Modifier.weight(1f), fontSize = 15.sp, fontWeight = if (id == theme) FontWeight.ExtraBold else FontWeight.Normal)
                            if (id == theme) Icon(Icons.Check, null, Modifier.size(18.dp), tint = c.primaryText)
                        }
                    }
                }
            },
            confirmButton = { TextButton(onClick = { chooseTheme = false }) { Text("Close") } },
            containerColor = c.bg
        )
    }
    if (confirmReset) {
        AlertDialog(
            onDismissRequest = { confirmReset = false },
            title = { Text("Reset Orbit?", fontWeight = FontWeight.ExtraBold) },
            text = { Text("This forgets your PC, this phone's keys and all settings. Orbit closes; open it again to set up. Files you received stay in Download/Orbit.") },
            confirmButton = { TextButton(onClick = { confirmReset = false; io.github.mohib76.orbit.Diagnostics.reset(ctx) }) { Text("Reset", color = c.danger) } },
            dismissButton = { TextButton(onClick = { confirmReset = false }) { Text("Cancel") } },
            containerColor = c.bg
        )
    }
    if (confirmUnpair) {
        AlertDialog(
            onDismissRequest = { confirmUnpair = false },
            title = { Text("Unpair ${ui.pcName}?", fontWeight = FontWeight.ExtraBold) },
            text = { Text("You'll need to scan the QR code again to reconnect.") },
            confirmButton = { TextButton(onClick = { confirmUnpair = false; Hub.unpair() }) { Text("Unpair", color = c.danger) } },
            dismissButton = { TextButton(onClick = { confirmUnpair = false }) { Text("Cancel") } },
            containerColor = c.bg
        )
    }
}

private fun appVersion(ctx: Context) = try { ctx.packageManager.getPackageInfo(ctx.packageName, 0).versionName ?: "" } catch (_: Exception) { "" }

// ---------------------------------------------------------------- app filter

private data class AppInfo(val pkg: String, val label: String, val icon: ImageBitmap?)

@Composable
fun AppFilterScreen(onBack: () -> Unit) {
    val c = LocalOrbit.current
    val ctx = LocalContext.current
    var query by remember { mutableStateOf("") }
    var disabled by remember { mutableStateOf(Hub.prefs.disabledApps) }
    val apps by produceState<List<AppInfo>?>(null) {
        value = withContext(Dispatchers.IO) {
            val pm = ctx.packageManager
            val intent = Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER)
            pm.queryIntentActivities(intent, PackageManager.MATCH_ALL)
                .map { it.activityInfo.applicationInfo }
                .distinctBy { it.packageName }
                .filter { it.packageName != ctx.packageName }
                .map { ai ->
                    AppInfo(ai.packageName, pm.getApplicationLabel(ai).toString(),
                        try { pm.getApplicationIcon(ai).toBitmap(96, 96).asImageBitmap() } catch (_: Exception) { null })
                }
                .sortedBy { it.label.lowercase() }
        }
    }

    Column(Modifier.fillMaxSize().background(c.bg).statusBarsPadding().navigationBarsPadding()) {
        Row(Modifier.height(64.dp).padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(48.dp).clip(RoundedCornerShape(24.dp)).clickable(onClick = onBack), contentAlignment = Alignment.Center) {
                Icon(Icons.Back, "Back", Modifier.size(22.dp), tint = c.text)
            }
            Text("Mirrored apps", fontSize = 22.sp, fontWeight = FontWeight.ExtraBold, color = c.text)
        }
        OutlinedTextField(
            query, { query = it }, Modifier.fillMaxWidth().padding(horizontal = 16.dp),
            singleLine = true, placeholder = { Text("Search apps") },
            leadingIcon = { Icon(Icons.Search, null, Modifier.size(20.dp)) }, shape = RoundedCornerShape(28.dp)
        )
        val list = apps
        if (list == null) {
            Text("Loading apps…", Modifier.padding(24.dp), color = c.text2)
        } else {
            val shown = list.filter { query.isBlank() || it.label.contains(query, true) }
            Text("Mirroring ${list.count { it.pkg !in disabled }} of ${list.size} apps", Modifier.padding(horizontal = 20.dp, vertical = 10.dp),
                fontSize = 13.sp, color = c.text2, fontWeight = FontWeight.SemiBold)
            LazyColumn(contentPadding = PaddingValues(horizontal = 16.dp, vertical = 4.dp)) {
                items(shown, key = { it.pkg }) { app ->
                    Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                        if (app.icon != null) Image(app.icon, null, Modifier.size(40.dp).clip(RoundedCornerShape(12.dp)))
                        else Box(Modifier.size(40.dp).clip(RoundedCornerShape(12.dp)).background(c.surfHi))
                        Text(app.label, Modifier.weight(1f).padding(horizontal = 14.dp), fontSize = 15.sp, fontWeight = FontWeight.SemiBold, color = c.text)
                        OrbitSwitch(app.pkg !in disabled, app.label) { on ->
                            disabled = if (on) disabled - app.pkg else disabled + app.pkg
                            Hub.prefs.disabledApps = disabled
                        }
                    }
                }
            }
        }
    }
}
