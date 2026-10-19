package io.github.mohib76.orbit.media

import android.annotation.SuppressLint
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.drawable.Icon
import android.media.MediaMetadata
import android.media.session.MediaController
import android.media.session.MediaSession
import android.media.session.MediaSessionManager
import android.media.session.PlaybackState
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.util.Base64
import android.util.Log
import io.github.mohib76.orbit.Hub
import io.github.mohib76.orbit.MainActivity
import io.github.mohib76.orbit.NotifListener
import io.github.mohib76.orbit.R
import kotlinx.coroutines.flow.MutableStateFlow
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.security.MessageDigest

/**
 * Media controls both ways. This phone publishes its media sessions to the PC (read through the notification
 * listener's access) and runs the PC's commands; the PC's players show on Home and as a system media player
 * (notification shade, lock screen, Quick Settings). Track titles never go to the log.
 */
@SuppressLint("StaticFieldLeak")
object Media {
    private const val TAG = "Orbit"
    private const val CHANNEL = "pc_media"
    private const val NOTIF_ID = 40
    private const val ACTION_PREV = "io.github.mohib76.orbit.MEDIA_PREV"
    private const val ACTION_TOGGLE = "io.github.mohib76.orbit.MEDIA_TOGGLE"
    private const val ACTION_NEXT = "io.github.mohib76.orbit.MEDIA_NEXT"

    private lateinit var app: Context
    private val main = Handler(Looper.getMainLooper())

    /** The PC's players as last reported. */
    val peer = MutableStateFlow(MediaSnapshot.EMPTY)
    /** The player the user picked in the switcher (null = follow the PC). */
    val pick = MutableStateFlow<String?>(null)
    /** Bumped when new album art arrives from the PC. */
    val artVersion = MutableStateFlow(0)
    private val peerArt = LinkedHashMap<String, Bitmap>()

    fun init(ctx: Context) {
        app = ctx.applicationContext
        app.getSystemService(NotificationManager::class.java).createNotificationChannel(
            NotificationChannel(CHANNEL, app.getString(R.string.media_channel), NotificationManager.IMPORTANCE_LOW).apply { setShowBadge(false) })
    }

    // ================================================================= this phone → PC

    private var msm: MediaSessionManager? = null
    private val tracked = LinkedHashMap<MediaSession.Token, Pair<MediaController, MediaController.Callback>>()
    @Volatile private var peerWants = true
    private val artSent = HashSet<String>()
    /** Album art bitmaps already encoded: bitmap identity → (hash, jpeg). */
    private val artCache = HashMap<Int, Pair<String, ByteArray>>()
    private var lastSent: MediaSnapshot? = null

    private val sessionsListener = MediaSessionManager.OnActiveSessionsChangedListener { list -> main.post { track(list ?: emptyList()) } }

    /** Starts watching this phone's media sessions. Needs notification access; safe to call again. */
    fun startLocal() = main.post {
        if (msm != null || !NotifListener.isEnabled(app)) return@post
        try {
            val m = app.getSystemService(MediaSessionManager::class.java)
            val cn = ComponentName(app, NotifListener::class.java)
            m.addOnActiveSessionsChangedListener(sessionsListener, cn, main)
            msm = m
            track(m.getActiveSessions(cn))
        } catch (e: SecurityException) {
            Log.w(TAG, "media: no notification access")
        } catch (e: Exception) {
            Log.w(TAG, "media: ${e.javaClass.simpleName}")
        }
    }

