package io.github.mohib76.orbit.media

import org.json.JSONArray
import org.json.JSONObject

/** One media player as it travels in orbit.media.state. Positions are in milliseconds. */
data class Player(
    val id: String, val app: String, val title: String, val artist: String, val album: String,
    val playing: Boolean, val pos: Long, val len: Long,
    val canPlay: Boolean, val canPause: Boolean, val canNext: Boolean, val canPrev: Boolean, val canSeek: Boolean,
    val art: String
) {
    val live: Boolean get() = len <= 0

    fun toJson(): JSONObject = JSONObject()
        .put("id", id).put("app", app).put("title", title).put("artist", artist).put("album", album)
        .put("playing", playing).put("pos", pos).put("len", len)
        .put("canPlay", canPlay).put("canPause", canPause).put("canNext", canNext).put("canPrev", canPrev).put("canSeek", canSeek)
        .put("art", art)

    companion object {
        fun fromJson(o: JSONObject?): Player? {
            val id = o?.optString("id").orEmpty()
            if (o == null || id.isEmpty()) return null
            return Player(
                id, o.optString("app"), o.optString("title"), o.optString("artist"), o.optString("album"),
                o.optBoolean("playing"), o.optLong("pos").coerceAtLeast(0), o.optLong("len").coerceAtLeast(0),
                o.optBoolean("canPlay", true), o.optBoolean("canPause", true), o.optBoolean("canNext"), o.optBoolean("canPrev"),
                o.optBoolean("canSeek"), o.optString("art")
            )
        }
    }
}

/** What one side reported. [receivedAt] is SystemClock.elapsedRealtime() when it arrived (or was built). */
data class MediaSnapshot(val players: List<Player>, val active: String?, val controllable: Boolean, val receivedAt: Long) {
    /** The player to show: the user's pick if it still exists, else the sender's current one, else a playing one. */
    fun pick(userPick: String?): Player? =
        players.firstOrNull { it.id == userPick } ?: players.firstOrNull { it.id == active }
            ?: players.firstOrNull { it.playing } ?: players.firstOrNull()

    /** Position now: while playing, advance from the moment the packet arrived. Clamped to the length. */
    fun positionNow(p: Player, now: Long): Long {
        var pos = p.pos
        if (p.playing && receivedAt > 0) pos += now - receivedAt
        return if (p.len > 0) pos.coerceIn(0, p.len) else pos.coerceAtLeast(0)
    }

    fun toJson(): JSONObject = JSONObject()
        .put("players", JSONArray().apply { players.forEach { put(it.toJson()) } })
        .put("active", active ?: JSONObject.NULL)
        .put("controllable", controllable)

    companion object {
        val EMPTY = MediaSnapshot(emptyList(), null, true, 0)

        fun fromJson(b: JSONObject, receivedAt: Long): MediaSnapshot {
            val arr = b.optJSONArray("players") ?: JSONArray()
            val players = (0 until arr.length()).mapNotNull { Player.fromJson(arr.optJSONObject(it)) }
            val active = if (b.isNull("active")) null else b.optString("active").ifEmpty { null }
            return MediaSnapshot(players, active, b.optBoolean("controllable", true), receivedAt)
        }

        /** "1:42", "1:12:05". */
        fun clock(ms: Long): String {
            val s = (ms.coerceAtLeast(0) / 1000)
            val h = s / 3600; val m = (s / 60) % 60; val sec = s % 60
            return if (h > 0) "%d:%02d:%02d".format(h, m, sec) else "%d:%02d".format(m, sec)
        }
    }
}
