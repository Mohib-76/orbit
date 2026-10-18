package io.github.mohib76.orbit.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

@Composable
fun OrbitLogo(size: Dp = 28.dp, color: Color = LocalOrbit.current.primary) {
    Canvas(Modifier.size(size)) {
        val s = this.size.width / 48f
        drawCircle(color, radius = 8f * s, center = Offset(24f * s, 24f * s))
        // Arc from the top-right, around, back to the right: the design's "M28.4 7.58 A17 17 0 1 0 40.42 19.6".
        drawArc(color, startAngle = -75f, sweepAngle = -285f, useCenter = false,
            topLeft = Offset(7f * s, 7f * s), size = androidx.compose.ui.geometry.Size(34f * s, 34f * s),
            style = Stroke(width = 3.4f * s, cap = StrokeCap.Round))
        drawCircle(color, radius = 4.4f * s, center = Offset(36.02f * s, 11.98f * s))
    }
}

@Composable
fun Title(text: String, modifier: Modifier = Modifier) = Text(
    text, modifier.padding(horizontal = 20.dp).padding(top = 18.dp, bottom = 8.dp),
    fontSize = 24.sp, fontWeight = FontWeight.ExtraBold, letterSpacing = (-0.6).sp, color = LocalOrbit.current.text
)

@Composable
fun SectionLabel(text: String) = Text(
    text, Modifier.padding(start = 4.dp, top = 6.dp, bottom = 4.dp),
    fontSize = 13.sp, fontWeight = FontWeight.ExtraBold, color = LocalOrbit.current.primaryText
)

@Composable
fun IconChip(icon: ImageVector, size: Dp = 40.dp, bg: Color = LocalOrbit.current.surfHi, tint: Color = LocalOrbit.current.primaryText) {
    Box(Modifier.size(size).clip(RoundedCornerShape(size * 0.3f)).background(bg), contentAlignment = Alignment.Center) {
        Icon(icon, null, Modifier.size(size / 2), tint = tint)
    }
}

@Composable
fun OrbitSwitch(checked: Boolean, label: String, onChange: (Boolean) -> Unit) {
    val c = LocalOrbit.current
    Switch(
        checked = checked, onCheckedChange = onChange,
        colors = SwitchDefaults.colors(
            checkedTrackColor = c.primary, checkedThumbColor = c.onPrimary,
            uncheckedTrackColor = c.surfHi, uncheckedThumbColor = c.outline, uncheckedBorderColor = c.outline
        ),
        modifier = Modifier.semantics { contentDescription = label }
    )
}

@Composable
fun PillButton(
    text: String, icon: ImageVector? = null, modifier: Modifier = Modifier,
    filled: Boolean = true, bg: Color? = null, fg: Color? = null, onClick: () -> Unit
) {
    val c = LocalOrbit.current
    val background = bg ?: if (filled) c.primary else Color.Transparent
    val content = fg ?: if (filled) c.onPrimary else c.primaryText
    Row(
        modifier
            .defaultMinSize(minHeight = 48.dp)
            .clip(RoundedCornerShape(18.dp))
            .background(background)
            .then(if (!filled && bg == null) Modifier.border(1.dp, c.outline, RoundedCornerShape(18.dp)) else Modifier)
            .clickable(role = Role.Button, onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 12.dp),
        horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically
    ) {
        if (icon != null) { Icon(icon, null, Modifier.size(18.dp), tint = content); Spacer(Modifier.width(8.dp)) }
        Text(text, color = content, fontWeight = FontWeight.Bold, fontSize = 15.sp, maxLines = 1, softWrap = false,
            overflow = TextOverflow.Ellipsis)
    }
}

@Composable
fun Tile(modifier: Modifier = Modifier, onClick: (() -> Unit)? = null, content: @Composable () -> Unit) {
    val c = LocalOrbit.current
    Box(
        modifier
            .clip(RoundedCornerShape(24.dp))
            .background(c.surf)
            .then(if (onClick != null) Modifier.clickable(onClick = onClick) else Modifier)
            .padding(16.dp)
    ) { content() }
}

@Composable
fun SettingRow(
    title: String, desc: String? = null, titleColor: Color = LocalOrbit.current.text,
    onClick: (() -> Unit)? = null, trailing: @Composable RowScope.() -> Unit = {}
) {
    val c = LocalOrbit.current
    Row(
        Modifier.fillMaxWidth().defaultMinSize(minHeight = 56.dp)
            .then(if (onClick != null) Modifier.clip(RoundedCornerShape(12.dp)).clickable(onClick = onClick) else Modifier)
            .padding(horizontal = 4.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column(Modifier.weight(1f).padding(end = 12.dp)) {
            Text(title, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, color = titleColor)
            if (desc != null) Text(desc, fontSize = 12.sp, color = c.text2, lineHeight = 16.sp)
        }
        trailing()
    }
}

@Composable
fun Dot(color: Color, size: Dp = 8.dp) = Box(Modifier.size(size).clip(CircleShape).background(color))

@Composable
fun Progress(value: Float) {
    val c = LocalOrbit.current
    LinearProgressIndicator(
        progress = { value }, modifier = Modifier.fillMaxWidth().height(6.dp).clip(RoundedCornerShape(3.dp)),
        color = c.primary, trackColor = c.surfHi, strokeCap = StrokeCap.Round, gapSize = 0.dp, drawStopIndicator = {}
    )
}

@Composable
fun Ellipsized(text: String, size: Int = 13, color: Color = LocalOrbit.current.text2, bold: Boolean = false, lines: Int = 1) = Text(
    text, fontSize = size.sp, color = color, maxLines = lines, overflow = TextOverflow.Ellipsis,
    fontWeight = if (bold) FontWeight.Bold else FontWeight.Normal, lineHeight = (size + 5).sp
)
