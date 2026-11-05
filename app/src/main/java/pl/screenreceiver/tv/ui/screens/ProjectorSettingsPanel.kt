package pl.screenreceiver.tv.ui.screens

import android.content.Intent
import android.graphics.Color
import android.text.InputType
import android.widget.EditText
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.widget.doOnTextChanged
import pl.screenreceiver.tv.overlay.ProjectorOverlayBridge
import pl.screenreceiver.tv.projector.ProjectorControllers
import pl.screenreceiver.tv.projector.LocalProjectorController
import pl.screenreceiver.tv.receiver.ReceiverService
import pl.screenreceiver.tv.timer.SleepTimerManager
import kotlinx.coroutines.launch

@Composable
fun ProjectorSettingsPanel(
    serviceEnabled: Boolean,
    timerAllowed: Boolean,
    onOpenTimerSettings: () -> Unit,
    notificationsAllowed: Boolean,
    onRequestNotifications: () -> Unit,
) {
    val context = LocalContext.current
    val power = remember(context) { LocalProjectorController(context) }
    val scope = rememberCoroutineScope()
    var powerReady by remember { mutableStateOf(power.authorized) }
    var authorizing by remember { mutableStateOf(false) }
    var message by remember { mutableStateOf<String?>(null) }
    var confirmStop by remember { mutableStateOf(false) }
    val cancelFocus = remember { FocusRequester() }
    val stopFocus = remember { FocusRequester() }
    var restoreStopFocus by remember { mutableStateOf(false) }
    LaunchedEffect(confirmStop) {
        if (confirmStop) { withFrameNanos { }; cancelFocus.requestFocus() }
        else if (restoreStopFocus) { withFrameNanos { }; stopFocus.requestFocus(); restoreStopFocus = false }
    }
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        TvInfoCard("Projector Control", detail = "Hold HOME for about one second for screen dimming, sleep timer and Google TV volume. BACK closes the overlay.")
        TvAction("Open Projector Control", {
            if (!ProjectorOverlayBridge.openOrClose()) message = "Enable Projector Control in Accessibility settings first."
        }, Modifier.fillMaxWidth(), enabled = serviceEnabled, tag = "open-projector-control")
        TvInfoCard("Sleep timer access", detail = if (timerAllowed) "Allowed. Authorize automatic power-off below, then the timer sends HDMI-CEC standby directly to the projector. Keep HDMI-CEC power control enabled on both devices."
        else "Allow Alarms & reminders to schedule projector shutdown.")
        TvInfoCard("Automatic projector power-off", detail = if (powerReady) "Authorized on this Google TV. No computer is needed when the timer expires."
        else "Enable network debugging in Google TV Developer options. This app uses a local debugging connection to send projector standby. Authorizing it grants debugging access to this Google TV; approve its key when prompted.")
        TvAction(if (authorizing) "Waiting for authorization..." else if (powerReady) "Check power-off connection" else "Authorize automatic power-off", {
            authorizing = true
            message = "Approve the debugging key on this TV. Select Always allow to keep timer shutdown working after restarts."
            scope.launch {
                try {
                    val result = power.authorize()
                    powerReady = power.authorized
                    message = result.message
                } catch (error: Exception) {
                    message = "Power-control setup failed: ${error.message}"
                } finally { authorizing = false }
            }
        }, Modifier.fillMaxWidth(), enabled = !authorizing, tag = "authorize-projector-power")
        if (powerReady) TvAction("Disable automatic power-off", {
            scope.launch {
                SleepTimerManager(context).set(0)
                power.forget()
                powerReady = false
                message = "Automatic power-off disabled and sleep timer cancelled."
            }
        }, Modifier.fillMaxWidth(), enabled = !authorizing, tag = "disable-projector-power")
        ProjectorControllers.powerOffUnavailableReason(context)?.let { TvInfoCard("Timer shutdown unavailable", detail = it) }
        if (!timerAllowed) TvAction("Allow sleep timer", onOpenTimerSettings, Modifier.fillMaxWidth(), tag = "allow-sleep-timer")
        TvInfoCard("Streaming notifications", detail = if (notificationsAllowed) "Allowed. The receiver can show its background service notification."
        else "Allow notifications to see when the receiver is running in the background.")
        if (!notificationsAllowed) TvAction("Allow streaming notifications", onRequestNotifications, Modifier.fillMaxWidth(), tag = "allow-notifications")
        if (!confirmStop) {
            TvAction("Stop receiver...", { confirmStop = true }, Modifier.fillMaxWidth().focusRequester(stopFocus), tag = "stop-receiver")
        } else {
            TvInfoCard("Stop the background receiver?", detail = "New senders will not be able to connect until the receiver is started again.")
            TvAction("Keep receiver running", { restoreStopFocus = true; confirmStop = false }, Modifier.fillMaxWidth().focusRequester(cancelFocus), selected = true, tag = "cancel-stop")
            TvAction("Stop receiver now", {
                context.stopService(Intent(context, ReceiverService::class.java))
                restoreStopFocus = true
                confirmStop = false
                message = "Receiver stop requested. Reopen the app to start it again."
            }, Modifier.fillMaxWidth(), tag = "confirm-stop")
        }
        message?.let { TvInfoCard("Receiver information", detail = it) }
    }
}

/** Retains the existing optional endpoint editor; it does not send a hardware command. */
@Composable
internal fun ProjectorAddressSettings() {
    val context = LocalContext.current
    var expanded by remember { mutableStateOf(false) }
    var address by remember { mutableStateOf(ProjectorControllers.address(context)) }
    var message by remember { mutableStateOf("The timer normally uses authorized local HDMI-CEC power control. Configure an external bridge only if you have a compatible, verified ViewSonic control endpoint. Screen dimming works independently.") }
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        TvInfoCard("Optional projector bridge", detail = message)
        TvAction(if (expanded) "Hide projector address" else "Configure external projector bridge", { expanded = !expanded }, Modifier.fillMaxWidth(), tag = "projector-address-toggle")
        if (expanded) {
            AndroidView(
                modifier = Modifier.fillMaxWidth(),
                factory = { viewContext ->
                    EditText(viewContext).apply {
                        hint = "Projector host or host:port"
                        setText(address)
                        textSize = 20f
                        setTextColor(Color.WHITE)
                        setHintTextColor(Color.LTGRAY)
                        inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI
                        isSingleLine = true
                        doOnTextChanged { text, _, _, _ -> address = text?.toString().orEmpty() }
                    }
                },
            )
            TvAction("Save projector address", {
                message = runCatching {
                    ProjectorControllers.saveAddress(context, address)
                    if (address.isBlank()) "External endpoint cleared. Authorize automatic power-off in Projector Control settings to use HDMI-CEC."
                    else "Projector address saved; connection and hardware control have not been verified."
                }.getOrElse { it.message ?: "Invalid projector address" }
            }, Modifier.fillMaxWidth())
        }
    }
}