    private fun track(list: List<MediaController>) {
        val keep = list.filter { it.packageName != app.packageName }
        val tokens = keep.map { it.sessionToken }.toSet()
        tracked.keys.filter { it !in tokens }.forEach { t -> tracked.remove(t)?.let { (c, cb) -> c.unregisterCallback(cb) } }
        for (c in keep) {
            if (c.sessionToken in tracked) continue
            val cb = object : MediaController.Callback() {
                override fun onPlaybackStateChanged(state: PlaybackState?) = schedulePublish()
                override fun onMetadataChanged(metadata: MediaMetadata?) = schedulePublish()
                override fun onSessionDestroyed() = schedulePublish()
            }
            c.registerCallback(cb, main)
            tracked[c.sessionToken] = c to cb
        }
        // Keep the system's order (most recently active first).
        val ordered = LinkedHashMap<MediaSession.Token, Pair<MediaController, MediaController.Callback>>()
        keep.forEach { c -> tracked[c.sessionToken]?.let { ordered[c.sessionToken] = it } }
        tracked.clear(); tracked.putAll(ordered)
        schedulePublish()
    }

    private val publishNow = Runnable { publish(false) }
    fun schedulePublish() {
        main.removeCallbacks(publishNow)
        main.postDelayed(publishNow, 250)
    }

    /** This phone's players right now (main thread). */
    private fun snapshot(): Pair<MediaSnapshot, Map<String, ByteArray>> {
        val now = SystemClock.elapsedRealtime()
        val players = ArrayList<Player>()
        val jpegs = HashMap<String, ByteArray>()
        val seen = HashMap<String, Int>()
        for ((c, _) in tracked.values) {
            val st = c.playbackState
            val md = c.metadata
            val title = md?.getString(MediaMetadata.METADATA_KEY_TITLE) ?: md?.getString(MediaMetadata.METADATA_KEY_DISPLAY_TITLE) ?: ""
            val state = st?.state ?: PlaybackState.STATE_NONE
            if (title.isEmpty() && state in listOf(PlaybackState.STATE_NONE, PlaybackState.STATE_STOPPED, PlaybackState.STATE_ERROR)) continue
            val playing = state == PlaybackState.STATE_PLAYING || state == PlaybackState.STATE_BUFFERING
            val len = (md?.getLong(MediaMetadata.METADATA_KEY_DURATION) ?: 0L).coerceAtLeast(0)
            var pos = st?.position ?: 0L
            if (playing && st != null && st.lastPositionUpdateTime > 0)
                pos += ((now - st.lastPositionUpdateTime) * st.playbackSpeed).toLong()
            pos = if (len > 0) pos.coerceIn(0, len) else pos.coerceAtLeast(0)
            val a = st?.actions ?: 0L
            fun has(flag: Long) = a and flag != 0L
            val n = (seen[c.packageName] ?: 0) + 1
            seen[c.packageName] = n
            val id = if (n == 1) c.packageName else "${c.packageName}#$n"
            val bmp = md?.getBitmap(MediaMetadata.METADATA_KEY_ART) ?: md?.getBitmap(MediaMetadata.METADATA_KEY_ALBUM_ART)
                ?: md?.getBitmap(MediaMetadata.METADATA_KEY_DISPLAY_ICON)
            val art = bmp?.let { encodeArt(it) }
            if (art != null) jpegs[art.first] = art.second
            players += Player(
                id, appLabel(c.packageName), title,
                md?.getString(MediaMetadata.METADATA_KEY_ARTIST) ?: md?.getString(MediaMetadata.METADATA_KEY_ALBUM_ARTIST)
                    ?: md?.getString(MediaMetadata.METADATA_KEY_DISPLAY_SUBTITLE) ?: "",
                md?.getString(MediaMetadata.METADATA_KEY_ALBUM) ?: "",
                playing, pos, len,
                has(PlaybackState.ACTION_PLAY) || has(PlaybackState.ACTION_PLAY_PAUSE),
                has(PlaybackState.ACTION_PAUSE) || has(PlaybackState.ACTION_PLAY_PAUSE),
                has(PlaybackState.ACTION_SKIP_TO_NEXT), has(PlaybackState.ACTION_SKIP_TO_PREVIOUS),
                has(PlaybackState.ACTION_SEEK_TO) && len > 0,
                art?.first ?: ""
            )
        }
        val active = players.firstOrNull { it.playing }?.id ?: players.firstOrNull()?.id
        return MediaSnapshot(players, active, Hub.prefs.pcControlsMedia, now) to jpegs
    }

