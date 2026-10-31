package pl.screenreceiver.tv.ui.theme

import androidx.compose.runtime.Composable
import androidx.tv.material3.ExperimentalTvMaterial3Api
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.darkColorScheme

@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
fun ScreenReceiverTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = darkColorScheme(
            primary = ReceiverPrimary,
            onPrimary = ReceiverText,
            background = ReceiverBackground,
            surface = ReceiverSurface,
            onBackground = ReceiverText,
            onSurface = ReceiverText,
            error = ReceiverError,
        ),
        typography = Typography,
        content = content,
    )
}
