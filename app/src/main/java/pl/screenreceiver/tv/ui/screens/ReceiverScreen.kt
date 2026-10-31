package pl.screenreceiver.tv.ui.screens

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.tv.material3.ExperimentalTvMaterial3Api
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Text
import pl.screenreceiver.tv.R
import pl.screenreceiver.tv.model.ReceiverState
import pl.screenreceiver.tv.model.ReceiverStatus
import pl.screenreceiver.tv.shortcut.ShortcutDiagnosticState
import pl.screenreceiver.tv.ui.theme.*

private enum class ReceiverPage(val label: String) {
    Receiver("Receiver"), Settings("Settings"), Technical("Technical details")
}

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
    var page by rememberSaveable { mutableStateOf(ReceiverPage.Receiver) }
    val receiverList = rememberLazyListState()
    val settingsList = rememberLazyListState()
    val technicalList = rememberLazyListState()
    val list = when (page) {
        ReceiverPage.Receiver -> receiverList
        ReceiverPage.Settings -> settingsList
        ReceiverPage.Technical -> technicalList
    }
    val firstFocus = remember { FocusRequester() }
    var needsInitialFocus by remember(page) { mutableStateOf(true) }
    val initialFocusModifier = Modifier.focusRequester(firstFocus).onGloballyPositioned {
        if (needsInitialFocus) {
            needsInitialFocus = false
            firstFocus.requestFocus()
        }
    }
    val scrollHint by remember(list) {
        derivedStateOf {
            when {
                list.canScrollBackward && list.canScrollForward -> "More above and below"
                list.canScrollForward -> "More below"
                list.canScrollBackward -> "More above"
                else -> "All information visible"
            }
        }
    }
    BackHandler(enabled = page != ReceiverPage.Receiver) { page = ReceiverPage.Receiver }
    LaunchedEffect(page) {
        list.scrollToItem(0)
    }

    Column(
        modifier.fillMaxSize().background(ReceiverBackground).padding(horizontal = 32.dp, vertical = 20.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            Image(painterResource(R.mipmap.ic_launcher_pk), "P&K Applications logo", Modifier.size(44.dp))
            Column(Modifier.weight(1f)) {
                Text(stringResource(R.string.app_name), style = MaterialTheme.typography.headlineLarge, color = ReceiverText)
                Text(stringResource(R.string.publisher_name), style = MaterialTheme.typography.labelMedium, color = ReceiverSecondaryText)
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            ReceiverPage.entries.forEach { destination ->
                TvAction(
                    destination.label,
                    onClick = { page = destination },
                    selected = page == destination,
                    tag = "tab-" + destination.name,
                )
            }
        }

        // This bounded viewport stays separate from the header and navigation hints.
        // Each information card can receive D-pad focus; Foundation scrolls focused items into view.
        LazyColumn(
            modifier = Modifier.weight(1f).fillMaxWidth().testTag("page-content"),
            state = list,
            contentPadding = PaddingValues(4.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            when (page) {
                ReceiverPage.Receiver -> {
                    item("receiver-status") {
                        val status = when (state.receiverStatus) {
                            ReceiverStatus.WAITING -> if (state.localIpAddress == null) "Waiting for a network" else "Ready to connect"
                            ReceiverStatus.IPHONE_CONNECTED -> "Sender connected"
                            ReceiverStatus.NEGOTIATING -> "Connecting..."
                            ReceiverStatus.STREAMING -> "Screen sharing active"
                            ReceiverStatus.ERROR -> "Connection needs attention"
                        }
                        val statusColor = when (state.receiverStatus) {
                            ReceiverStatus.ERROR -> ReceiverError
                            ReceiverStatus.STREAMING -> ReceiverSuccess
                            ReceiverStatus.IPHONE_CONNECTED, ReceiverStatus.NEGOTIATING -> ReceiverInfo
                            ReceiverStatus.WAITING -> ReceiverAccent
                        }
                        TvInfoCard(
                            status,
                            initialFocusModifier.testTag("receiver-status"),
                            titleColor = statusColor,
                        ) {
                            Text(state.deviceName, style = MaterialTheme.typography.titleLarge, color = ReceiverText)
                            Text(
                                if (state.localIpAddress == null) "Connect this TV and your computer to the same local network."
                                else "Choose this name in Projector Connect on Windows.",
                                style = MaterialTheme.typography.bodyLarge, color = ReceiverSecondaryText,
                            )
                            Text(
                                "Manual address: " + (state.connectionAddress ?: "Not available yet"),
                                style = MaterialTheme.typography.bodyLarge, color = ReceiverText,
                            )
                            state.connectedDeviceName?.let {
                                Text("Connected sender: " + it, style = MaterialTheme.typography.bodyLarge, color = ReceiverText)
                            }
                        }
                    }
                    if (state.receiverStatus == ReceiverStatus.ERROR) {
                        item("friendly-error") {
                            TvInfoCard("Sharing could not continue", detail = "Check the sender and network, then try connecting again. The full error is available in Technical details.", titleColor = ReceiverError)
                        }
                        item("error-details-action") {
                            TvAction("View connection details", { page = ReceiverPage.Technical }, Modifier.fillMaxWidth())
                        }
                    }
                    item("how-to-share") {
                        TvInfoCard("Share from your computer", detail = "1. Open Projector Connect on Windows.\n2. Select this receiver and choose Share screen.\n3. Use Stop sharing on Windows when you are finished.")
                    }
                    item("receiver-settings-action") {
                        TvAction("Receiver and remote settings", { page = ReceiverPage.Settings }, Modifier.fillMaxWidth())
                    }
                    item("receiver-end") {
                        TvInfoCard("Your remote", Modifier.testTag("receiver-end"), detail = "Use UP / DOWN to move and scroll. Hold HOME for about one second to open Projector Control. The receiver can continue listening in the background.")
                    }
                }
                ReceiverPage.Settings -> {
                    item("settings-start") {
                        TvInfoCard("Receiver settings", initialFocusModifier.testTag("settings-start"), detail = "Change the receiver name and review remote access and permissions. UP / DOWN scrolls to every setting.")
                    }
                    item("receiver-name") { ReceiverNameSettings(state.deviceName) }
                    item("shortcut-access") {
                        TvInfoCard("HOME shortcut", detail = if (shortcutServiceEnabled) "Projector Control is enabled. Hold HOME for about one second; short HOME opens the normal TV launcher."
                        else "Enable Projector Control in Accessibility settings to use the long-HOME shortcut.")
                    }
                    item("shortcut-settings") {
                        TvAction("Open accessibility settings", onOpenAccessibilitySettings, Modifier.fillMaxWidth(), tag = "accessibility-settings")
                    }
                    item("recovery-info") {
                        TvInfoCard("Startup recovery", detail = when {
                            shortcutRecoveryEnabled -> "On. Restores shortcut selection after restart. Turn this off before disabling Projector Control in Accessibility."
                            shortcutRecoveryAvailable -> "Off. You can enable recovery if the TV resets shortcut access after restart."
                            else -> "One-time computer setup is needed to enable recovery. HOME does not need calibration; the key history resets when the app restarts."
                        })
                    }
                    item("recovery-action") {
                        TvAction(
                            if (shortcutRecoveryEnabled) "Turn startup recovery off" else "Turn startup recovery on",
                            onToggleShortcutRecovery, Modifier.fillMaxWidth(), enabled = shortcutRecoveryAvailable, tag = "startup-recovery",
                        )
                    }
                    item("projector-settings") {
                        ProjectorSettingsPanel(shortcutServiceEnabled, timerAllowed, onOpenTimerSettings, notificationsAllowed, onRequestNotifications)
                    }
                    item("settings-end") {
                        TvInfoCard("End of settings", Modifier.testTag("settings-end"), detail = "Use UP to review earlier settings, or BACK to return to Receiver.")
                    }
                }
                ReceiverPage.Technical -> {
                    item("connection-details") {
                        TvInfoCard("Connection details", initialFocusModifier.testTag("technical-start")) {
                            Text("Receiver: " + state.deviceName, color = ReceiverText)
                            Text("Address: " + (state.connectionAddress ?: "Unavailable"), color = ReceiverText)
                            Text("State: " + state.receiverStatus.name, color = ReceiverSecondaryText)
                            Text("Video: H.264  |  Audio: Opus  |  Local network discovery", color = ReceiverSecondaryText)
                        }
                    }
                    // Split diagnostic text into readable focus targets rather than truncating it
                    // or creating one card taller than the viewport.
                    state.errorMessage?.chunked(240)?.forEachIndexed { index, part ->
                        item("error-" + index) {
                            TvInfoCard("Error details (" + (index + 1) + ")", Modifier.testTag("error-part-" + index), detail = part, titleColor = ReceiverError)
                        }
                    }
                    item("shortcut-diagnostics") {
                        RemoteShortcutDiagnostic(shortcutDiagnostic, shortcutServiceEnabled, shortcutRecoveryAvailable, shortcutRecoveryEnabled)
                    }
                    item("advanced-settings") { ProjectorAddressSettings() }
                    item("technical-end") {
                        TvInfoCard("End of technical details", Modifier.testTag("technical-end"), detail = "All connection and remote diagnostics are above. BACK returns to Receiver.")
                    }
                }
            }
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            Text("UP / DOWN move  |  OK select  |  BACK return", style = MaterialTheme.typography.labelMedium, color = ReceiverSecondaryText)
            Text(scrollHint, style = MaterialTheme.typography.labelMedium, color = ReceiverAccent, modifier = Modifier.testTag("scroll-hint"))
        }
    }
}

@Preview(device = "spec:width=960dp,height=540dp,dpi=160")
@Composable
private fun ReceiverScreenPreview() {
    ScreenReceiverTheme {
        ReceiverScreen(ReceiverState(deviceName = "Living Room TV", localIpAddress = "192.0.2.10"))
    }
}
