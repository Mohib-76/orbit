package io.github.mohib76.orbit.ui

import android.app.Activity
import android.content.Intent
import android.speech.RecognizerIntent
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.platform.LocalViewConfiguration
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.TextRange
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.mohib76.orbit.Hub
import io.github.mohib76.orbit.R
import io.github.mohib76.orbit.remote.RemoteInput
import kotlinx.coroutines.delay
import kotlin.math.max

/**
 * Asks the PC for its foreground app while this screen is in front, again after a reconnect (the PC stops
 * watching when the link drops), and not while the app is in the background.
 */
@Composable
private fun WatchFocus() {
    val ui by Hub.ui.collectAsState()
    val connected = ui.conn == io.github.mohib76.orbit.Conn.Connected
    androidx.lifecycle.compose.LifecycleResumeEffect(connected) {
        if (connected) RemoteInput.watchFocus(true)
        onPauseOrDispose { if (connected) RemoteInput.watchFocus(false) }
    }
}

@Composable
private fun BackButton(tint: Color, onBack: () -> Unit) {
    val desc = stringResource(R.string.back)
    Box(Modifier.size(48.dp).clip(CircleShape).clickable(role = Role.Button, onClick = onBack).semantics { contentDescription = desc },
        contentAlignment = Alignment.Center) { Icon(Icons.Back, null, Modifier.size(22.dp), tint = tint) }
}

// ============================================================== touchpad and keyboard

