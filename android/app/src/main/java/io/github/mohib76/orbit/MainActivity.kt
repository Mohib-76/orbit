package io.github.mohib76.orbit

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Bundle
import android.view.KeyEvent
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.codescanner.GmsBarcodeScannerOptions
import com.google.mlkit.vision.codescanner.GmsBarcodeScanning
import io.github.mohib76.orbit.buds.Buds
import io.github.mohib76.orbit.buds.BudsService
import io.github.mohib76.orbit.ui.MainScreen
import io.github.mohib76.orbit.ui.MirrorSetupScreen
import io.github.mohib76.orbit.ui.OnboardingScreen
import io.github.mohib76.orbit.ui.OrbitTheme
import io.github.mohib76.orbit.ui.PairDialog
import io.github.mohib76.orbit.ui.PairScreen

class MainActivity : ComponentActivity() {
    companion object {
        const val ACTION_PAIR = "io.github.mohib76.orbit.PAIR"
        const val ACTION_PICK_FILES = "io.github.mohib76.orbit.PICK_FILES"
        const val ACTION_FILES = "io.github.mohib76.orbit.FILES"
        const val ACTION_BUDS = "io.github.mohib76.orbit.BUDS"
        const val ACTION_MIRROR_SETUP = "io.github.mohib76.orbit.MIRROR_SETUP"
    }

    private var mirrorSetup by mutableStateOf<Boolean?>(null) // null = hidden, true = asked by the PC

    private var tab by mutableIntStateOf(0)
    private var showBuds by mutableStateOf(false)
    private val btPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) {
            Hub.setBudsEnabled(true)
            if (!Buds.autoSelect()) tab = 3 // several candidates: pick in Settings
        } else Toast.makeText(this, "Orbit needs Bluetooth access to reach your earbuds", Toast.LENGTH_LONG).show()
        resumeTick++
    }
    private var resumeTick by mutableIntStateOf(0)
    private var onboarded by mutableStateOf(false)

    private val pickFiles = registerForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { uris ->
        if (uris.isNotEmpty()) {
            Hub.sendFiles(uris)
            tab = 2
        }
    }
    private val notifPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { resumeTick++ }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        onboarded = Hub.prefs.onboarded
        handleIntent(intent)
        setContent {
            OrbitTheme {
                val ui by Hub.ui.collectAsState()
                val offer by Hub.offer.collectAsState()
                when {
                    !onboarded -> OnboardingScreen(resumeTick, needsNotifPermission(), { notifPermission.launch(Manifest.permission.POST_NOTIFICATIONS) }) {
                        Hub.prefs.onboarded = true
                        onboarded = true
                        if (needsNotifPermission()) notifPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
                    }
                    ui.conn == Conn.NotPaired || Hub.prefs.pc == null -> PairScreen(::scanQr)
                    mirrorSetup != null -> MirrorSetupScreen(resumeTick, mirrorSetup == true) { mirrorSetup = null }
                    else -> MainScreen(tab, { tab = it }, resumeTick, { pickFiles.launch(arrayOf("*/*")) },
                        showBuds, { showBuds = it }, { btPermission.launch(Manifest.permission.BLUETOOTH_CONNECT) })
                }
                offer?.let { PairDialog(it) }
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleIntent(intent)
    }

    override fun onResume() {
        super.onResume()
        resumeTick++
        OrbitService.start(this)
        BudsService.start(this)
    }

    /** The slides remote can turn the volume keys into Previous / Next while it's open. */
    override fun onKeyDown(keyCode: Int, event: KeyEvent): Boolean {
        val handler = io.github.mohib76.orbit.remote.RemoteInput.volumeKeys
        if (handler != null && isVolumeKey(keyCode)) {
            if (event.repeatCount == 0) handler(keyCode == KeyEvent.KEYCODE_VOLUME_UP)
            return true
        }
        return super.onKeyDown(keyCode, event)
    }

    override fun onKeyUp(keyCode: Int, event: KeyEvent): Boolean =
        if (io.github.mohib76.orbit.remote.RemoteInput.volumeKeys != null && isVolumeKey(keyCode)) true else super.onKeyUp(keyCode, event)

    private fun isVolumeKey(code: Int) = code == KeyEvent.KEYCODE_VOLUME_UP || code == KeyEvent.KEYCODE_VOLUME_DOWN

    private fun needsNotifPermission() =
        checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED

    private fun handleIntent(i: Intent?) {
        when (i?.action) {
            ACTION_PAIR -> i.getStringExtra("payload")?.let { offerFrom(it) }
            Intent.ACTION_VIEW -> i.dataString?.takeIf { it.startsWith("orbit://pair") }?.let { offerFrom(it) }
            ACTION_PICK_FILES -> pickFiles.launch(arrayOf("*/*"))
            ACTION_FILES -> tab = 2
            ACTION_BUDS -> { tab = 0; showBuds = true }
            ACTION_MIRROR_SETUP -> mirrorSetup = i.getBooleanExtra("fromPc", true)
        }
    }

    private fun offerFrom(raw: String) {
        val o = Hub.parsePairPayload(raw)
        if (o == null) Toast.makeText(this, "That isn't an Orbit pairing code", Toast.LENGTH_SHORT).show()
        else Hub.offer.value = o // the dialog shows over whatever screen is up, onboarding included
    }

    private fun scanQr() {
        val opts = GmsBarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE).enableAutoZoom().build()
        GmsBarcodeScanning.getClient(this, opts).startScan()
            .addOnSuccessListener { code -> code.rawValue?.let { offerFrom(it) } }
            .addOnFailureListener { Toast.makeText(this, "Scanner unavailable: ${it.message}", Toast.LENGTH_LONG).show() }
    }
}
