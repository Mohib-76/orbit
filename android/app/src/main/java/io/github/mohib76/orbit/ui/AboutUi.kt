package io.github.mohib76.orbit.ui

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
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
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.mohib76.orbit.BuildConfig
import io.github.mohib76.orbit.Hub
import io.github.mohib76.orbit.Proto

object AppLinks {
    const val REPO = "https://github.com/Mohib-76/orbit"
    const val ISSUES = "$REPO/issues/new/choose"
    const val PRIVACY = "$REPO/blob/main/PRIVACY.md"
    const val USB_DEBUGGING = "$REPO/blob/main/docs/TROUBLESHOOTING.md#turn-on-usb-debugging"
}

/** Version, project links and the open-source licenses (bundled from the repository root at build time). */
@Composable
fun AboutScreen(onBack: () -> Unit) {
    val c = LocalOrbit.current
    val ctx = LocalContext.current
    val notices = remember {
        try { ctx.assets.open("THIRD_PARTY_NOTICES.md").bufferedReader().readText() } catch (_: Exception) { "See THIRD_PARTY_NOTICES.md in the source repository." }
    }
    fun open(url: String) = try { ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url))) } catch (_: Exception) { }

    Column(Modifier.fillMaxSize().background(c.bg).statusBarsPadding().navigationBarsPadding()) {
        Row(Modifier.height(64.dp).padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(48.dp).clip(RoundedCornerShape(24.dp)).clickable(onClick = onBack), contentAlignment = Alignment.Center) {
                Icon(Icons.Back, "Back", Modifier.size(22.dp), tint = c.text)
            }
            Text("About Orbit", fontSize = 22.sp, fontWeight = FontWeight.ExtraBold, color = c.text)
        }
        Column(Modifier.verticalScroll(rememberScrollState()).padding(horizontal = 20.dp).padding(bottom = 24.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                OrbitLogo(48.dp)
                Spacer(Modifier.width(14.dp))
                Column {
                    Text("Orbit", fontSize = 24.sp, fontWeight = FontWeight.ExtraBold, color = c.text)
                    Text("Version ${BuildConfig.VERSION_NAME} · protocol ${Proto.VERSION}" +
                        (if (Hub.peerAppVersion.isNotEmpty()) " · PC app ${Hub.peerAppVersion}" else ""),
                        fontSize = 13.sp, color = c.text2)
                }
            }
            Text("Your phone and PC, in one orbit. Free and open-source software under the GNU Affero General Public License v3.0. " +
                "Orbit is an independent project, not affiliated with realme, OnePlus, OPPO, Samsung, Google, Microsoft or Genymobile.",
                fontSize = 14.sp, color = c.text2, lineHeight = 20.sp)
            Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                PillButton("Source code", filled = false) { open(AppLinks.REPO) }
                PillButton("Report a bug", filled = false) { open(AppLinks.ISSUES) }
                PillButton("Privacy", filled = false) { open(AppLinks.PRIVACY) }
                PillButton("License", filled = false) { open("https://www.gnu.org/licenses/agpl-3.0.html") }
            }
            SectionLabel("Open-source licenses")
            Text(notices, Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(c.surf).padding(14.dp),
                fontSize = 12.sp, fontFamily = FontFamily.Monospace, color = c.text, lineHeight = 17.sp)
        }
    }
}
