package io.github.mohib76.orbit.usb

import android.annotation.SuppressLint
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.util.Log
import io.github.mohib76.orbit.Hub
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.update
import org.json.JSONObject

/**
 * USB modes: charging only, USB tethering or file sharing while the phone is plugged into the PC. Android
 * doesn't let apps switch the USB mode, so the PC does it over adb; this side reports what the port is doing
 * (from the system's USB_STATE broadcast, which also sees switches made from Android's own USB menu) and
 * passes the user's choice on.
 */
@SuppressLint("StaticFieldLeak")
object Usb {
    const val CHARGING = "charging"
    const val TETHERING = "tethering"
    const val FILES = "files"
    const val OTHER = "other"
    val MODES = listOf(CHARGING, TETHERING, FILES)

    /** The phone's own view: is a USB data cable in, and what mode its port is in. */
    data class Port(val cable: Boolean = false, val mode: String = CHARGING)
    /** What the PC said: can it switch (USB debugging authorized), what it's switching to, and is USB mirroring on. */
    data class Control(val adb: Boolean = false, val switching: String = "", val mirroring: Boolean = false)

    val port = MutableStateFlow(Port())
    val control = MutableStateFlow(Control())
    private lateinit var app: Context

    // UsbManager.ACTION_USB_STATE and its extras are hidden constants, but the broadcast is sticky and public.
    private const val ACTION_USB_STATE = "android.hardware.usb.action.USB_STATE"

    private val receiver = object : BroadcastReceiver() {
        override fun onReceive(c: Context, i: Intent) = onUsbState(i)
    }

    fun init(ctx: Context) {
        app = ctx.applicationContext
        try { app.registerReceiver(receiver, IntentFilter(ACTION_USB_STATE))?.let { onUsbState(it) } }
        catch (e: Exception) { Log.w(Hub.TAG, "usb: ${e.message}") }
    }

    private fun onUsbState(i: Intent) {
        val p = Port(cable = i.getBooleanExtra("connected", false), mode = usbModeOf { i.getBooleanExtra(it, false) })
        if (p == port.value) return
        Log.i(Hub.TAG, "usb: cable=${p.cable} mode=${p.mode}")
        port.value = p
        send()
    }

    fun send() {
        val p = port.value
        Hub.link?.send("orbit.usb.state", JSONObject().put("cable", p.cable).put("mode", p.mode))
    }

    fun onControl(b: JSONObject) {
        control.value = Control(b.optBoolean("adb"), b.optString("switching"), b.optBoolean("mirroring"))
    }

    fun onPeerGone() { control.value = Control() }

    /** The user picked a mode here; the PC switches it. */
    fun request(mode: String) {
        val l = Hub.link ?: return
        if (mode == port.value.mode || control.value.switching.isNotEmpty()) return
        l.send("orbit.usb.set", JSONObject().put("mode", mode))
        control.update { it.copy(switching = mode) }
    }

    /** "" = can switch; else why not: "noPc" | "debugOff" | "mirroring" | "switching". */
    fun blocked(c: Control = control.value): String = when {
        Hub.link == null -> "noPc"
        c.switching.isNotEmpty() -> "switching"
        c.mirroring -> "mirroring"
        !c.adb -> "debugOff"
        else -> ""
    }

    fun setDefault(mode: String) {
        Hub.prefs.usbMode = mode
        Hub.pushSettings()
        Hub.settingsVersion.update { it + 1 }
    }
}

/** From USB_STATE extras: "connected", "configured", and one boolean per function ("rndis", "mtp"…). */
fun usbModeOf(extras: (String) -> Boolean): String = when {
    extras("rndis") || extras("ncm") -> Usb.TETHERING
    extras("mtp") -> Usb.FILES
    extras("ptp") || extras("midi") -> Usb.OTHER
    else -> Usb.CHARGING
}
