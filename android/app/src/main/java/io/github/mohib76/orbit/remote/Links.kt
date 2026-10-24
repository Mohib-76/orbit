package io.github.mohib76.orbit.remote

import android.app.Activity
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.widget.Toast
import androidx.core.content.pm.ShortcutInfoCompat
import androidx.core.content.pm.ShortcutManagerCompat
import androidx.core.graphics.drawable.IconCompat
import io.github.mohib76.orbit.Hub
import io.github.mohib76.orbit.MainActivity
import io.github.mohib76.orbit.R
import org.json.JSONObject

/**
 * Web links between the devices (`orbit.url`). A link from the PC arrives as a notification, since Android
 * doesn't let an app open the browser from the background; one shared here opens on the PC.
 */
object Links {
    const val MAX_LENGTH = 4096
    private const val CHANNEL = "links"
    private const val SHORTCUT_ID = "open_on_pc"
    const val SHARE_CATEGORY = "io.github.mohib76.orbit.category.OPEN_ON_PC"
    private val pattern = Regex("""https?://[^\s<>"']+""", RegexOption.IGNORE_CASE)

    /**
     * The URL when it's an absolute http(s) link with a host; null for anything else. Deliberately lenient about
     * the rest (`|`, `{`, `_` in host names…), as browsers are: java.net.URI would refuse links the PC accepts.
     */
    fun normalize(url: String?): String? {
        val u = url?.trim().orEmpty()
        if (u.isEmpty() || u.length > MAX_LENGTH || u.any { it.isWhitespace() || it.isISOControl() }) return null
        val rest = when {
            u.startsWith("https://", ignoreCase = true) -> u.substring(8)
            u.startsWith("http://", ignoreCase = true) -> u.substring(7)
            else -> return null
        }
        val host = rest.substringBefore('/').substringBefore('?').substringBefore('#').substringAfterLast('@').substringBefore(':')
        return if (host.isEmpty()) null else u
    }

    /** Sends at most one link a second, so another app firing the share target can't flood the PC. */
    @Volatile private var lastSent = 0L

    /** The first link in shared text such as "Article title https://example.com/a". */
    fun find(text: String?): String? {
        if (text.isNullOrEmpty()) return null
        return pattern.findAll(text).firstNotNullOfOrNull { normalize(it.value.trimEnd('.', ',', ')', ']', '!', '?', ';', ':')) }
    }

    /** "example.com/path" for display. */
    fun short(url: String): String =
        url.removePrefix("https://").removePrefix("http://").removePrefix("www.").trimEnd('/')

    /** Opens [url] on the PC. Returns false when it isn't connected. */
    fun send(url: String): Boolean {
        val l = Hub.link ?: return false
        val safe = normalize(url) ?: return false
        val now = android.os.SystemClock.elapsedRealtime()
        if (now - lastSent < 1000) return true
        lastSent = now
        l.send("orbit.url", JSONObject().put("url", safe))
        return true
    }

    /** What to say after sending: the PC either opened it or is asking first (Settings › Phone remote). */
    fun sentMessage(ctx: Context): String {
        val pc = Hub.ui.value.pcName
        return if (Remote.caps.value.linksAsk) ctx.getString(R.string.link_sent_ask, pc) else ctx.getString(R.string.link_opened_on, pc)
    }

    fun onUrl(b: JSONObject) {
        val url = normalize(b.optString("url")) ?: return
        val app = Hub.app
        val nm = app.getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(NotificationChannel(CHANNEL, app.getString(R.string.links_channel), NotificationManager.IMPORTANCE_HIGH).apply {
            description = app.getString(R.string.links_channel_desc)
            setShowBadge(false)
        })
        val id = url.hashCode()
        val view = Intent(Intent.ACTION_VIEW, Uri.parse(url)).addCategory(Intent.CATEGORY_BROWSABLE)
        val open = PendingIntent.getActivity(app, id, view, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val copy = PendingIntent.getBroadcast(app, id, Intent(app, LinkReceiver::class.java).setAction(LinkReceiver.COPY)
            .putExtra("url", url).putExtra("id", id), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val share = PendingIntent.getActivity(app, id,
            Intent.createChooser(Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, url), null),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val pc = Hub.ui.value.pcName
        nm.notify(id, Notification.Builder(app, CHANNEL)
            .setSmallIcon(R.drawable.ic_link)
            .setContentTitle(app.getString(R.string.link_from, pc))
            .setContentText(short(url))
            .setCategory(Notification.CATEGORY_MESSAGE)
            .setAutoCancel(true)
            .setContentIntent(open)
            .addAction(Notification.Action.Builder(null, app.getString(R.string.link_open), open).build())
            .addAction(Notification.Action.Builder(null, app.getString(R.string.link_copy), copy).build())
            .addAction(Notification.Action.Builder(null, app.getString(R.string.link_share), share).build())
            .build())
    }

    /** "Open on <PC>" at the top of the share sheet (a sharing shortcut). Updated when the PC is paired or renamed. */
    fun publishShareTarget(ctx: Context) {
        val pc = Hub.prefs.pc
        try {
            if (pc == null) { ShortcutManagerCompat.removeDynamicShortcuts(ctx, listOf(SHORTCUT_ID)); return }
            val label = ctx.getString(R.string.open_on, pc.name)
            ShortcutManagerCompat.pushDynamicShortcut(ctx, ShortcutInfoCompat.Builder(ctx, SHORTCUT_ID)
                .setShortLabel(label)
                .setLongLabel(label)
                .setIcon(IconCompat.createWithResource(ctx, R.drawable.ic_share_pc))
                .setIntent(Intent(ctx, MainActivity::class.java).setAction(Intent.ACTION_MAIN))
                .setCategories(setOf(SHARE_CATEGORY))
                .setLongLived(true)
                .build())
        } catch (e: Exception) {
            android.util.Log.w(Hub.TAG, "share target: ${e.message}")
        }
    }
}

/** The link notification's Copy button. */
class LinkReceiver : BroadcastReceiver() {
    companion object { const val COPY = "io.github.mohib76.orbit.COPY_LINK" }

    override fun onReceive(context: Context, intent: Intent) {
        val url = Links.normalize(intent.getStringExtra("url")) ?: return
        context.getSystemService(ClipboardManager::class.java).setPrimaryClip(ClipData.newPlainText("Link", url))
        context.getSystemService(NotificationManager::class.java).cancel(intent.getIntExtra("id", 0))
        Toast.makeText(context, R.string.link_copied, Toast.LENGTH_SHORT).show()
    }
}

/** Share › "Open on <PC>": sends the first link in the shared text to the PC's browser, then closes. */
class OpenOnPcActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val text = intent.getStringExtra(Intent.EXTRA_TEXT)
        val url = Links.find(text) ?: Links.find(intent.dataString)
        val msg = when {
            Hub.prefs.pc == null -> getString(R.string.pair_first)
            url == null -> getString(R.string.link_none)
            Links.send(url) -> Links.sentMessage(this)
            else -> getString(R.string.pc_not_connected)
        }
        Toast.makeText(this, msg, Toast.LENGTH_SHORT).show()
        finish()
    }
}
