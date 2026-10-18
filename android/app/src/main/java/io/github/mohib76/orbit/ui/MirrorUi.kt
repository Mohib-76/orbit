package io.github.mohib76.orbit.ui

import androidx.activity.compose.BackHandler
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
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.mohib76.orbit.Hub
import io.github.mohib76.orbit.mirror.Mirror

/** Design: AndMirrorSetup. Walks the phone through turning on wireless debugging. */
@Composable
fun MirrorSetupScreen(resumeTick: Int, fromPc: Boolean, onClose: () -> Unit) {
    val c = LocalOrbit.current
    val ctx = LocalContext.current
    val ui by Hub.ui.collectAsState()
    val s = remember(resumeTick) { Mirror.devState(ctx) }
    LaunchedEffect(Unit) { Mirror.setupShown() }
    BackHandler(onBack = onClose)

    Column(
        Modifier.fillMaxSize().background(c.bg).statusBarsPadding().navigationBarsPadding()
            .padding(start = 20.dp, end = 20.dp, top = 16.dp, bottom = 28.dp)
    ) {
        Box(Modifier.offset(x = (-8).dp).size(48.dp).clip(CircleShape).clickable(role = Role.Button, onClick = onClose),
            contentAlignment = Alignment.Center) {
            Icon(Icons.Close, "Close", Modifier.size(22.dp), tint = c.text)
        }
        Column(Modifier.weight(1f).verticalScroll(rememberScrollState())) {
            if (fromPc) {
                Row(Modifier.fillMaxWidth().padding(top = 4.dp).clip(RoundedCornerShape(16.dp)).background(c.pc).padding(horizontal = 14.dp, vertical = 10.dp),
                    verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Laptop, null, Modifier.size(18.dp), tint = c.onPc)
                    Spacer(Modifier.width(10.dp))
                    Text("${ui.pcName} asked to set up mirroring", fontSize = 13.sp, fontWeight = FontWeight.Bold, color = c.onPc)
                }
            }
            Text("Mirror this phone to your PC", fontSize = 28.sp, fontWeight = FontWeight.ExtraBold, letterSpacing = (-0.8).sp,
                lineHeight = 32.sp, color = c.text, modifier = Modifier.padding(top = 18.dp))
            Text("Two taps in Android's settings. You only do this once.", fontSize = 15.sp, color = c.text2, lineHeight = 22.sp,
                modifier = Modifier.padding(top = 8.dp))

            Column(Modifier.padding(top = 22.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                if (s.devOptions) StepDone("Developer options", "Already on")
                else Step(1, "Turn on Developer options", "About phone › Software information › tap Build number 7 times",
                    active = true, action = "Open" to { Mirror.openAboutPhone(ctx) })

                val stepTwoActive = s.devOptions && !s.wirelessDebugging
                Column(
                    Modifier.fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(if (stepTwoActive) c.bg else c.surf)
                        .then(if (stepTwoActive) Modifier.border(1.5.dp, Color(0xFF9FA1F0), RoundedCornerShape(20.dp)) else Modifier)
                        .padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(12.dp)
                ) {
                    StepHead(2, if (s.wirelessDebugging) "Wireless debugging is on" else "Turn on Wireless debugging",
                        "Then tap “Pair device with QR code”", active = stepTwoActive)
                    SettingsPreview(s.wirelessDebugging)
                }
                Step(3, "Scan the code on your PC", "Orbit takes it from there", active = s.devOptions && s.wirelessDebugging)
            }

            if (!s.wifi) {
                Row(Modifier.fillMaxWidth().padding(top = 14.dp).clip(RoundedCornerShape(16.dp)).background(c.surf).padding(14.dp),
                    verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Wifi, null, Modifier.size(18.dp), tint = c.amber)
                    Spacer(Modifier.width(10.dp))
                    Text("Wireless debugging needs this phone on Wi-Fi, on the same network as your PC. It doesn't work over your own hotspot. With a USB cable, mirroring works without any of this.",
                        fontSize = 13.sp, color = c.text2, lineHeight = 18.sp)
                }
            }
        }

        PillButton("Open Wireless debugging", modifier = Modifier.fillMaxWidth().padding(top = 12.dp)) { Mirror.openWirelessDebugging(ctx) }
        Text("Your screen is only streamed while the PC window is open, and you'll always see a notice.",
            fontSize = 12.sp, color = c.text2, textAlign = TextAlign.Center, lineHeight = 18.sp,
            modifier = Modifier.fillMaxWidth().padding(top = 8.dp))
    }
}

