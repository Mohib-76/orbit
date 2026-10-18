package io.github.mohib76.orbit.buds

import org.json.JSONArray
import org.json.JSONObject

/** A noise-control mode the buds offer. `id` is what `orbit.buds.cmd {op: "anc"}` carries. */
data class NoiseMode(val id: String, val label: String)

/** An on/off setting the buds reported. */
data class BudsToggle(val id: String, val title: String, val subtitle: String, val on: Boolean)

/**
 * Everything one device knows about the buds. The driver fills in the controls; without one (buds Orbit
 * has no protocol for) only the connection and the OS battery level are known.
 */
data class BudsState(
    /** The OS has the buds connected to this device. */
    val connected: Boolean = false,
    /** A driver holds the buds' control channel here, so the controls are live. */
    val control: Boolean = false,
    /** The driver that matches these buds ("heymelody"), or null: battery and playback only. */
    val driver: String? = null,
    val name: String = "Earbuds",
    val mac: String? = null,
    /** Current noise mode id; one of [noiseModes]. */
    val noise: String? = null,
    /** Only the modes the buds answered for, so buds without noise control show none. */
    val noiseModes: List<NoiseMode> = emptyList(),
    /** HeyMelody: bitmask of AncMode codes cycled by the noise-control touch action; 0 = unknown. */
    val cycleMask: Int = 0,
    /** Left, right, case from the driver; -1 = unknown. */
    val battery: List<Int> = listOf(-1, -1, -1),
    val charging: List<Boolean> = listOf(false, false, false),
    /** One overall level from the OS (the headset's standard battery report); -1 = unknown. */
    val batteryMain: Int = -1,
    /** Only toggles the buds reported, so unsupported ones never show up. */
    val toggles: List<BudsToggle> = emptyList(),
    /** HeyMelody touch settings. */
    val touch: Map<TouchKey, Int> = emptyMap(),
) {
    val hasSideBattery get() = battery.any { it >= 0 }
    val noiseLabel get() = noiseModes.firstOrNull { it.id == noise }?.label

    /** The mode after the current one, for one-tap cycling. */
    fun nextNoise(): String? {
        if (noiseModes.isEmpty()) return null
        val i = noiseModes.indexOfFirst { it.id == noise }
        return noiseModes[(i + 1) % noiseModes.size].id
    }

    fun withToggle(id: String, on: Boolean) = copy(toggles = toggles.map { if (it.id == id) it.copy(on = on) else it })

    /** Adds or updates toggles, keeping [order] (the driver's catalog order). */
    fun mergeToggles(new: List<BudsToggle>, order: List<String>): BudsState {
        val byId = toggles.associateBy { it.id } + new.associateBy { it.id }
        return copy(toggles = byId.values.sortedBy { order.indexOf(it.id) })
    }

    /** What an `orbit.buds.cmd` op will change, shown before the buds confirm it. */
    fun apply(op: JSONObject): BudsState = when (op.optString("op")) {
        "anc" -> op.optString("mode").let { m -> if (noiseModes.any { it.id == m }) copy(noise = m) else this }
        "feature" -> withToggle(op.optString("id"), op.optBoolean("on"))
        "touch" -> copy(touch = touch + (TouchKey(op.optInt("side"), op.optInt("gesture")) to op.optInt("action")))
        "cycle" -> copy(cycleMask = op.optInt("mask"))
        else -> this
    }

    fun toJson(): JSONObject = JSONObject()
        .put("connected", connected).put("control", control).put("driver", driver ?: JSONObject.NULL)
        .put("name", name).put("mac", mac ?: JSONObject.NULL)
        .put("anc", noise ?: JSONObject.NULL)
        .put("noiseModes", JSONArray().apply { noiseModes.forEach { put(JSONObject().put("id", it.id).put("label", it.label)) } })
        .put("cycle", cycleMask)
        .put("battery", JSONArray(battery)).put("charging", JSONArray(charging)).put("batteryMain", batteryMain)
        .put("toggles", JSONArray().apply {
            toggles.forEach { put(JSONObject().put("id", it.id).put("title", it.title).put("subtitle", it.subtitle).put("on", it.on)) }
        })
        // Older Orbit versions read these instead of "toggles".
        .put("features", JSONObject().apply { toggles.forEach { put(it.id, it.on) } })
        .put("touch", JSONArray().apply { touch.forEach { (k, a) -> put(JSONArray(listOf(k.side, k.gesture, a))) } })

    companion object {
        fun fromJson(o: JSONObject): BudsState {
            val bat = o.optJSONArray("battery")
            val chg = o.optJSONArray("charging")
            val touch = o.optJSONArray("touch")
            val connected = o.optBoolean("connected")
            // Older versions only sent "connected", meaning the control channel (always HeyMelody).
            val legacy = !o.has("control")
            val control = if (legacy) connected else o.optBoolean("control")
            val noise = o.optString("anc").takeIf { it.isNotEmpty() && it != "null" }
            val modes = o.optJSONArray("noiseModes")?.let { a ->
                (0 until a.length()).map { a.getJSONObject(it) }.map { NoiseMode(it.getString("id"), it.optString("label", it.getString("id"))) }
            } ?: if (noise != null) HeyMelody.NOISE_MODES else emptyList()
            val toggles = o.optJSONArray("toggles")?.let { a ->
                (0 until a.length()).map { a.getJSONObject(it) }
                    .map { BudsToggle(it.getString("id"), it.optString("title"), it.optString("subtitle"), it.optBoolean("on")) }
            } ?: o.optJSONObject("features")?.let { f ->
                Feature.entries.filter { f.has(it.id.toString()) }.map { it.toggle(f.getBoolean(it.id.toString())) }
            } ?: emptyList()
            return BudsState(
                connected = connected,
                control = control,
                driver = o.optString("driver").takeIf { it.isNotEmpty() && it != "null" } ?: if (legacy && connected) HeyMelody.ID else null,
                name = o.optString("name", "Earbuds"),
                mac = o.optString("mac").takeIf { it.isNotEmpty() && it != "null" },
                noise = noise,
                noiseModes = modes,
                cycleMask = o.optInt("cycle"),
                battery = (0..2).map { bat?.optInt(it, -1) ?: -1 },
                charging = (0..2).map { chg?.optBoolean(it) ?: false },
                batteryMain = o.optInt("batteryMain", -1),
                toggles = toggles,
                touch = buildMap {
                    if (touch != null) for (i in 0 until touch.length()) {
                        val t = touch.getJSONArray(i)
                        put(TouchKey(t.getInt(0), t.getInt(1)), t.getInt(2))
                    }
                }
            )
        }
    }
}

/** What the PC reports about its own link to the buds (and whether it is playing audio). */
data class PeerBuds(val enabled: Boolean, val state: BudsState, val playing: Boolean)
