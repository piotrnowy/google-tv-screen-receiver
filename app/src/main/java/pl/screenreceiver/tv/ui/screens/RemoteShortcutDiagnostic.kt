package pl.screenreceiver.tv.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp
import pl.screenreceiver.tv.shortcut.ShortcutDiagnosticState

/** Read-only diagnostics; setup actions live on Settings rather than crowding the receiver screen. */
@Composable
fun RemoteShortcutDiagnostic(
    state: ShortcutDiagnosticState,
    serviceEnabled: Boolean,
    recoveryAvailable: Boolean = false,
    recoveryEnabled: Boolean = false,
) {
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        TvInfoCard("Remote service", detail = if (serviceEnabled) "Projector Control: enabled" else "Projector Control: disabled")
        TvInfoCard("Startup recovery", detail = when {
            recoveryEnabled -> "On"
            recoveryAvailable -> "Off"
            else -> "One-time computer setup required"
        })
        TvInfoCard("Last remote key", detail = state.lastKeyName ?: "No key received in this app session")
        TvInfoCard("Last HOME hold", detail = state.lastHomeDurationMs?.let { it.toString() + " ms" } ?: "No HOME release in this app session")
        TvInfoCard("Long HOME detected", detail = if (state.longHomeDetected) "Yes, in this app session" else "Not in this app session. This history resets; it is not a calibration requirement.")
    }
}
