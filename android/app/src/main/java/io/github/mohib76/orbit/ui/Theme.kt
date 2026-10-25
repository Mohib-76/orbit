package io.github.mohib76.orbit.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.addPathNodes
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.mohib76.orbit.R

/** Tokens from the Orbit design (light and dark). */
data class OrbitColors(
    val bg: Color, val surf: Color, val surfHi: Color, val text: Color, val text2: Color,
    val primary: Color, val primaryText: Color, val onPrimary: Color, val pc: Color, val onPc: Color,
    val green: Color, val greenInk: Color, val amber: Color, val nav: Color, val navInd: Color,
    val danger: Color, val dangerSoft: Color, val onDanger: Color, val outline: Color,
    val pcLine: Color, val disabled: Color, val muted: Color
)

val LightOrbit = OrbitColors(
    bg = Color(0xFFFCFBFF), surf = Color(0xFFF1F0FA), surfHi = Color(0xFFE5E4F2), text = Color(0xFF1A1B23),
    text2 = Color(0xFF46474F), primary = Color(0xFF4B4FE6), primaryText = Color(0xFF3B3FD0), onPrimary = Color.White,
    pc = Color(0xFFE1E0FF), onPc = Color(0xFF14157A), green = Color(0xFF1DB46A), greenInk = Color(0xFF0B6E3F),
    amber = Color(0xFF8A5300), nav = Color(0xFFF1F0FA), navInd = Color(0xFFE1E0FF), danger = Color(0xFFA12A20),
    dangerSoft = Color(0xFFFCE8E6), onDanger = Color.White,
    outline = Color(0xFF77778A), pcLine = Color(0xFFC9C8F5), disabled = Color(0xFFB4B3C6), muted = Color(0xFFA3A2BB)
)

val DarkOrbit = OrbitColors(
    bg = Color(0xFF121318), surf = Color(0xFF1E1F26), surfHi = Color(0xFF2B2C35), text = Color(0xFFE4E1EC),
    text2 = Color(0xFFC7C5D0), primary = Color(0xFFBFC1FF), primaryText = Color(0xFFBFC1FF), onPrimary = Color(0xFF1C1E86),
    pc = Color(0xFF2E3190), onPc = Color(0xFFE1E0FF), green = Color(0xFF3DD68C), greenInk = Color(0xFF6FE3A8),
    amber = Color(0xFFFFC567), nav = Color(0xFF1E1F26), navInd = Color(0xFF2E3190), danger = Color(0xFFFF9C92),
    dangerSoft = Color(0xFF45201D), onDanger = Color(0xFF5C110B),
    outline = Color(0xFF918F9A), pcLine = Color(0xFF4446A8), disabled = Color(0xFF5A5A68), muted = Color(0xFF6C6B80)
)

val LocalOrbit = staticCompositionLocalOf { LightOrbit }

val Manrope = FontFamily(
    Font(R.font.manrope_regular, FontWeight.Normal),
    Font(R.font.manrope_semibold, FontWeight.SemiBold),
    Font(R.font.manrope_bold, FontWeight.Bold),
    Font(R.font.manrope_extrabold, FontWeight.ExtraBold),
)

@Composable
fun OrbitTheme(content: @Composable () -> Unit) {
    val mode by io.github.mohib76.orbit.Hub.theme.collectAsState()
    val dark = when (mode) { "light" -> false; "dark" -> true; else -> isSystemInDarkTheme() }
    val c = if (dark) DarkOrbit else LightOrbit
    val scheme = (if (dark) darkColorScheme() else lightColorScheme()).copy(
        primary = c.primary, onPrimary = c.onPrimary, primaryContainer = c.pc, onPrimaryContainer = c.onPc,
        background = c.bg, surface = c.bg, onBackground = c.text, onSurface = c.text, onSurfaceVariant = c.text2,
        surfaceVariant = c.surfHi, surfaceContainer = c.surf, surfaceContainerHigh = c.surfHi,
        surfaceContainerHighest = c.surfHi, outline = c.outline, error = c.danger
    )
    val base = TextStyle(fontFamily = Manrope)
    val typo = Typography(
        bodyLarge = base.copy(fontSize = 15.sp), bodyMedium = base.copy(fontSize = 14.sp), bodySmall = base.copy(fontSize = 12.sp),
        labelLarge = base.copy(fontSize = 15.sp, fontWeight = FontWeight.Bold), labelMedium = base.copy(fontSize = 12.sp, fontWeight = FontWeight.SemiBold),
        titleLarge = base.copy(fontSize = 22.sp, fontWeight = FontWeight.ExtraBold), titleMedium = base.copy(fontSize = 16.sp, fontWeight = FontWeight.ExtraBold),
        headlineSmall = base.copy(fontSize = 24.sp, fontWeight = FontWeight.ExtraBold),
    )
    CompositionLocalProvider(LocalOrbit provides c) {
        MaterialTheme(colorScheme = scheme, typography = typo, content = content)
    }
}

