package pl.screenreceiver.tv.audio

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class MediaVolumeControllerTest {
    @Test
    fun mapsNormalizedVolumeIntoMediaStreamRange() {
        assertEquals(0, mediaVolumeIndex(0.0, 0, 15))
        assertEquals(8, mediaVolumeIndex(0.5, 0, 15))
        assertEquals(15, mediaVolumeIndex(1.0, 0, 15))
    }

    @Test
    fun respectsNonzeroMinimumAndFixedVolumeRanges() {
        assertEquals(6, mediaVolumeIndex(0.5, 2, 10))
        assertEquals(5, mediaVolumeIndex(0.5, 5, 5))
    }

    @Test
    fun rejectsInvalidVolumeAndRanges() {
        listOf(Double.NaN, Double.POSITIVE_INFINITY, -0.1, 1.1).forEach { value ->
            assertThrows(IllegalArgumentException::class.java) { mediaVolumeIndex(value, 0, 15) }
        }
        assertThrows(IllegalArgumentException::class.java) { mediaVolumeIndex(0.5, 10, 2) }
    }

    @Test
    fun remoteActionsAdjustOnlyMediaAndToggleMute() {
        val fake = FakeMedia()
        val controller = RemoteVolumeController(fake)
        assertEquals(RemoteVolumeController.Result(8, 15, false, true), controller.current())
        controller.apply("up")
        assertEquals(android.media.AudioManager.ADJUST_RAISE, fake.lastAdjustment)
        controller.apply("down")
        assertEquals(android.media.AudioManager.ADJUST_LOWER, fake.lastAdjustment)
        controller.apply("muteToggle")
        assertTrue(fake.currentlyMuted)
        controller.apply("muteToggle")
        assertFalse(fake.currentlyMuted)
    }

    @Test
    fun fixedVolumeReturnsUnavailableWithoutApplyingChanges() {
        val logs = mutableListOf<String>()
        val fake = FakeMedia().also { it.fixed = true }
        val state = RemoteVolumeController(fake, logs::add).apply("up")
        assertFalse(state.available)
        assertEquals(0, fake.lastAdjustment)
        assertTrue(logs.any { it.contains("fixed-volume") })
    }

    @Test
    fun unsupportedActionIsRejected() {
        assertThrows(IllegalArgumentException::class.java) { RemoteVolumeController(FakeMedia()).apply("reset") }
    }

    private class FakeMedia : MediaVolumeController {
        var current = 8
        var currentlyMuted = false
        var fixed = false
        var lastAdjustment = 0
        override fun state() = MediaVolumeController.State(0, 15, current, currentlyMuted, fixed)
        override fun setVolume(value: Double) { current = mediaVolumeIndex(value, 0, 15) }
        override fun setMuted(muted: Boolean) { this.currentlyMuted = muted }
        override fun adjust(direction: Int) {
            lastAdjustment = direction
            current = (current + direction).coerceIn(0, 15)
        }
    }
}