    private fun encodeArt(b: Bitmap): Pair<String, ByteArray>? {
        val key = System.identityHashCode(b) * 31 + b.generationId
        artCache[key]?.let { return it }
        return try {
            val scale = minOf(1f, 320f / maxOf(b.width, b.height))
            val small = if (scale < 1f) Bitmap.createScaledBitmap(b, (b.width * scale).toInt().coerceAtLeast(1), (b.height * scale).toInt().coerceAtLeast(1), true) else b
            val out = ByteArrayOutputStream()
            small.compress(Bitmap.CompressFormat.JPEG, 85, out)
            val bytes = out.toByteArray()
            val hash = MessageDigest.getInstance("SHA-1").digest(bytes).take(6).joinToString("") { "%02x".format(it) }
            if (artCache.size > 20) artCache.clear()
            (hash to bytes).also { artCache[key] = it }
        } catch (e: Exception) { null }
    }

    private val labels = HashMap<String, String>()
    private fun appLabel(pkg: String): String = labels.getOrPut(pkg) {
        try { app.packageManager.getApplicationLabel(app.packageManager.getApplicationInfo(pkg, 0)).toString() } catch (_: Exception) { pkg }
    }

    private fun publish(force: Boolean) {
        val l = Hub.link ?: return
        if (!peerWants) return
        val (snap, jpegs) = snapshot()
        val last = lastSent
        if (!force && last != null && !differs(last, snap)) return
        lastSent = snap
        for ((hash, jpeg) in jpegs) {
            if (!artSent.add(hash)) continue
            l.send("orbit.media.art", JSONObject().put("hash", hash).put("jpeg", Base64.encodeToString(jpeg, Base64.NO_WRAP)))
        }
        l.send("orbit.media.state", snap.toJson())
    }

    /** Anything the PC would show differently, including a seek (position off by more than 2 s from the estimate). */
    private fun differs(old: MediaSnapshot, now: MediaSnapshot): Boolean {
        if (old.active != now.active || old.controllable != now.controllable || old.players.size != now.players.size) return true
        for (i in now.players.indices) {
            val a = old.players[i]; val b = now.players[i]
            if (a.copy(pos = 0) != b.copy(pos = 0)) return true
            if (kotlin.math.abs(old.positionNow(a, now.receivedAt) - b.pos) > 2000) return true
        }
        return false
    }

    /** The PC asked to control one of this phone's players. */
    fun onCommand(b: JSONObject) = main.post {
        if (!Hub.prefs.pcControlsMedia) return@post
        val id = b.optString("player")
        val pkg = id.substringBefore('#')
        val n = id.substringAfter('#', "1").toIntOrNull() ?: 1
        val c = tracked.values.map { it.first }.filter { it.packageName == pkg }.getOrNull(n - 1)
            ?: tracked.values.firstOrNull()?.first ?: return@post
        val t = c.transportControls
        when (b.optString("op")) {
            "play" -> t.play()
            "pause" -> t.pause()
            "toggle" -> if (c.playbackState?.state == PlaybackState.STATE_PLAYING) t.pause() else t.play()
            "next" -> t.skipToNext()
            "prev" -> t.skipToPrevious()
            "seek" -> t.seekTo(b.optLong("pos"))
        }
    }


    // ================================================================= PC → this phone

    fun onPeerState(b: JSONObject) {
        val snap = MediaSnapshot.fromJson(b, SystemClock.elapsedRealtime())
        main.post {
            peer.value = snap
            if (pick.value != null && snap.players.none { it.id == pick.value }) pick.value = null
            updateSystemPlayer()
        }
    }

    fun onPeerArt(b: JSONObject) {
        val hash = b.optString("hash")
        if (hash.isEmpty()) return
        val bmp = try {
            val bytes = Base64.decode(b.optString("jpeg"), Base64.DEFAULT)
            BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
        } catch (_: Exception) { null } ?: return
        main.post {
            if (peerArt.size > 30) peerArt.clear()
            peerArt[hash] = bmp
            artVersion.value++
            updateSystemPlayer()
        }
    }

