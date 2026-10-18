package io.github.mohib76.orbit

import android.content.ContentValues
import android.net.Uri
import android.os.Environment
import android.provider.MediaStore
import android.util.Log
import android.webkit.MimeTypeMap
import org.json.JSONObject
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.io.InputStream
import java.io.OutputStream
import javax.net.ssl.SSLSocket

/**
 * File transfer, phone side. Offer/accept/done/cancel travel on the control link; the bytes travel on a
 * separate TLS connection the PC opens ("orbit.data"): "push" = PC → phone, "pull" = phone → PC.
 * "accept" carries the offset to resume from, so an interrupted transfer continues where it stopped.
 */
object Transfers {
    private val partialDir by lazy { File(Hub.app.filesDir, "partial").apply { mkdirs() } }
    private fun partFile(id: String) = File(partialDir, "$id.part")

    // ------------------------------------------------ PC → phone

    fun onOffer(b: JSONObject) {
        val id = b.getString("id")
        val name = safeName(b.optString("name", "file"))
        val size = b.optLong("size", 0)
        val existing = Hub.transfers.value.firstOrNull { it.id == id }
        if (existing != null && !existing.active) return
        if (existing == null) Hub.transfers.value = listOf(TransferItem(id, name, size, outgoing = false)) + Hub.transfers.value
        if (Hub.prefs.autoAcceptFiles || existing != null) accept(id)
        else {
            Hub.updateTransfer(id) { it.copy(state = TState.Waiting) }
            OrbitService.instance?.notifyOffer(id, name, size)
        }
    }

    fun accept(id: String) {
        val offset = partFile(id).let { if (it.exists()) it.length() else 0L }
        Hub.updateTransfer(id) { it.copy(state = TState.Queued, done = offset) }
        Hub.link?.send("orbit.file.accept", JSONObject().put("id", id).put("offset", offset))
    }

    fun decline(id: String) {
        Hub.updateTransfer(id) { it.copy(state = TState.Cancelled) }
        Hub.link?.send("orbit.file.cancel", JSONObject().put("id", id))
        partFile(id).delete()
    }

    // ------------------------------------------------ phone → PC: accept comes from the PC

    fun onAccept(b: JSONObject) {
        val id = b.getString("id")
        Hub.updateTransfer(id) { it.copy(state = TState.Queued, done = b.optLong("offset", 0)) }
    }

    fun onDone(b: JSONObject) {
        val id = b.getString("id")
        Hub.updateTransfer(id) { it.copy(state = TState.Done, done = it.size) }
        synchronized(Hub.sources) { Hub.sources.remove(id)?.close() }
        OrbitService.instance?.refreshNotification()
    }

    fun onCancel(b: JSONObject) {
        val id = b.getString("id")
        Hub.updateTransfer(id) { it.copy(state = TState.Cancelled) }
        synchronized(Hub.sources) { Hub.sources.remove(id)?.close() }
        partFile(id).delete()
    }

    fun cancel(id: String) {
        Hub.link?.send("orbit.file.cancel", JSONObject().put("id", id))
        onCancel(JSONObject().put("id", id))
    }

    fun retry(t: TransferItem) {
        if (t.outgoing) { Hub.updateTransfer(t.id) { it.copy(state = TState.Queued) }; Hub.offerFile(t) }
        else accept(t.id)
    }

    // ------------------------------------------------ the byte channel

    fun handleData(s: SSLSocket, input: InputStream, b: JSONObject) {
        val id = b.getString("id")
        val offset = b.optLong("offset", 0)
        s.soTimeout = 30_000
        val t = Hub.transfers.value.firstOrNull { it.id == id } ?: return s.close()
        try {
            when (b.getString("dir")) {
                "push" -> receive(t, input, offset)
                "pull" -> send(t, s.outputStream, offset)
            }
        } catch (e: Exception) {
            Log.w(Hub.TAG, "transfer ${t.name}: ${e.message}")
            val now = Hub.transfers.value.firstOrNull { it.id == id }
            if (now != null && now.state != TState.Cancelled && now.state != TState.Done)
                Hub.updateTransfer(id) { it.copy(state = if (Hub.link == null) TState.Paused else TState.Failed) }
        } finally {
            try { s.close() } catch (_: Exception) { }
            OrbitService.instance?.refreshNotification()
        }
    }

