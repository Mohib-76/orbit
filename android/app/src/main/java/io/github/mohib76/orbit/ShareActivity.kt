package io.github.mohib76.orbit

import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.mohib76.orbit.ui.Dot
import io.github.mohib76.orbit.ui.Ellipsized
import io.github.mohib76.orbit.ui.IconChip
import io.github.mohib76.orbit.ui.Icons
import io.github.mohib76.orbit.ui.LocalOrbit
import io.github.mohib76.orbit.ui.OrbitTheme
import io.github.mohib76.orbit.ui.PillButton

/** "Share → Orbit": a bottom sheet that sends files (or text, to the PC clipboard). */
class ShareActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val uris = extractUris(intent)
        val text = intent.getStringExtra(Intent.EXTRA_TEXT)?.takeIf { uris.isEmpty() }
        if (Hub.prefs.pc == null) {
            Toast.makeText(this, "Pair Orbit with your PC first", Toast.LENGTH_LONG).show()
            startActivity(Intent(this, MainActivity::class.java)); finish(); return
        }
        OrbitService.start(this)
        setContent { OrbitTheme { Sheet(uris, text) } }
    }

    @Suppress("DEPRECATION")
    private fun extractUris(i: Intent): List<Uri> = when (i.action) {
        Intent.ACTION_SEND -> listOfNotNull(
            if (Build.VERSION.SDK_INT >= 33) i.getParcelableExtra(Intent.EXTRA_STREAM, Uri::class.java) else i.getParcelableExtra(Intent.EXTRA_STREAM)
        )
        Intent.ACTION_SEND_MULTIPLE ->
            (if (Build.VERSION.SDK_INT >= 33) i.getParcelableArrayListExtra(Intent.EXTRA_STREAM, Uri::class.java) else i.getParcelableArrayListExtra(Intent.EXTRA_STREAM))
                ?: emptyList()
        else -> emptyList()
    }

    private fun names(uris: List<Uri>): List<Pair<String, Long>> = uris.map { uri ->
        var name = uri.lastPathSegment ?: "file"; var size = -1L
        try {
            contentResolver.query(uri, arrayOf(android.provider.OpenableColumns.DISPLAY_NAME, android.provider.OpenableColumns.SIZE), null, null, null)?.use { c ->
                if (c.moveToFirst()) { c.getString(0)?.let { name = it }; if (!c.isNull(1)) size = c.getLong(1) }
            }
        } catch (_: Exception) { }
        name to size
    }

    @Composable
    private fun Sheet(uris: List<Uri>, text: String?) {
        val c = LocalOrbit.current
        val ui by Hub.ui.collectAsState()
        val files = remember { names(uris) }
        Box(
            Modifier.fillMaxSize().clickable(remember { MutableInteractionSource() }, null) { finish() },
            contentAlignment = Alignment.BottomCenter
        ) {
            Column(
                Modifier.fillMaxWidth().clip(RoundedCornerShape(topStart = 28.dp, topEnd = 28.dp)).background(c.bg)
                    .clickable(remember { MutableInteractionSource() }, null) { }
                    .navigationBarsPadding().padding(20.dp),
                verticalArrangement = Arrangement.spacedBy(14.dp)
            ) {
                Box(Modifier.align(Alignment.CenterHorizontally).size(32.dp, 4.dp).clip(RoundedCornerShape(2.dp)).background(c.outline))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    IconChip(Icons.Laptop, 44.dp, c.pc, c.onPc)
                    Column(Modifier.padding(start = 12.dp)) {
                        Text("Send to ${ui.pcName}", fontSize = 18.sp, fontWeight = FontWeight.ExtraBold, color = c.text)
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Dot(if (ui.conn == Conn.Connected) c.green else c.amber, 7.dp)
                            Spacer(Modifier.width(6.dp))
                            Text(if (ui.conn == Conn.Connected) "Connected · ${ui.link}" else "Not connected · sends when it reconnects",
                                fontSize = 12.sp, color = c.text2, fontWeight = FontWeight.SemiBold)
                        }
                    }
                }
                if (text != null) {
                    Box(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(c.surf).padding(14.dp)) {
                        Ellipsized(text, 14, c.text, lines = 5)
                    }
                    PillButton("Copy to PC clipboard", Icons.Clip, Modifier.fillMaxWidth()) {
                        val ok = Hub.sendClipboard(text)
                        Toast.makeText(this@ShareActivity, if (ok) "Sent to ${ui.pcName}" else "PC not connected", Toast.LENGTH_SHORT).show()
                        finish()
                    }
                } else {
                    LazyColumn(Modifier.heightIn(max = 280.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        items(files) { (name, size) ->
                            Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(c.surf).padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
                                Box(Modifier.size(40.dp).clip(RoundedCornerShape(12.dp)).background(c.surfHi), contentAlignment = Alignment.Center) {
                                    Text(name.substringAfterLast('.', "file").take(4).uppercase(), fontSize = 10.sp, fontWeight = FontWeight.ExtraBold, color = c.primaryText)
                                }
                                Column(Modifier.padding(start = 12.dp)) {
                                    Ellipsized(name, 14, c.text, bold = true)
                                    Text(if (size >= 0) formatSize(size) else "", fontSize = 12.sp, color = c.text2)
                                }
                            }
                        }
                    }
                    PillButton("Send ${if (files.size == 1) "file" else "${files.size} files"}", Icons.Upload, Modifier.fillMaxWidth()) {
                        Hub.sendFiles(uris)
                        Toast.makeText(this@ShareActivity, "Sending to ${ui.pcName}", Toast.LENGTH_SHORT).show()
                        finish()
                    }
                }
                Spacer(Modifier.height(4.dp))
            }
        }
    }
}
