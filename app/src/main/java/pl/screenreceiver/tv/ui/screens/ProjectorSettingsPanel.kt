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
import pl.screenreceiver.tv.receiver.ReceiverService

@Composable
fun ProjectorSettingsPanel(
    serviceEnabled: Boolean,
    timerAllowed: Boolean,
    onOpenTimerSettings: () -> Unit,
    notificationsAllowed: Boolean,
    onRequestNotifications: () -> Unit,
) {
    val context = LocalContext.current
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
        TvInfoCard("Sleep timer access", detail = if (timerAllowed) "Allowed. Timer state is available; actual projector shutdown still needs a verified external control endpoint."
        else "Permission is needed to schedule a sleep timer. Without a verified external control endpoint the app cannot power off the projector.")
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
    var message by remember { mutableStateOf("No built-in LED power control or projector power-off path has been verified. Software screen dimming works independently.") }
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
                    if (address.isBlank()) "External endpoint cleared. No hardware power control is configured."
                    else "Projector address saved; connection and hardware control have not been verified."
                }.getOrElse { it.message ?: "Invalid projector address" }
            }, Modifier.fillMaxWidth())
        }
    }
}
