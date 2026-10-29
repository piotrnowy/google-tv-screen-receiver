package pl.screenreceiver.tv.overlay

import org.junit.Assert.*
import org.junit.Test

class ProjectorControlStateTest {
    @Test fun openCloseAndToggleDoNotLoseSelectedValues() {
        val selected = ProjectorControlState(screenBrightnessIndex = 2, sleepIndex = 3).reduce(OverlayEvent.Toggle)
        assertTrue(selected.isOpen)
        val closed = selected.reduce(OverlayEvent.Close)
        assertFalse(closed.isOpen)
        assertEquals(60, closed.selectedScreenBrightness)
        assertEquals(45, closed.selectedSleep)
        assertTrue(closed.reduce(OverlayEvent.Toggle).isOpen)
    }

    @Test fun changingRequestedBrightnessDoesNotChangeAppliedBrightness() {
        val state = ProjectorControlState().reduce(OverlayEvent.Change(-1))
        assertEquals(80, state.selectedScreenBrightness)
        assertEquals(100, state.appliedScreenBrightness)
        assertEquals(80, state.reduce(OverlayEvent.ScreenBrightnessApplied(80)).appliedScreenBrightness)
    }

    @Test fun navigationStaysInBoundsAndStreamingDoesNotModifyBrightness() {
        var state = ProjectorControlState().reduce(OverlayEvent.Move(-100))
        assertEquals(ControlRow.SCREEN_BRIGHTNESS, state.row)
        state = state.reduce(OverlayEvent.Change(-100))
        assertEquals(20, state.selectedScreenBrightness)
        state = state.reduce(OverlayEvent.Focus(ControlRow.SLEEP)).reduce(OverlayEvent.Change(100))
        assertEquals(120, state.selectedSleep)
        state = state.reduce(OverlayEvent.Focus(ControlRow.STREAMING))
        assertEquals(state, state.reduce(OverlayEvent.Change(1)))
        assertEquals(ControlRow.VOLUME, state.reduce(OverlayEvent.Move(100)).row)
    }

    @Test fun appliedScreenDimmingSurvivesClosingAndReopening() {
        val selected = ProjectorControlState().reduce(OverlayEvent.Change(-3))
        assertEquals(40, selected.selectedScreenBrightness)
        assertEquals(100, selected.appliedScreenBrightness)
        val applied = selected.reduce(OverlayEvent.ScreenBrightnessApplied(40))
        assertEquals(40, applied.appliedScreenBrightness)
        val reopened = applied.reduce(OverlayEvent.Close).reduce(OverlayEvent.Toggle)
        assertEquals(40, reopened.appliedScreenBrightness)
    }
}
