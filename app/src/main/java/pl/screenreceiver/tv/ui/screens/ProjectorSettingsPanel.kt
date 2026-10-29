package pl.screenreceiver.tv.ui.screens

import android.graphics.Color
import android.text.InputType
import android.widget.EditText
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.widget.doOnTextChanged
import androidx.tv.material3.Button
import androidx.tv.material3.ExperimentalTvMaterial3Api
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Text
import pl.screenreceiver.tv.overlay.ProjectorOverlayBridge
import pl.screenreceiver.tv.projector.ProjectorControllers
import pl.screenreceiver.tv.receiver.ReceiverService
import pl.screenreceiver.tv.ui.theme.ReceiverText

@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
fun ProjectorSettingsPanel(
    serviceEnabled: Boolean,
    timerAllowed: Boolean,
    onOpenTimerSettings: () -> Unit,
    notificationsAllowed: Boolean,
    onRequestNotifications: () -> Unit,
) {
    val context = LocalContext.current
    var address by remember { mutableStateOf(ProjectorControllers.address(context)) }
    var message by remember { mutableStateOf("Hold HOME for 800 ms to open Projector Control") }
    Column(Modifier.fillMaxWidth().padding(vertical = 8.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Projector Control", style = MaterialTheme.typography.titleLarge, color = ReceiverText)
        Text("Overlay access: ${if (serviceEnabled) "ENABLED" else "DISABLED"} · uses the accessibility service", color = ReceiverText)
        Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            Button(onClick = {
                if (!ProjectorOverlayBridge.openOrClose()) message = "Enable Projector Control in Accessibility settings first"
            }) { Text("OPEN PROJECTOR CONTROL") }
            if (!timerAllowed) {
                Button(onClick = onOpenTimerSettings) { Text("ALLOW SLEEP TIMER") }
            }
        }
        Text("Sleep timer access: ${if (timerAllowed) "ENABLED" else "DISABLED"}", color = ReceiverText)
        Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            if (!notificationsAllowed) {
                Button(onClick = onRequestNotifications) { Text("ENABLE STREAMING NOTIFICATIONS") }
            }
            Button(onClick = { context.stopService(android.content.Intent(context, ReceiverService::class.java)) }) {
                Text("STOP RECEIVER")
            }
        }
        AndroidView(
            modifier = Modifier.fillMaxWidth(),
            factory = { viewContext ->
                EditText(viewContext).apply {
                    hint = "Projector address (optional, host:port)"
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
        Button(onClick = {
            message = runCatching {
                ProjectorControllers.saveAddress(context, address)
                if (address.isBlank()) "Projector disconnected; requests will be logged" else "Projector address saved; connection not yet verified"
            }.getOrElse { it.message ?: "Invalid projector address" }
        }) { Text("SAVE PROJECTOR ADDRESS") }
        Text(message, color = ReceiverText)
    }
}
