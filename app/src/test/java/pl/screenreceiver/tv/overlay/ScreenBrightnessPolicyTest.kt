package pl.screenreceiver.tv.overlay

import org.junit.Assert.*
import org.junit.Test

class ScreenBrightnessPolicyTest {
    @Test fun fullBrightnessIsTransparentAndLowestLevelCannotBlackOutTheScreen() {
        assertEquals(0f, ScreenBrightnessPolicy.opacity(100), 0f)
        assertEquals(0.8f, ScreenBrightnessPolicy.opacity(20), 0.0001f)
        assertTrue(ScreenBrightnessPolicy.levels.all { ScreenBrightnessPolicy.opacity(it) in 0f..0.8f })
    }

    @Test fun corruptSavedValuesRestoreToNormalBrightness() {
        for (invalid in listOf(-1, 0, 33, 101, Int.MAX_VALUE)) {
            assertEquals(100, ScreenBrightnessPolicy.restoredLevel(invalid))
        }
        assertEquals(40, ScreenBrightnessPolicy.restoredLevel(40))
    }

    @Test(expected = IllegalArgumentException::class)
    fun applyingBlackoutIsRejected() { ScreenBrightnessPolicy.opacity(0) }
}
