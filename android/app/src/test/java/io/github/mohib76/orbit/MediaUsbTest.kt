package io.github.mohib76.orbit

import io.github.mohib76.orbit.media.MediaSnapshot
import io.github.mohib76.orbit.media.Player
import io.github.mohib76.orbit.usb.usbModeOf
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class MediaTest {
    private fun song(playing: Boolean = true, pos: Long = 10_000, len: Long = 200_000) =
        Player("chrome.exe", "Chrome", "Lofi beats", "Lofi Girl", "", playing, pos, len, true, true, true, true, true, "abc123")

    @Test fun stateRoundTrips() {
        val snap = MediaSnapshot(listOf(song()), "chrome.exe", false, 0)
        val line = String(Proto.encode("orbit.media.state", snap.toJson())).trimEnd('\n')
        val back = MediaSnapshot.fromJson(Proto.decode(line).body, 0)
        assertEquals(song(), back.players.single())
        assertEquals("chrome.exe", back.active)
        assertFalse(back.controllable)
    }

    @Test fun readsWhatTheWindowsSideSends() {
        // Shape produced by MediaSnapshot.ToJson on Windows (null active, missing optional fields elsewhere).
        val b = JSONObject("""{"players":[{"id":"x","app":"VLC","playing":false,"pos":5,"len":0},{"app":"no id"}],"active":null,"controllable":true}""")
        val snap = MediaSnapshot.fromJson(b, 0)
        val p = snap.players.single()
        assertNull(snap.active)
        assertTrue(p.live)
        assertFalse(p.canSeek)
        assertEquals("", p.art)
    }

    @Test fun positionAdvancesOnlyWhilePlaying() {
        val playing = MediaSnapshot(listOf(song()), null, true, 1_000)
        assertEquals(13_000, playing.positionNow(playing.players[0], 4_000))
        assertEquals(200_000, playing.positionNow(playing.players[0], 10_000_000))
        val paused = MediaSnapshot(listOf(song(playing = false)), null, true, 1_000)
        assertEquals(10_000, paused.positionNow(paused.players[0], 4_000))
    }

    @Test fun pickPrefersUserThenActiveThenPlaying() {
        val a = song(playing = false).copy(id = "a")
        val b = song(playing = true).copy(id = "b")
        val c = song(playing = false).copy(id = "c")
        val snap = MediaSnapshot(listOf(a, b, c), "c", true, 0)
        assertEquals("a", snap.pick("a")?.id)
        assertEquals("c", snap.pick("gone")?.id)
        assertEquals("b", snap.copy(active = null).pick(null)?.id)
        assertNull(MediaSnapshot.EMPTY.pick(null))
    }

    @Test fun clock() {
        assertEquals("0:00", MediaSnapshot.clock(0))
        assertEquals("1:42", MediaSnapshot.clock(102_000))
        assertEquals("1:12:05", MediaSnapshot.clock(4_325_000))
    }
}

class UsbModeTest {
    private fun modeFor(vararg on: String) = usbModeOf { it in on }

    @Test fun readsUsbStateExtras() {
        assertEquals("tethering", modeFor("connected", "configured", "rndis", "adb"))
        assertEquals("tethering", modeFor("ncm"))
        assertEquals("files", modeFor("connected", "mtp"))
        assertEquals("other", modeFor("ptp"))
        assertEquals("charging", modeFor("connected", "adb"))
        assertEquals("charging", modeFor())
    }
}