    private fun receive(t: TransferItem, input: InputStream, offset: Long) {
        val part = partFile(t.id)
        Hub.updateTransfer(t.id) { it.copy(state = TState.Running, done = offset) }
        FileOutputStream(part, offset > 0).use { out ->
            if (offset == 0L) out.channel.truncate(0)
            pump(t, input, out, offset)
        }
        val uri = publish(part, t.name)
        part.delete()
        Hub.updateTransfer(t.id) { it.copy(state = TState.Done, done = t.size, savedUri = uri) }
        Hub.link?.send("orbit.file.done", JSONObject().put("id", t.id))
        OrbitService.instance?.notifyReceived(t.name, uri)
    }

    private fun send(t: TransferItem, out: OutputStream, offset: Long) {
        val pfd = synchronized(Hub.sources) { Hub.sources[t.id] } ?: throw IllegalStateException("source gone")
        Hub.updateTransfer(t.id) { it.copy(state = TState.Running, done = offset) }
        val fin = FileInputStream(pfd.fileDescriptor)
        fin.channel.position(offset)
        pump(t, fin, out, offset)
        out.flush()
        // Done is confirmed by the PC's "orbit.file.done".
    }

    private fun pump(t: TransferItem, input: InputStream, out: OutputStream, offset: Long) {
        val buf = ByteArray(256 * 1024)
        var done = offset
        var lastT = System.nanoTime()
        var lastB = offset
        while (done < t.size) {
            if (Hub.transfers.value.firstOrNull { it.id == t.id }?.state == TState.Cancelled) throw IllegalStateException("cancelled")
            val n = input.read(buf, 0, minOf(buf.size.toLong(), t.size - done).toInt())
            if (n < 0) throw IllegalStateException("closed early")
            out.write(buf, 0, n)
            done += n
            val now = System.nanoTime()
            if (now - lastT > 400_000_000L || done == t.size) {
                val speed = (done - lastB) / ((now - lastT) / 1e9)
                lastT = now; lastB = done
                val d = done
                Hub.updateTransfer(t.id) { it.copy(done = d, speed = speed) }
                OrbitService.instance?.progress(t.name, d, t.size, t.outgoing)
            }
        }
    }

    /** Move a finished file into Download/Orbit so it shows up in Files and the gallery. */
    private fun publish(part: File, name: String): Uri? {
        val cr = Hub.app.contentResolver
        val ext = name.substringAfterLast('.', "").lowercase()
        val mime = MimeTypeMap.getSingleton().getMimeTypeFromExtension(ext) ?: "application/octet-stream"
        val values = ContentValues().apply {
            put(MediaStore.Downloads.DISPLAY_NAME, name)
            put(MediaStore.Downloads.MIME_TYPE, mime)
            put(MediaStore.Downloads.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/Orbit")
            put(MediaStore.Downloads.IS_PENDING, 1)
        }
        val uri = cr.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values) ?: return null
        cr.openOutputStream(uri)?.use { out -> part.inputStream().use { it.copyTo(out, 256 * 1024) } }
        values.clear()
        values.put(MediaStore.Downloads.IS_PENDING, 0)
        cr.update(uri, values, null, null)
        return uri
    }

    private fun safeName(n: String): String {
        val base = n.replace('\\', '/').substringAfterLast('/').replace(Regex("[\\u0000-\\u001f:*?\"<>|]"), "_").trim()
        return if (base.isEmpty() || base == "." || base == "..") "file" else base
    }
}
