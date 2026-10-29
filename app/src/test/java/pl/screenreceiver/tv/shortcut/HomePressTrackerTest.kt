package pl.screenreceiver.tv.shortcut

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class HomePressTrackerTest {
    @Test
    fun distinguishesShortPressAndExactLongPressBoundary() {
        val tracker = HomePressTracker()
        tracker.onDown(1000, 0)
        val shortDuration = tracker.onUp(1000, 1799)!!
        assertEquals(799L, shortDuration)
        assertFalse(shortDuration >= HomePressTracker.LONG_PRESS_THRESHOLD_MS)

        tracker.onDown(2000, 0)
        val longDuration = tracker.onUp(2000, 2800)!!
        assertEquals(800L, longDuration)
        assertTrue(longDuration >= HomePressTracker.LONG_PRESS_THRESHOLD_MS)
    }

    @Test
    fun repeatsPreserveOriginalPressAndReleaseIsReportedOnlyOnce() {
        val tracker = HomePressTracker()
        tracker.onDown(1000, 0)
        tracker.onDown(1000, 1)
        tracker.onDown(1000, 2)
        assertEquals(1234L, tracker.onUp(1000, 2234))
        assertNull(tracker.onUp(1000, 2234))
    }

    @Test
    fun missingOrMismatchedDownDoesNotProduceFalseLongPress() {
        val tracker = HomePressTracker()
        assertNull(tracker.onUp(1000, 3000))
        tracker.onDown(1000, 1)
        assertNull(tracker.onUp(1000, 3000))
        tracker.onDown(1000, 0)
        assertNull(tracker.onUp(2000, 3000))
    }

    @Test
    fun interruptedOrOutOfOrderPressIsDiscarded() {
        val tracker = HomePressTracker()
        tracker.onDown(4000, 0)
        tracker.reset()
        assertNull(tracker.onUp(4000, 6000))
        tracker.onDown(7000, 0)
        assertNull(tracker.onUp(7000, 6999))
    }
}