/** 24×24 stroke icons built from the design's SVG paths. */
object Icons {
    private fun stroke(name: String, vararg paths: String, width: Float = 1.9f, fill: Boolean = false): ImageVector {
        val b = ImageVector.Builder(name, 24.dp, 24.dp, 24f, 24f)
        for (p in paths) {
            if (fill) b.addPath(addPathNodes(p), fill = SolidColor(Color.Black))
            else b.addPath(addPathNodes(p), stroke = SolidColor(Color.Black), strokeLineWidth = width,
                strokeLineCap = StrokeCap.Round, strokeLineJoin = StrokeJoin.Round)
        }
        return b.build()
    }

    val Home = stroke("home", "M3 10.5 12 3l9 7.5V20a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z")
    val HomeFilled = stroke("homeF", "M3 10.5 12 3l9 7.5V20a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z", fill = true)
    val Clip = stroke("clip", "M9 3h6v3H9z", "M15 4.5h2a2 2 0 0 1 2 2V19a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6.5a2 2 0 0 1 2-2h2")
    val Folder = stroke("folder", "M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z")
    val Settings = stroke("settings", "M4 7h10M18 7h2M4 17h4M12 17h8", "M18 7a2 2 0 1 1-4 0a2 2 0 1 1 4 0", "M12 17a2 2 0 1 1-4 0a2 2 0 1 1 4 0")
    val Bell = stroke("bell", "M6 16v-5a6 6 0 1 1 12 0v5l2 2H4z", "M10 20.5a2 2 0 0 0 4 0")
    val Upload = stroke("upload", "M12 16V4M6 10l6-6 6 6", "M4 20h16", width = 2f)
    val Laptop = stroke("laptop", "M5.5 5h13A1.5 1.5 0 0 1 20 6.5V16H4V6.5A1.5 1.5 0 0 1 5.5 5z", "M2 19h20")
    val Usb = stroke("usb", "M9 2v5M15 2v5", "M6 7h12v4a6 6 0 0 1-12 0z", "M12 17v5")
    val Lock = stroke("lock", "M7 11h10a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2v-6a2 2 0 0 1 2-2z", "M8 11V8a4 4 0 0 1 8 0v3")
    val Check = stroke("check", "M5 12.5 10 17l9-10", width = 2.2f)
    val Close = stroke("close", "M6 6l12 12M18 6 6 18", width = 2f)
    val Plus = stroke("plus", "M12 5v14M5 12h14", width = 2.4f)
    val Chevron = stroke("chev", "m9 6 6 6-6 6", width = 2f)
    val Back = stroke("back", "m15 6-6 6 6 6", width = 2f)
    val Qr = stroke("qr", "M4 4h6v6H4z", "M14 4h6v6h-6z", "M4 14h6v6H4z", "M14 14h2v2h-2z", "M18 18h2v2h-2z", "M14 18h2", "M18 14h2")
    val Search = stroke("search", "M11 4a7 7 0 1 1 0 14a7 7 0 1 1 0-14z", "M20 20l-4-4")
    val Battery = stroke("battery", "M4 8h14a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V9a1 1 0 0 1 1-1z", "M21 11v2")
    val Buds = stroke("buds", "M8 3a4 4 0 0 0-4 4v1a4 4 0 0 0 3 3.9V20a1.5 1.5 0 0 0 3 0V7a4 4 0 0 0-2-4z",
        "M16 3a4 4 0 0 1 4 4v1a4 4 0 0 1-3 3.9V20a1.5 1.5 0 0 1-3 0V7a4 4 0 0 1 2-4z")
    val ModeOff = stroke("mOff", "M12 3a9 9 0 1 0 0 18a9 9 0 0 0 0-18z", "M5.6 5.6l12.8 12.8")
    val ModeTrans = stroke("mTrans", "M8 15.5c0 2 1.3 3.5 3 3.5 2.5 0 2.5-3 4-4.5 1.3-1.3 2-2.6 2-4.5a5 5 0 0 0-10 0",
        "M10 10a2 2 0 0 1 4 0", "M19.5 6.5a7 7 0 0 1 0 7")
    val ModeAnc = stroke("mAnc", "M12 3 5 6v5.5c0 4.2 3 7.6 7 8.5 4-.9 7-4.3 7-8.5V6z", "M9 12l2 2 4-4")
    val Swap = stroke("swap", "M4 8h14l-3-3", "M20 16H6l3 3", width = 2f)
    val Phone = stroke("phone", "M9.5 2.5h5A2.5 2.5 0 0 1 17 5v14a2.5 2.5 0 0 1-2.5 2.5h-5A2.5 2.5 0 0 1 7 19V5a2.5 2.5 0 0 1 2.5-2.5z", "M11 18.5h2")
    val Sync = stroke("sync", "M20 11a8 8 0 0 0-14.5-4.5L4 8", "M4 4v4h4", "M4 13a8 8 0 0 0 14.5 4.5L20 16", "M20 20v-4h-4")
    val Wifi = stroke("wifi", "M2 9a15 15 0 0 1 20 0M5.5 12.5a10 10 0 0 1 13 0M9 16a5 5 0 0 1 6 0", "M12 19.5h.01")
    val Cable = stroke("cable", "M9 2v6M15 2v6", "M7 8h10v4a5 5 0 0 1-10 0z", "M12 17v5", width = 2.2f)
    val Info = stroke("info", "M12 8v5M12 16h.01", "M12 3a9 9 0 1 0 0 18a9 9 0 0 0 0-18z", width = 2f)
    val Switching = stroke("switching", "M20 11a8 8 0 0 0-14.5-4.5L4 8", "M4 4v4h4", width = 2f)
    val Bolt = stroke("bolt", "M13 2 5 14h6l-1 8 8-12h-6z", fill = true)
    val Play = stroke("play", "M7 4v16l13-8z", fill = true)
    val Pause = stroke("pause", "M6 4h4v16H6z", "M14 4h4v16h-4z", fill = true)
    val Next = stroke("next", "M15 5h2v14h-2z", "M4 5v14l10-7z", fill = true)
    val Prev = stroke("prev", "M7 5h2v14H7z", "M20 5v14l-10-7z", fill = true)
    val Volume = stroke("volume", "M4 9v6h4l5 4V5L8 9z", "M16.5 8.5a5 5 0 0 1 0 7", "M19 6a8.5 8.5 0 0 1 0 12", width = 2f)
    val VolumeOff = stroke("volumeOff", "M4 9v6h4l5 4V5L8 9z", "M17 9l4 6M21 9l-4 6", width = 2f)
    val Moon = stroke("moon", "M20 14.5A8 8 0 1 1 9.5 4a6.5 6.5 0 0 0 10.5 10.5z")
    val Power = stroke("power", "M12 3v8", "M6.3 6.3a8 8 0 1 0 11.4 0", width = 2f)
    val Restart = stroke("restart", "M20 11a8 8 0 1 0-2.3 5.7", "M20 5v6h-6", width = 2f)
    val Shield = stroke("shield", "M12 3 5 6v5.5c0 4.2 3 7.6 7 8.5 4-.9 7-4.3 7-8.5V6z", width = 2f)
    val Terminal = stroke("terminal", "M5 5h14a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z", "M8 10l3 2.5L8 15", "M13 15h3")
    val Remote = stroke("remote", "M5.5 5h13A1.5 1.5 0 0 1 20 6.5V16H4V6.5A1.5 1.5 0 0 1 5.5 5z", "M2 19h20", "M10 10.5l2-2 2 2")
    val Touchpad = stroke("touchpad", "M6 4h12a3 3 0 0 1 3 3v10a3 3 0 0 1-3 3H6a3 3 0 0 1-3-3V7a3 3 0 0 1 3-3z", "M3 15h18M12 15v5")
    val Keyboard = stroke("keyboard", "M4 6h16a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z", "M6 10h.01M10 10h.01M14 10h.01M18 10h.01M7 14h10")
    val Slides = stroke("slides", "M5 4h14a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z", "M12 16v4M8 20h8", "M10 8l4 2-4 2z")
    val Mic = stroke("mic", "M12 3a3 3 0 0 1 3 3v5a3 3 0 0 1-6 0V6a3 3 0 0 1 3-3z", "M5 11a7 7 0 0 0 14 0M12 18v3", width = 2f)
    val Square = stroke("square", "M8 6h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z", fill = true)
    val Camera = stroke("camera", "M4 8a2 2 0 0 1 2-2h2l1.5-2h5L16 6h2a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z", "M12 9a3.5 3.5 0 1 1 0 7a3.5 3.5 0 1 1 0-7z")
    val Exit = stroke("exit", "M9 5H6a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h3", "M15 8l4 4-4 4", "M19 12H9", width = 2f)
    val Link = stroke("link", "M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1", "M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1", width = 2f)
}
