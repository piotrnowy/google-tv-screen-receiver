package pl.screenreceiver.tv.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.tv.material3.Button
import androidx.tv.material3.ExperimentalTvMaterial3Api
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Text
import pl.screenreceiver.tv.R
import pl.screenreceiver.tv.shortcut.ShortcutDiagnosticState
import pl.screenreceiver.tv.ui.theme.ReceiverSecondaryText
import pl.screenreceiver.tv.ui.theme.ReceiverSurface
import pl.screenreceiver.tv.ui.theme.ReceiverText

@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
fun RemoteShortcutDiagnostic(
    state: ShortcutDiagnosticState,
    serviceEnabled: Boolean,
    onOpenAccessibilitySettings: () -> Unit,
    recoveryAvailable: Boolean = false,
    recoveryEnabled: Boolean = false,
    onToggleRecovery: () -> Unit = {},
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier.fillMaxWidth()
            .background(ReceiverSurface, RoundedCornerShape(16.dp))
            .padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = stringResource(R.string.shortcut_diagnostic_title),
                style = MaterialTheme.typography.titleMedium,
                color = ReceiverText,
                modifier = Modifier.weight(1f),
            )
            Button(onClick = onOpenAccessibilitySettings) {
                Text(stringResource(R.string.shortcut_open_settings))
            }
        }
        Text(
            text = stringResource(if (recoveryEnabled) R.string.shortcut_recovery_enabled_hint else R.string.shortcut_recovery_setup_hint),
            style = MaterialTheme.typography.bodyMedium,
            color = ReceiverSecondaryText,
        )
        Button(onClick = onToggleRecovery) {
            Text(stringResource(when {
                !recoveryAvailable -> R.string.shortcut_recovery_needs_setup
                recoveryEnabled -> R.string.shortcut_recovery_on
                else -> R.string.shortcut_recovery_off
            }))
        }
        Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            DiagnosticValue(
                stringResource(R.string.shortcut_service_status),
                stringResource(if (serviceEnabled) R.string.shortcut_enabled else R.string.shortcut_disabled),
                Modifier.weight(1f),
            )
            DiagnosticValue(
                stringResource(R.string.shortcut_last_key),
                state.lastKeyName ?: stringResource(R.string.shortcut_no_key),
                Modifier.weight(1f),
            )
            DiagnosticValue(
                stringResource(R.string.shortcut_last_duration),
                state.lastHomeDurationMs?.let { stringResource(R.string.shortcut_duration_ms, it) }
                    ?: stringResource(R.string.shortcut_no_duration),
                Modifier.weight(1f),
            )
            DiagnosticValue(
                stringResource(R.string.shortcut_long_home),
                stringResource(if (state.longHomeDetected) R.string.shortcut_yes else R.string.shortcut_no),
                Modifier.weight(1f),
            )
        }
    }
}

@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
private fun DiagnosticValue(label: String, value: String, modifier: Modifier) {
    Column(modifier, verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Text(label, style = MaterialTheme.typography.labelMedium, color = ReceiverSecondaryText)
        Text(value, style = MaterialTheme.typography.bodyMedium, color = ReceiverText)
    }
}