    fun art(hash: String): Bitmap? = if (hash.isEmpty()) null else peerArt[hash]

    /** The player shown here, or null (none, or the user turned PC media off). */
    fun shown(): Player? = if (Hub.prefs.showPcMedia) peer.value.pick(pick.value) else null

    fun choose(id: String) {
        pick.value = id
        updateSystemPlayer()
    }

    /** play | pause | toggle | next | prev | seek, for the PC player shown here. */
    fun command(op: String, pos: Long = 0) {
        val snap = peer.value
        val p = snap.pick(pick.value) ?: return
        val l = Hub.link ?: return
        if (!snap.controllable) return
        val cmd = JSONObject().put("player", p.id).put("op", op)
        if (op == "seek") cmd.put("pos", pos)
        l.send("orbit.media.cmd", cmd)
        // Answer the tap right away; the PC's next state confirms it.
        val now = SystemClock.elapsedRealtime()
        val at = snap.positionNow(p, now)
        val updated = when (op) {
            "play" -> p.copy(playing = true, pos = at)
            "pause" -> p.copy(playing = false, pos = at)
            "toggle" -> p.copy(playing = !p.playing, pos = at)
            "seek" -> p.copy(pos = pos)
            else -> return
        }
        main.post {
            peer.value = snap.copy(players = snap.players.map { if (it.id == p.id) updated else it }, receivedAt = now)
            updateSystemPlayer()
        }
    }

    // ================================================================= connection and settings

    fun onConnected() = main.post {
        artSent.clear(); lastSent = null; peerWants = true
        startLocal()
        publish(true)
    }

    fun onPeerGone() = main.post {
        artSent.clear(); lastSent = null; peerWants = true
        peer.value = MediaSnapshot.EMPTY
        pick.value = null
        updateSystemPlayer()
    }

    fun onPeerWants(wanted: Boolean) = main.post {
        if (wanted == peerWants) return@post
        peerWants = wanted
        lastSent = null
        if (wanted) publish(true)
    }

    fun setShowPc(on: Boolean) {
        Hub.prefs.showPcMedia = on
        Hub.pushSettings()
        main.post {
            if (!on) peer.value = MediaSnapshot.EMPTY
            updateSystemPlayer()
        }
    }

    fun setPcControl(on: Boolean) {
        Hub.prefs.pcControlsMedia = on
        main.post { publish(true) }
    }

    // ================================================================= the system player for the PC's playback

    private var session: MediaSession? = null
    /** When the shown player was last seen playing (elapsedRealtime). A long-paused tab shouldn't sit in the shade. */
    private var lastPlaying = 0L
    private var lastPlayingId = ""
    private const val PAUSED_HIDE_MS = 10 * 60_000L
    private val recheck = Runnable { updateSystemPlayer() }

