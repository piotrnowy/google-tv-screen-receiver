package pl.screenreceiver.tv.overlay

import pl.screenreceiver.tv.timer.SleepTimerPlan

enum class ControlRow { SCREEN_BRIGHTNESS, SLEEP, STREAMING, VOLUME }

data class ProjectorControlState(
    val isOpen: Boolean = false,
    val row: ControlRow = ControlRow.SCREEN_BRIGHTNESS,
    val screenBrightnessIndex: Int = 4,
    val appliedScreenBrightness: Int = 100,
    val sleepIndex: Int = 0,
) {
    val selectedScreenBrightness get() = ScreenBrightnessPolicy.levels[screenBrightnessIndex]
    val selectedSleep get() = sleepOptions[sleepIndex]

    fun reduce(event: OverlayEvent): ProjectorControlState = when (event) {
        OverlayEvent.Toggle -> copy(isOpen = !isOpen)
        OverlayEvent.Close -> copy(isOpen = false)
        is OverlayEvent.Focus -> copy(row = event.row)
        is OverlayEvent.Move -> copy(row = ControlRow.entries[(row.ordinal + event.delta).coerceIn(0, ControlRow.entries.lastIndex)])
        is OverlayEvent.Change -> when (row) {
            ControlRow.SCREEN_BRIGHTNESS -> copy(screenBrightnessIndex =
                (screenBrightnessIndex + event.delta).coerceIn(0, ScreenBrightnessPolicy.levels.lastIndex))
            ControlRow.SLEEP -> copy(sleepIndex = (sleepIndex + event.delta).coerceIn(0, sleepOptions.lastIndex))
            else -> this
        }
        is OverlayEvent.ScreenBrightnessApplied -> copy(appliedScreenBrightness = event.percent)
    }

    companion object { val sleepOptions = SleepTimerPlan.options }
}

sealed interface OverlayEvent {
    data object Toggle : OverlayEvent
    data object Close : OverlayEvent
    data class Focus(val row: ControlRow) : OverlayEvent
    data class Move(val delta: Int) : OverlayEvent
    data class Change(val delta: Int) : OverlayEvent
    data class ScreenBrightnessApplied(val percent: Int) : OverlayEvent
}
