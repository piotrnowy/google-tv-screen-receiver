package pl.screenreceiver.tv.ui.screens

import android.graphics.Color
import android.text.InputFilter
import android.widget.EditText
import android.view.inputmethod.InputMethodManager
import android.content.Context
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.widget.doOnTextChanged
import androidx.tv.material3.Button
import androidx.tv.material3.ExperimentalTvMaterial3Api
import androidx.tv.material3.Text
import pl.screenreceiver.tv.receiver.ReceiverRuntime
import pl.screenreceiver.tv.ui.theme.ReceiverText

@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
fun ReceiverNameSettings(name: String) {
    val context = LocalContext.current
    var draft by remember(name) { mutableStateOf(name) }
    var editing by remember { mutableStateOf(false) }
    val view = LocalView.current
    fun hideKeyboard() {
        (context.getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager).hideSoftInputFromWindow(view.windowToken, 0)
    }
    var message by remember { mutableStateOf("This name appears in the Windows app on your local network") }
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Receiver name", color = ReceiverText)
        if (editing) AndroidView(
            modifier = Modifier.fillMaxWidth(),
            factory = { viewContext -> EditText(viewContext).apply {
                hint = "For example: Living room projector"
                setText(draft)
                setTextColor(Color.WHITE)
                setHintTextColor(Color.LTGRAY)
                textSize = 20f
                isSingleLine = true
                filters = arrayOf(InputFilter.LengthFilter(96))
                doOnTextChanged { text, _, _, _ -> draft = text?.toString().orEmpty() }
                post {
                    requestFocus()
                    (viewContext.getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager).showSoftInput(this, InputMethodManager.SHOW_IMPLICIT)
                }
            } },
        ) else Text(name, color = ReceiverText)
        if (editing) Button(onClick = {
            message = runCatching {
                val saved = ReceiverRuntime.get(context).rename(draft)
                hideKeyboard(); editing = false
                "Receiver name saved: $saved"
            }
                .getOrElse { it.message ?: "Could not save receiver name" }
        }) { Text("SAVE RECEIVER NAME") }
        if (editing) Button(onClick = { hideKeyboard(); draft = name; editing = false }) { Text("CANCEL") }
        else Button(onClick = { editing = true }) { Text("CHANGE RECEIVER NAME") }
        Text(message, color = ReceiverText)
    }
}