    private fun updateSystemPlayer() {
        var p = shown()
        val nm = app.getSystemService(NotificationManager::class.java)
        main.removeCallbacks(recheck)
        if (p != null) {
            val now = SystemClock.elapsedRealtime()
            if (p.id != lastPlayingId) { lastPlayingId = p.id; lastPlaying = if (p.playing) now else 0L }
            if (p.playing) lastPlaying = now
            val left = lastPlaying + PAUSED_HIDE_MS - now
            if (!p.playing && left <= 0) p = null
            else if (!p.playing) main.postDelayed(recheck, left)
        }
        if (p == null) {
            session?.let { it.isActive = false; it.release() }
            session = null
            nm.cancel(NOTIF_ID)
            return
        }
        val snap = peer.value
        val s = session ?: MediaSession(app, "OrbitPcMedia").also { ms ->
            ms.setCallback(object : MediaSession.Callback() {
                override fun onPlay() = command("play")
                override fun onPause() = command("pause")
                override fun onSkipToNext() = command("next")
                override fun onSkipToPrevious() = command("prev")
                override fun onSeekTo(pos: Long) = command("seek", pos)
            }, main)
            ms.setSessionActivity(PendingIntent.getActivity(app, 40, Intent(app, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE))
            session = ms
        }
        val pc = Hub.ui.value.pcName
        val subtitle = app.getString(R.string.media_on_pc, p.artist.ifEmpty { p.app }, pc)
        val art = art(p.art)
        s.setMetadata(MediaMetadata.Builder()
            .putString(MediaMetadata.METADATA_KEY_TITLE, p.title.ifEmpty { p.app })
            .putString(MediaMetadata.METADATA_KEY_ARTIST, subtitle)
            .putString(MediaMetadata.METADATA_KEY_ALBUM, p.album)
            .putLong(MediaMetadata.METADATA_KEY_DURATION, if (p.live) -1 else p.len)
            .apply { if (art != null) putBitmap(MediaMetadata.METADATA_KEY_ART, art) }
            .build())
        val control = snap.controllable
        var actions = 0L
        if (control && (p.canPlay || p.canPause)) actions = actions or PlaybackState.ACTION_PLAY or PlaybackState.ACTION_PAUSE or PlaybackState.ACTION_PLAY_PAUSE
        if (control && p.canNext) actions = actions or PlaybackState.ACTION_SKIP_TO_NEXT
        if (control && p.canPrev) actions = actions or PlaybackState.ACTION_SKIP_TO_PREVIOUS
        if (control && p.canSeek) actions = actions or PlaybackState.ACTION_SEEK_TO
        s.setPlaybackState(PlaybackState.Builder()
            .setState(if (p.playing) PlaybackState.STATE_PLAYING else PlaybackState.STATE_PAUSED, p.pos, if (p.playing) 1f else 0f, snap.receivedAt)
            .setActions(actions)
            .build())
        s.isActive = true

        fun act(icon: Int, label: Int, action: String, code: Int) = Notification.Action.Builder(
            Icon.createWithResource(app, icon), app.getString(label),
            PendingIntent.getBroadcast(app, code, Intent(app, MediaActionReceiver::class.java).setAction(action), PendingIntent.FLAG_IMMUTABLE)
        ).build()

        val b = Notification.Builder(app, CHANNEL)
            .setSmallIcon(R.drawable.ic_orbit)
            .setContentTitle(p.title.ifEmpty { p.app })
            .setContentText(subtitle)
            .setSubText(pc)
            .setLargeIcon(art)
            .setOnlyAlertOnce(true)
            .setShowWhen(false)
            .setVisibility(Notification.VISIBILITY_PUBLIC)
            .setContentIntent(PendingIntent.getActivity(app, 41, Intent(app, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE))
        val compact = ArrayList<Int>()
        if (control) {
            if (p.canPrev) { b.addAction(act(R.drawable.ic_media_prev, R.string.media_previous, ACTION_PREV, 42)); compact += compact.size }
            b.addAction(if (p.playing) act(R.drawable.ic_media_pause, R.string.media_pause, ACTION_TOGGLE, 43)
                        else act(R.drawable.ic_media_play, R.string.media_play, ACTION_TOGGLE, 43))
            compact += compact.size
            if (p.canNext) { b.addAction(act(R.drawable.ic_media_next, R.string.media_next, ACTION_NEXT, 44)); compact += compact.size }
        }
        b.setStyle(Notification.MediaStyle().setMediaSession(s.sessionToken).setShowActionsInCompactView(*compact.toIntArray()))
        try { nm.notify(NOTIF_ID, b.build()) } catch (e: SecurityException) { Log.w(TAG, "media: can't post notification") }
    }

    /** Buttons on the PC media notification (older Android versions use these instead of the session). */
    class MediaActionReceiver : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            when (intent.action) {
                ACTION_PREV -> command("prev")
                ACTION_NEXT -> command("next")
                ACTION_TOGGLE -> command(if (peer.value.pick(pick.value)?.playing == true) "pause" else "play")
            }
        }
    }
}
