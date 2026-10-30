package pl.screenreceiver.tv.network

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ReceiverNamePolicyTest {
    @Test fun trimsNamesAndKeepsUnicode() {
        assertEquals("Living room 📺", ReceiverNamePolicy.validate("  Living room 📺  "))
        assertEquals("📺".repeat(45), ReceiverNamePolicy.validate("📺".repeat(45)))
    }

    @Test fun rejectsBlankOversizeAndControlCharacters() {
        for (value in listOf(" ", "a".repeat(49), "📺".repeat(46), "Living\nroom", "TV\u0000room")) {
            assertTrue(runCatching { ReceiverNamePolicy.validate(value) }.isFailure)
        }
    }
}
