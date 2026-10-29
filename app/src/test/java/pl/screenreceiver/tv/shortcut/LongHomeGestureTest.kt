package pl.screenreceiver.tv.shortcut

import org.junit.Assert.*
import org.junit.Test

class LongHomeGestureTest {
    @Test fun shortHomePassesThroughAndCannotFireAfterRelease() {
        var opens = 0
        val gesture = LongHomeGesture { opens++; true }
        assertFalse(gesture.down(1000, 0, 1000))
        gesture.tick(1799)
        assertFalse(gesture.up(1000, 1799, false))
        gesture.tick(3000)
        assertEquals(0, opens)
    }

    @Test fun longHomeFiresAt800AndConsumesOnlySubsequentEventsOnce() {
        var opens = 0
        val gesture = LongHomeGesture { opens++; true }
        assertFalse(gesture.down(1000, 0, 1000))
        gesture.tick(1800)
        gesture.tick(2000)
        assertTrue(gesture.down(1000, 1, 2100))
        assertTrue(gesture.down(1000, 2, 2200))
        assertTrue(gesture.up(1000, 2300, false))
        assertEquals(1, opens)
    }

    @Test fun delayedTimerFallsBackToReleaseAndFailedOpenDoesNotSwallowHome() {
        var calls = 0
        val gesture = LongHomeGesture { calls++; false }
        gesture.down(10, 0, 10)
        assertFalse(gesture.up(10, 900, false))
        assertEquals(1, calls)
        gesture.tick(2000)
        assertEquals(1, calls)
    }

    @Test fun missingDownCanceledAndInterruptedPressesDoNotOpen() {
        var opens = 0
        val gesture = LongHomeGesture { opens++; true }
        assertFalse(gesture.up(0, 2000, false))
        gesture.down(1000, 0, 1000)
        assertFalse(gesture.up(1000, 1900, true))
        gesture.down(2000, 0, 2000)
        gesture.reset()
        gesture.tick(3000)
        assertEquals(0, opens)
    }

    @Test fun aNewPressCanTriggerAgainWithoutReplayingShortHome() {
        var opens = 0
        val gesture = LongHomeGesture { opens++; true }
        gesture.down(0, 0, 0)
        gesture.tick(800)
        assertTrue(gesture.up(0, 900, false))
        assertFalse(gesture.down(1000, 0, 1000))
        assertFalse(gesture.up(1000, 1100, false))
        gesture.down(2000, 0, 2000)
        assertTrue(gesture.up(2000, 2900, false))
        assertEquals(2, opens)
    }
}
