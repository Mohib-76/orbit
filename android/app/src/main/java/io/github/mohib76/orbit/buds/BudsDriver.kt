package io.github.mohib76.orbit.buds

import android.bluetooth.BluetoothDevice
import org.json.JSONObject

/**
 * One brand's control protocol (noise modes, settings, touch). Everything else (connection, OS battery,
 * playback, syncing with the PC) is the same for all buds and lives in [Buds].
 *
 * To add a brand: implement this, add it to [BudsDrivers.forDevice], and port it to the Windows app
 * (`Core/Buds/IBudsDriver.cs`). A driver only on one side still works on both: the other side relays
 * its commands (`orbit.buds.cmd`).
 */
interface BudsDriver {
    /** Stable id, sent to the PC in `orbit.buds.state` ("heymelody"). */
    val id: String
    val isOpen: Boolean

    /** Opens the control channel. Worker thread; blocking. */
    fun open(device: BluetoothDevice): Boolean

    /** Asks the buds for everything (battery, modes, settings) after [open]. Worker thread. */
    fun refresh()

    /** Closes the control channel. Any thread. */
    fun close()

    /** Runs one `orbit.buds.cmd` op. Worker thread. */
    fun execute(op: JSONObject)
}

/** What a driver reports to. */
interface BudsHost {
    /** Changes the state (any thread). */
    fun update(f: BudsState.() -> BudsState)

    /** The control channel dropped (any thread). */
    fun closed(driver: BudsDriver)

    /** Runs [block] on the worker thread, after the commands already queued. */
    fun post(block: () -> Unit)
}

object BudsDrivers {
    /** The driver for these buds, or null if Orbit has none for them (battery and playback only). */
    fun forDevice(device: BluetoothDevice, host: BudsHost): BudsDriver? = when {
        HeyMelody.matches(device) -> HeyMelody(host)
        else -> null
    }
}