/** Touchpad | Keyboard, switched with one tap at the top. [keyboard] picks the starting tab. */
@Composable
fun InputScreen(keyboard: Boolean, onBack: () -> Unit) {
    val c = LocalOrbit.current
    var kb by remember { mutableStateOf(keyboard) }
    WatchFocus()
    Column(Modifier.fillMaxSize().background(c.bg).statusBarsPadding().navigationBarsPadding().imePadding()) {
        Row(Modifier.fillMaxWidth().height(64.dp).padding(start = 12.dp, end = 12.dp, top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            BackButton(c.text, onBack)
            Spacer(Modifier.weight(1f))
            Row(Modifier.width(230.dp).clip(RoundedCornerShape(16.dp)).background(c.surf).padding(4.dp), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                listOf(false to R.string.input_touchpad, true to R.string.input_keyboard).forEach { (isKb, label) ->
                    val sel = kb == isKb
                    Box(
                        Modifier.weight(1f).height(40.dp).clip(RoundedCornerShape(12.dp)).background(if (sel) c.primary else Color.Transparent)
                            .clickable { kb = isKb }.semantics { role = Role.Tab; selected = sel },
                        contentAlignment = Alignment.Center
                    ) { Text(stringResource(label), fontSize = 13.sp, fontWeight = if (sel) FontWeight.ExtraBold else FontWeight.Bold, color = if (sel) c.onPrimary else c.text) }
                }
            }
            Spacer(Modifier.weight(1f))
            Spacer(Modifier.width(48.dp))
        }
        if (kb) KeyboardPane() else TouchpadPane()
    }
}

@Composable
private fun StatusLine(text: String) {
    val c = LocalOrbit.current
    Row(Modifier.padding(horizontal = 4.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Dot(c.green, 7.dp)
        Text(text, fontSize = 12.sp, fontWeight = FontWeight.Bold, color = c.text2, maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

// PC pixels per dp of finger travel, before acceleration.
private val SPEEDS = floatArrayOf(1.2f, 1.8f, 2.6f, 3.6f)

@Composable
@OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
private fun TouchpadPane() {
    val c = LocalOrbit.current
    val ui by Hub.ui.collectAsState()
    var speed by remember { mutableIntStateOf(Hub.prefs.touchpadSpeed) }
    val slop = LocalViewConfiguration.current.touchSlop
    val padDesc = stringResource(R.string.input_pad_desc)
    Column(Modifier.fillMaxSize().padding(start = 16.dp, end = 16.dp, top = 12.dp, bottom = 20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.weight(1f)) { StatusLine(stringResource(R.string.input_moving_on, ui.pcName)) }
            val speedDesc = stringResource(R.string.input_speed)
            val speedState = stringResource(R.string.input_speed_state, speed)
            Row(
                Modifier.clip(RoundedCornerShape(8.dp)).clickable { speed = speed % 4 + 1; Hub.prefs.touchpadSpeed = speed }
                    .semantics { contentDescription = speedDesc; stateDescription = speedState }.padding(horizontal = 6.dp, vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(3.dp)
            ) {
                Text(speedDesc, Modifier.padding(end = 5.dp), fontSize = 12.sp, fontWeight = FontWeight.Bold, color = c.text2)
                for (i in 1..4) Box(Modifier.size(6.dp, 12.dp).clip(RoundedCornerShape(2.dp)).background(if (i <= speed) c.primary else c.surfHi))
            }
        }

        Box(
            Modifier.weight(1f).fillMaxWidth().clip(RoundedCornerShape(28.dp)).background(c.surf)
                .semantics { contentDescription = padDesc }
                .pointerInput(speed) {
                    val factor = SPEEDS[speed - 1] / density
                    awaitEachGesture {
                        val first = awaitFirstDown(requireUnconsumed = false)
                        val start = first.uptimeMillis
                        var fingers = 1
                        var travel = 0f
                        var end = start
                        do {
                            val event = awaitPointerEvent()
                            end = event.changes.maxOf { it.uptimeMillis }
                            val down = event.changes.filter { it.pressed && it.previousPressed }
                            fingers = max(fingers, event.changes.count { it.pressed })
                            if (down.isNotEmpty()) {
                                var d = Offset.Zero
                                down.forEach { d += it.position - it.previousPosition }
                                d /= down.size.toFloat()
                                travel += d.getDistance()
                                if (fingers == 1) {
                                    // A little acceleration: slow moves stay precise, flicks cross the screen.
                                    val accel = 1f + minOf(d.getDistance() / density / 14f, 1.5f)
                                    RemoteInput.move(d.x * factor * accel, d.y * factor * accel)
                                } else if (down.size >= 2) RemoteInput.scroll(d.y / density / 24f, -d.x / density / 24f)
                            }
                            event.changes.forEach { it.consume() }
                        } while (event.changes.any { it.pressed })
                        // A short touch that didn't move is a tap: one finger clicks, two right-click.
                        if (travel < slop && end - start < 250) RemoteInput.button(if (fingers >= 2) "right" else "left")
                    }
                }
        ) {
            androidx.compose.foundation.layout.FlowRow(Modifier.align(Alignment.BottomCenter).padding(bottom = 18.dp, start = 12.dp, end = 12.dp),
                horizontalArrangement = Arrangement.spacedBy(6.dp, Alignment.CenterHorizontally), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                listOf(R.string.input_hint_tap, R.string.input_hint_right, R.string.input_hint_scroll).forEach {
                    Text(stringResource(it), Modifier.clip(RoundedCornerShape(50)).background(c.bg.copy(alpha = 0.85f)).padding(horizontal = 9.dp, vertical = 6.dp),
                        fontSize = 11.sp, fontWeight = FontWeight.Bold, color = c.text2, maxLines = 1)
                }
            }
        }

        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            ClickButton(stringResource(R.string.input_left_click), "left", Modifier.weight(1f))
            ClickButton(stringResource(R.string.input_right_click), "right", Modifier.weight(1f))
        }
    }
}

/** Press = button down, release = up, so holding it while moving on the pad drags. */
@Composable
private fun ClickButton(label: String, button: String, modifier: Modifier) {
    val c = LocalOrbit.current
    var pressed by remember { mutableStateOf(false) }
    Box(
        modifier.height(72.dp).clip(RoundedCornerShape(22.dp)).background(if (pressed) c.primary else c.pc)
            .semantics { role = Role.Button; contentDescription = label }
            .pointerInput(button) {
                detectTapGestures(onPress = {
                    pressed = true
                    RemoteInput.button(button, "down")
                    // finally: leaving the screen mid-press must still let go of the button on the PC.
                    try { tryAwaitRelease() } finally {
                        RemoteInput.button(button, "up")
                        pressed = false
                    }
                })
            },
        contentAlignment = Alignment.Center
    ) { Text(label, fontSize = 15.sp, fontWeight = FontWeight.ExtraBold, color = if (pressed) c.onPrimary else c.onPc) }
}

/** Keeps a zero-width character at the start, so Backspace on an "empty" field still reaches us. */
private const val SENTINEL = "​"
private const val ECHO_MAX = 200

private val KEYS = listOf(
    "Esc" to "esc", "Tab" to "tab", "Ctrl" to "ctrl", "Alt" to "alt", "Win" to "win", "Del" to "delete",
    "←" to "left", "↑" to "up", "↓" to "down", "→" to "right", "Home" to "home", "End" to "end"
)
private val MODS = setOf("ctrl", "alt", "win")

@Composable
private fun KeyboardPane() {
    val c = LocalOrbit.current
    val ui by Hub.ui.collectAsState()
    val app by RemoteInput.focusApp.collectAsState()
    var field by remember { mutableStateOf(TextFieldValue(SENTINEL, TextRange(1))) }
    var held by remember { mutableStateOf(emptySet<String>()) }
    val focus = remember { FocusRequester() }
    LaunchedEffect(Unit) { focus.requestFocus() }

    // Ctrl, Alt and Win apply to the next key or letter only, as on other on-screen keyboards: left held, typing
    // after Ctrl+C would send Ctrl+W and close the tab.
    fun shortcut(key: String) {
        RemoteInput.key(key, held)
        held = emptySet()
    }

    fun typed(s: String) {
        if (held.isEmpty()) { RemoteInput.text(s); return }
        val first = s.first()
        if (first.isLetterOrDigit() && first.code < 128) shortcut(first.lowercase()) else { RemoteInput.text(first.toString()); held = emptySet() }
        if (s.length > 1) RemoteInput.text(s.substring(1))
    }

    fun onEdit(v: TextFieldValue) {
        // Compare before putting the sentinel back, so deleting it (Backspace on an "empty" field) counts.
        val old = field.text
        val common = old.commonPrefixWith(v.text).length
        repeat(old.length - common) { RemoteInput.key("backspace") }
        val added = v.text.substring(common)
        if (added.isNotEmpty()) typed(added)
        val new = if (v.text.startsWith(SENTINEL)) v.text else SENTINEL + v.text
        val kept = if (new.length > ECHO_MAX) SENTINEL + new.takeLast(ECHO_MAX) else new
        field = TextFieldValue(kept, TextRange(kept.length))
    }

    val voice = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) { r ->
        val said = r.data?.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS)?.firstOrNull()
        if (r.resultCode == Activity.RESULT_OK && !said.isNullOrEmpty()) onEdit(TextFieldValue(field.text + said))
    }
    val ctx = LocalContext.current
    val canVoice = remember { Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).resolveActivity(ctx.packageManager) != null }

    Column(Modifier.fillMaxSize().padding(start = 16.dp, end = 16.dp, top = 12.dp, bottom = 12.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        StatusLine(if (app.isNotEmpty()) stringResource(R.string.input_typing_into, app, ui.pcName) else stringResource(R.string.input_typing_on, ui.pcName))
        Column(
            Modifier.weight(1f).fillMaxWidth().clip(RoundedCornerShape(24.dp)).background(c.surf).clickable { focus.requestFocus() }.padding(18.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            Text(stringResource(R.string.input_each_letter), fontSize = 12.sp, fontWeight = FontWeight.Bold, color = c.text2)
            BasicTextField(
                field, ::onEdit, Modifier.fillMaxWidth().weight(1f).focusRequester(focus),
                textStyle = TextStyle(fontFamily = Manrope, fontSize = 20.sp, fontWeight = FontWeight.SemiBold, color = c.text, lineHeight = 28.sp),
                cursorBrush = SolidColor(c.primary),
                keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.None, autoCorrectEnabled = false)
            )
            if (canVoice) Row(verticalAlignment = Alignment.CenterVertically) {
                Text(stringResource(R.string.input_voice), Modifier.weight(1f), fontSize = 12.sp, color = c.text2)
                val micDesc = stringResource(R.string.input_voice_button)
                Box(
                    Modifier.size(44.dp).clip(CircleShape).background(c.pc).clickable(role = Role.Button) {
                        voice.launch(Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH)
                            .putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM))
                    }.semantics { contentDescription = micDesc },
                    contentAlignment = Alignment.Center
                ) { Icon(Icons.Mic, null, Modifier.size(20.dp), tint = c.onPc) }
            }
        }

        val heldText = stringResource(R.string.input_key_held)
        KEYS.chunked(6).forEach { row ->
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                row.forEach { (label, key) ->
                    val isMod = key in MODS
                    val on = isMod && key in held
                    Box(
                        Modifier.weight(1f).height(44.dp).clip(RoundedCornerShape(12.dp))
                            .then(if (on) Modifier.background(c.primary) else Modifier.background(c.bg).border(1.dp, c.surfHi, RoundedCornerShape(12.dp)))
                            .clickable(role = if (isMod) Role.Switch else Role.Button) {
                                if (isMod) held = if (on) held - key else held + key
                                else shortcut(key)
                            }
                            .semantics { if (isMod) stateDescription = if (on) heldText else "" },
                        contentAlignment = Alignment.Center
                    ) { Text(label, fontSize = 13.sp, fontWeight = FontWeight.Bold, color = if (on) c.onPrimary else c.text, maxLines = 1) }
                }
            }
        }
    }
}

