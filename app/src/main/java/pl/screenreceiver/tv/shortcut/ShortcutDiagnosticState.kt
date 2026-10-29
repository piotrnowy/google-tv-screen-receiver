package pl.screenreceiver.tv.shortcut

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update

data class ShortcutDiagnosticState(
    val lastKeyName: String? = null,
    val lastHomeDurationMs: Long? = null,
    val longHomeDetected: Boolean = false,
)

/** Process-local diagnostics survive Activity stops while the service observes other apps. */
internal object ShortcutDiagnostics {
    private val mutableState = MutableStateFlow(ShortcutDiagnosticState())
    val state = mutableState.asStateFlow()

    fun recordKey(name: String) {
        mutableState.update { it.copy(lastKeyName = name) }
    }

    fun recordHomeRelease(durationMs: Long) {
        mutableState.update {
            it.copy(
                lastHomeDurationMs = durationMs,
                longHomeDetected = durationMs >= HomePressTracker.LONG_PRESS_THRESHOLD_MS,
            )
        }
    }
}
