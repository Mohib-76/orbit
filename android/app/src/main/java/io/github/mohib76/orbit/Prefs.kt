package io.github.mohib76.orbit

import android.content.Context
import android.content.SharedPreferences
import android.os.Build
import android.provider.Settings
import java.util.UUID

data class PairedPc(val id: String, val name: String, val fingerprint: String, val pairedAt: Long)

/** Small typed wrapper around SharedPreferences. */
class Prefs(private val ctx: Context) {
    private val sp: SharedPreferences = ctx.getSharedPreferences("orbit", Context.MODE_PRIVATE)

    val deviceId: String
        get() = sp.getString("deviceId", null) ?: UUID.randomUUID().toString().replace("-", "").also {
            sp.edit().putString("deviceId", it).apply()
        }

    var deviceName: String
        get() = sp.getString("deviceName", null)
            ?: Settings.Global.getString(ctx.contentResolver, "device_name")
            ?: Build.MODEL
        set(v) = sp.edit().putString("deviceName", v).apply()

    val model: String get() = Build.MODEL

    var pc: PairedPc?
        get() {
            val id = sp.getString("pc.id", null) ?: return null
            return PairedPc(id, sp.getString("pc.name", "PC")!!, sp.getString("pc.fp", "")!!, sp.getLong("pc.at", 0))
        }
        set(v) {
            val e = sp.edit()
            if (v == null) e.remove("pc.id").remove("pc.name").remove("pc.fp").remove("pc.at")
            else e.putString("pc.id", v.id).putString("pc.name", v.name).putString("pc.fp", v.fingerprint).putLong("pc.at", v.pairedAt)
            e.apply()
        }

    var onboarded by bool("onboarded", false)
    var clipboardAutoReceive by bool("clip.auto", true)
    var mirrorNotifications by bool("notif.on", true)
    var autoAcceptFiles by bool("files.auto", true)
    /** Mirrors the PC's setting; the PC is the one that actually flips USB tethering on. */
    var autoUsbTethering by bool("usb.tether", true)

    /** "system", "light" or "dark". */
    var theme: String
        get() = sp.getString("theme", "system")!!
        set(v) = sp.edit().putString("theme", v).apply()

    /** Earbuds feature on/off (kept in sync with the PC). */
    var budsEnabled by bool("buds.on", true)
    var budsMac: String?
        get() = sp.getString("buds.mac", null)
        set(v) = sp.edit().putString("buds.mac", v).apply()
    var budsName: String?
        get() = sp.getString("buds.name", null)
        set(v) = sp.edit().putString("buds.name", v).apply()

    var disabledApps: Set<String>
        get() = sp.getStringSet("notif.disabled", emptySet())!!.toSet()
        set(v) = sp.edit().putStringSet("notif.disabled", v).apply()

    private fun bool(key: String, def: Boolean) = object : kotlin.properties.ReadWriteProperty<Any?, Boolean> {
        override fun getValue(thisRef: Any?, property: kotlin.reflect.KProperty<*>) = sp.getBoolean(key, def)
        override fun setValue(thisRef: Any?, property: kotlin.reflect.KProperty<*>, value: Boolean) {
            sp.edit().putBoolean(key, value).apply()
        }
    }
}
