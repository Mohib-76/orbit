package io.github.mohib76.orbit

import io.github.mohib76.orbit.remote.Links
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class LinksTest {
    @Test fun onlyWebLinksAreAccepted() {
        assertEquals("https://example.com/a?b=1", Links.normalize("https://example.com/a?b=1"))
        assertEquals("http://example.com", Links.normalize("  http://example.com "))
        assertNull(Links.normalize("file:///sdcard/secret.txt"))
        assertNull(Links.normalize("intent://scan/#Intent;scheme=zxing;end"))
        assertNull(Links.normalize("javascript:alert(1)"))
        assertNull(Links.normalize("example.com"))
        assertNull(Links.normalize("https:///path"))
        // Links browsers open even though java.net.URI refuses them.
        assertEquals("https://example.com/search?q=a|b", Links.normalize("https://example.com/search?q=a|b"))
        assertEquals("https://my_host.example.com/", Links.normalize("https://my_host.example.com/"))
        assertNull(Links.normalize("https://example.com/" + "a".repeat(Links.MAX_LENGTH)))
    }

    @Test fun findsTheLinkInSharedText() {
        assertEquals("https://longreads.example/local-first",
            Links.find("The quiet rise of local-first software https://longreads.example/local-first"))
        assertEquals("https://example.com/a", Links.find("(see https://example.com/a)."))
        assertNull(Links.find("no link here"))
    }

    @Test fun shortFormDropsSchemeAndWww() = assertEquals("example.com/path", Links.short("https://www.example.com/path/"))
}