// ============================================================== slides remote

private val SlidesBg = Color(0xFF0E0E14)

/** Dark on purpose (it shouldn't light up the room), whatever the app theme. */
@Composable
fun SlidesScreen(onBack: () -> Unit) {
    androidx.compose.runtime.CompositionLocalProvider(LocalOrbit provides DarkOrbit) { SlidesContent(onBack) }
}

@Composable
private fun SlidesContent(onBack: () -> Unit) {
    val c = LocalOrbit.current
    val app by RemoteInput.focusApp.collectAsState()
    var black by remember { mutableStateOf(false) }
    var startedAt by remember { mutableLongStateOf(0L) }
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    var volumeKeys by remember { mutableStateOf(Hub.prefs.slidesVolumeKeys) }
    WatchFocus()

    val view = LocalView.current
    DisposableEffect(Unit) {
        view.keepScreenOn = true
        onDispose { view.keepScreenOn = false }
    }
    DisposableEffect(volumeKeys) {
        RemoteInput.volumeKeys = if (volumeKeys) { up -> RemoteInput.slides(if (up) "prev" else "next"); true } else null
        onDispose { RemoteInput.volumeKeys = null }
    }
    LaunchedEffect(startedAt) { while (startedAt > 0) { now = System.currentTimeMillis(); delay(1000) } }

    Column(Modifier.fillMaxSize().background(SlidesBg).statusBarsPadding().navigationBarsPadding()) {
        Row(Modifier.fillMaxWidth().height(64.dp).padding(start = 12.dp, end = 12.dp, top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            BackButton(c.text, onBack)
            Column(Modifier.weight(1f).padding(start = 4.dp)) {
                Text(stringResource(R.string.slides_title), fontSize = 18.sp, fontWeight = FontWeight.ExtraBold, color = c.text)
                Text(app.ifEmpty { stringResource(R.string.slides_window_in_front) }, fontSize = 12.sp, fontWeight = FontWeight.SemiBold,
                    color = c.text2, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            val secs = if (startedAt > 0) (now - startedAt) / 1000 else 0
            val elapsedDesc = stringResource(R.string.slides_elapsed)
            Text("%02d:%02d".format(secs / 60, secs % 60), Modifier.clip(RoundedCornerShape(10.dp)).background(c.surf).padding(horizontal = 12.dp, vertical = 6.dp)
                .semantics { contentDescription = elapsedDesc }, fontSize = 15.sp, fontFamily = FontFamily.Monospace, color = c.text)
        }

        Column(Modifier.weight(1f).padding(start = 16.dp, end = 16.dp, top = 12.dp, bottom = 20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                SlideChip(stringResource(R.string.slides_start), Icons.Play, false, Modifier.weight(1f)) {
                    RemoteInput.slides("start"); black = false; startedAt = System.currentTimeMillis(); now = startedAt
                }
                SlideChip(stringResource(R.string.slides_black), Icons.Square, black, Modifier.weight(1f)) { RemoteInput.slides("black"); black = !black }
                SlideChip(stringResource(R.string.slides_end), Icons.Exit, false, Modifier.weight(1f)) {
                    RemoteInput.slides("end"); black = false; startedAt = 0
                }
            }
            val nextDesc = stringResource(R.string.slides_next_desc)
            Column(
                Modifier.weight(1f).fillMaxWidth().clip(RoundedCornerShape(32.dp)).background(c.primary)
                    .clickable(role = Role.Button) { RemoteInput.slides("next"); black = false }.semantics { contentDescription = nextDesc },
                horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(8.dp, Alignment.CenterVertically)
            ) {
                Icon(Icons.Chevron, null, Modifier.size(56.dp), tint = c.onPrimary)
                Text(stringResource(R.string.slides_next), fontSize = 24.sp, fontWeight = FontWeight.ExtraBold, color = c.onPrimary)
            }
            val prevDesc = stringResource(R.string.slides_prev_desc)
            Row(
                Modifier.fillMaxWidth().height(120.dp).clip(RoundedCornerShape(28.dp)).background(c.surfHi)
                    .clickable(role = Role.Button) { RemoteInput.slides("prev"); black = false }.semantics { contentDescription = prevDesc },
                horizontalArrangement = Arrangement.spacedBy(10.dp, Alignment.CenterHorizontally), verticalAlignment = Alignment.CenterVertically
            ) {
                Icon(Icons.Back, null, Modifier.size(28.dp), tint = c.text)
                Text(stringResource(R.string.slides_prev), fontSize = 18.sp, fontWeight = FontWeight.ExtraBold, color = c.text)
            }
            Row(Modifier.padding(start = 4.dp, end = 4.dp, top = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(stringResource(R.string.slides_volume_keys), fontSize = 14.sp, fontWeight = FontWeight.Bold, color = c.text)
                    Text(stringResource(R.string.slides_volume_keys_desc), fontSize = 12.sp, color = c.text2)
                }
                OrbitSwitch(volumeKeys, stringResource(R.string.slides_volume_keys)) { volumeKeys = it; Hub.prefs.slidesVolumeKeys = it }
            }
        }
    }
}

@Composable
private fun SlideChip(label: String, icon: androidx.compose.ui.graphics.vector.ImageVector, on: Boolean, modifier: Modifier, onClick: () -> Unit) {
    val c = LocalOrbit.current
    val onText = stringResource(R.string.slides_on)
    Row(
        modifier.height(52.dp).clip(RoundedCornerShape(16.dp)).background(if (on) c.text else c.surf).clickable(role = Role.Button, onClick = onClick)
            .semantics { if (on) stateDescription = onText },
        horizontalArrangement = Arrangement.spacedBy(6.dp, Alignment.CenterHorizontally), verticalAlignment = Alignment.CenterVertically
    ) {
        Icon(icon, null, Modifier.size(16.dp), tint = if (on) SlidesBg else c.text)
        Text(label, fontSize = 13.sp, fontWeight = FontWeight.Bold, color = if (on) SlidesBg else c.text, textAlign = TextAlign.Center)
    }
}
