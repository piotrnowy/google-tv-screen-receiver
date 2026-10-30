package pl.screenreceiver.tv.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.tv.material3.ExperimentalTvMaterial3Api
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Text
import pl.screenreceiver.tv.R
import pl.screenreceiver.tv.model.ReceiverState
import pl.screenreceiver.tv.model.ReceiverStatus
import pl.screenreceiver.tv.shortcut.ShortcutDiagnosticState
import pl.screenreceiver.tv.ui.theme.ReceiverAccent
import pl.screenreceiver.tv.ui.theme.ReceiverBackground
import pl.screenreceiver.tv.ui.theme.ReceiverError
import pl.screenreceiver.tv.ui.theme.ReceiverSecondaryText
import pl.screenreceiver.tv.ui.theme.ReceiverSurface
import pl.screenreceiver.tv.ui.theme.ReceiverText
import pl.screenreceiver.tv.ui.theme.ScreenReceiverTheme

@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
fun ReceiverScreen(
    state: ReceiverState,
    modifier: Modifier = Modifier,
    shortcutDiagnostic: ShortcutDiagnosticState = ShortcutDiagnosticState(),
    shortcutServiceEnabled: Boolean = false,
    onOpenAccessibilitySettings: () -> Unit = {},
    shortcutRecoveryAvailable: Boolean = false,
    shortcutRecoveryEnabled: Boolean = false,
    onToggleShortcutRecovery: () -> Unit = {},
    timerAllowed: Boolean = false,
    onOpenTimerSettings: () -> Unit = {},
    notificationsAllowed: Boolean = false,
    onRequestNotifications: () -> Unit = {},
) {
    val statusText = when (state.receiverStatus) {
        ReceiverStatus.WAITING -> R.string.status_waiting
        ReceiverStatus.IPHONE_CONNECTED -> R.string.status_iphone_connected
        ReceiverStatus.NEGOTIATING -> R.string.status_negotiating
        ReceiverStatus.STREAMING -> R.string.status_streaming
        ReceiverStatus.ERROR -> R.string.status_error
    }
    val statusColor = if (state.receiverStatus == ReceiverStatus.ERROR) ReceiverError else ReceiverAccent

    Column(
        modifier = modifier
            .fillMaxSize()
            .background(ReceiverBackground)
            .padding(horizontal = 48.dp, vertical = 32.dp)
            .verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(24.dp),
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(
                text = stringResource(R.string.screen_title),
                style = MaterialTheme.typography.headlineLarge,
                color = ReceiverText,
            )
            Text(text = stringResource(R.string.publisher_name), style = MaterialTheme.typography.bodyLarge, color = ReceiverAccent)
            Text(text = state.deviceName, style = MaterialTheme.typography.bodyLarge, color = ReceiverSecondaryText)
        }

        Column(verticalArrangement = Arrangement.spacedBy(16.dp)) {
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text(text = stringResource(R.string.connect_iphone_label),
                    style = MaterialTheme.typography.labelLarge, color = ReceiverSecondaryText)
                Text(text = state.connectionAddress ?: stringResource(R.string.unavailable),
                    style = MaterialTheme.typography.headlineLarge, color = ReceiverText)
            }
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(
                    text = stringResource(R.string.status_label),
                    style = MaterialTheme.typography.labelLarge,
                    color = ReceiverSecondaryText,
                )
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(16.dp),
                ) {
                    Box(Modifier.size(12.dp).background(statusColor, CircleShape))
                    Text(
                        text = stringResource(statusText),
                        style = MaterialTheme.typography.headlineLarge,
                        color = statusColor,
                    )
                }
            }

            Row(
                modifier = Modifier.fillMaxWidth()
                    .background(ReceiverSurface, RoundedCornerShape(16.dp))
                    .padding(24.dp),
                horizontalArrangement = Arrangement.spacedBy(24.dp),
            ) {
                ReceiverDetail(
                    label = stringResource(R.string.signaling_port_label),
                    value = state.signalingPort.toString(),
                    modifier = Modifier.weight(1f),
                )
                ReceiverDetail(
                    label = stringResource(R.string.local_ip_label),
                    value = state.localIpAddress ?: stringResource(R.string.unavailable),
                    modifier = Modifier.weight(1f),
                )
                ReceiverDetail(
                    label = stringResource(R.string.webrtc_label),
                    value = stringResource(when (state.receiverStatus) {
                        ReceiverStatus.WAITING, ReceiverStatus.IPHONE_CONNECTED -> R.string.webrtc_waiting
                        ReceiverStatus.NEGOTIATING -> R.string.webrtc_negotiating
                        ReceiverStatus.STREAMING -> R.string.webrtc_streaming
                        ReceiverStatus.ERROR -> R.string.status_error
                    }),
                    modifier = Modifier.weight(1f),
                )
            }

            state.connectedDeviceName?.let { name ->
                Text(text = name, style = MaterialTheme.typography.bodyLarge, color = ReceiverText)
            }

            state.errorMessage?.let { message ->
                Text(
                    text = message,
                    style = MaterialTheme.typography.bodyLarge,
                    color = ReceiverError,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }

        ReceiverNameSettings(state.deviceName)

        RemoteShortcutDiagnostic(
            state = shortcutDiagnostic,
            serviceEnabled = shortcutServiceEnabled,
            onOpenAccessibilitySettings = onOpenAccessibilitySettings,
            recoveryAvailable = shortcutRecoveryAvailable,
            recoveryEnabled = shortcutRecoveryEnabled,
            onToggleRecovery = onToggleShortcutRecovery,
        )

        ProjectorSettingsPanel(shortcutServiceEnabled, timerAllowed, onOpenTimerSettings, notificationsAllowed, onRequestNotifications)

        Text(
            text = stringResource(R.string.same_wifi_hint),
            style = MaterialTheme.typography.bodyLarge,
            color = ReceiverSecondaryText,
        )
    }
}

@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
private fun ReceiverDetail(label: String, value: String, modifier: Modifier = Modifier) {
    Column(modifier = modifier, verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(text = label, style = MaterialTheme.typography.labelLarge, color = ReceiverSecondaryText)
        Text(
            text = value,
            style = MaterialTheme.typography.titleLarge,
            color = ReceiverText,
            maxLines = 2,
            overflow = TextOverflow.Ellipsis,
        )
    }
}

@Preview(device = "spec:width=960dp,height=540dp,dpi=160")
@Composable
private fun ReceiverScreenPreview() {
    ScreenReceiverTheme {
        ReceiverScreen(ReceiverState(deviceName = "Living Room TV", localIpAddress = "192.168.1.42"))
    }
}
