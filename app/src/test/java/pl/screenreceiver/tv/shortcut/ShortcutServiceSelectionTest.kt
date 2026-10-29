package pl.screenreceiver.tv.shortcut

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ShortcutServiceSelectionTest {
    private val vendor = "com.sdt.projector.observer/com.sdt.hdmi.MyAccessibilityService"

    @Test fun restoresOurSelectionWithoutRemovingVendorOrOtherServices() {
        val others = "$vendor:other.package/.Accessibility"
        assertEquals("$others:${ShortcutServiceSelection.COMPONENT}", ShortcutServiceSelection.includeProjector(others))
    }

    @Test fun doesNotDuplicateFullOrAbbreviatedComponent() {
        for (ours in listOf(ShortcutServiceSelection.COMPONENT, "pl.screenreceiver.tv/.shortcut.ProjectorShortcutAccessibilityService")) {
            val existing = "$vendor:$ours"
            assertTrue(ShortcutServiceSelection.containsProjector(existing))
            assertEquals(existing, ShortcutServiceSelection.includeProjector(existing))
        }
    }

    @Test fun handlesUnsetAndEmptySettings() {
        for (unset in listOf(null, "", "null", ":")) {
            assertFalse(ShortcutServiceSelection.containsProjector(unset))
            assertEquals(ShortcutServiceSelection.COMPONENT, ShortcutServiceSelection.includeProjector(unset))
        }
    }

    @Test fun similarlyNamedServicesDoNotCountAsOurService() {
        val other = ShortcutServiceSelection.COMPONENT + "Other"
        assertFalse(ShortcutServiceSelection.containsProjector(other))
        assertEquals("$other:${ShortcutServiceSelection.COMPONENT}", ShortcutServiceSelection.includeProjector(other))
    }
}