@Composable
private fun StepDone(title: String, desc: String) {
    val c = LocalOrbit.current
    Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(c.surf).padding(horizontal = 16.dp, vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(36.dp).clip(CircleShape).background(c.green), contentAlignment = Alignment.Center) {
            Icon(Icons.Check, null, Modifier.size(18.dp), tint = Color.White)
        }
        Column(Modifier.padding(start = 14.dp)) {
            Text(title, fontSize = 15.sp, fontWeight = FontWeight.Bold, color = c.text)
            Text(desc, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = c.greenInk)
        }
    }
}

@Composable
private fun Step(n: Int, title: String, desc: String, active: Boolean, action: Pair<String, () -> Unit>? = null) {
    val c = LocalOrbit.current
    Row(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(20.dp)).background(c.bg)
            .border(if (active) 1.5.dp else 1.dp, if (active) Color(0xFF9FA1F0) else c.surfHi, RoundedCornerShape(20.dp))
            .padding(horizontal = 16.dp, vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Box(Modifier.weight(1f)) { StepHead(n, title, desc, active) }
        if (action != null) PillButton(action.first, filled = false, onClick = action.second)
    }
}

@Composable
private fun StepHead(n: Int, title: String, desc: String, active: Boolean) {
    val c = LocalOrbit.current
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(36.dp).clip(CircleShape).background(if (active) c.primary else c.surf), contentAlignment = Alignment.Center) {
            Text("$n", fontSize = 15.sp, fontWeight = FontWeight.ExtraBold, color = if (active) c.onPrimary else c.text2)
        }
        Column(Modifier.padding(start = 14.dp)) {
            Text(title, fontSize = 15.sp, fontWeight = FontWeight.Bold, color = c.text)
            Text(desc, fontSize = 13.sp, color = c.text2, lineHeight = 18.sp)
        }
    }
}

/** A small picture of Android's Wireless debugging page, with the item to tap highlighted. */
@Composable
private fun SettingsPreview(on: Boolean) {
    val c = LocalOrbit.current
    Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(c.surf).padding(horizontal = 12.dp, vertical = 10.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Use wireless debugging", fontSize = 12.sp, fontWeight = FontWeight.Bold, color = c.text, modifier = Modifier.weight(1f))
            Box(Modifier.width(34.dp).height(20.dp).clip(RoundedCornerShape(10.dp)).background(c.primary).padding(3.dp),
                contentAlignment = Alignment.CenterEnd) {
                Box(Modifier.size(14.dp).clip(CircleShape).background(c.onPrimary))
            }
        }
        Box(Modifier.fillMaxWidth().height(1.dp).background(c.surfHi))
        Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(8.dp)).background(c.pc).padding(horizontal = 8.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Qr, null, Modifier.size(14.dp), tint = c.onPc)
            Spacer(Modifier.width(8.dp))
            Text("Pair device with QR code", fontSize = 12.sp, fontWeight = FontWeight.ExtraBold, color = c.onPc, modifier = Modifier.weight(1f))
            Text("← tap", fontSize = 12.sp, fontWeight = FontWeight.ExtraBold, color = c.onPc)
        }
        Text("Pair device with pairing code", fontSize = 12.sp, color = c.outline)
    }
}
