package pl.screenreceiver.tv.ui

import android.graphics.Bitmap
import android.view.KeyEvent
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.unit.Density
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Rule
import org.junit.Test
import pl.screenreceiver.tv.model.ReceiverState
import pl.screenreceiver.tv.model.ReceiverStatus
import pl.screenreceiver.tv.ui.screens.ReceiverScreen
import pl.screenreceiver.tv.ui.theme.ScreenReceiverTheme
import java.io.File

/** Real TV key navigation with simulated data; never starts capture, sharing, or hardware control. */
class ReceiverNavigationTest {
    @get:Rule val compose = createComposeRule()

    private fun show(state: ReceiverState = ReceiverState(deviceName = "Living room projector", localIpAddress = "192.0.2.10"), fontScale: Float = 1f) {
        compose.setContent {
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, fontScale)) {
                ScreenReceiverTheme { ReceiverScreen(state) }
            }
        }
        compose.waitForIdle()
    }

    private fun key(code: Int) {
        InstrumentationRegistry.getInstrumentation().sendKeyDownUpSync(code)
        compose.waitForIdle()
    }

    private fun focused(tag: String) = compose.onAllNodes(hasTestTag(tag) and isFocused()).fetchSemanticsNodes().isNotEmpty()

    private fun awaitFocus(tag: String) {
        try {
            compose.waitUntil(timeoutMillis = 4_000) { focused(tag) }
        } catch (error: Throwable) {
            capture("focus-failed-" + tag)
            val context = InstrumentationRegistry.getInstrumentation().targetContext
            File(context.getExternalFilesDir(null), "focus-failed-" + tag + ".txt").writeText(compose.onRoot().printToString())
            throw error
        }
        compose.onNodeWithTag(tag).assertIsDisplayed()
    }

    private fun downTo(tag: String) {
        repeat(70) {
            if (focused(tag)) {
                compose.onNodeWithTag(tag).assertIsDisplayed()
                return
            }
            key(KeyEvent.KEYCODE_DPAD_DOWN)
        }
        capture("navigation-failed-" + tag)
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        File(context.getExternalFilesDir(null), "navigation-failure.txt").writeText(compose.onRoot().printToString())
        error("D-pad could not reach " + tag)
    }

    private fun selectTab(index: Int) {
        val tabs = listOf("tab-Receiver", "tab-Settings", "tab-Technical")
        for (attempt in 0 until 16) {
            if (tabs.any(::focused)) break
            key(KeyEvent.KEYCODE_DPAD_UP)
        }
        val current = tabs.indexOfFirst(::focused)
        check(current >= 0) { "D-pad could not reach the page tabs" }
        repeat((index - current + tabs.size) % tabs.size) { key(KeyEvent.KEYCODE_DPAD_RIGHT) }
        compose.onNodeWithTag(tabs[index]).assertIsFocused()
        key(KeyEvent.KEYCODE_DPAD_CENTER)
    }

    private fun capture(name: String) {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val destination = File(context.getExternalFilesDir(null), name + ".png")
        destination.outputStream().use {
            compose.onRoot().captureToImage().asAndroidBitmap().compress(Bitmap.CompressFormat.PNG, 100, it)
        }
    }

    @Test fun receiverInformationIsVisibleAndRemoteScrollsBothDirections() {
        show()
        compose.onNodeWithTag("receiver-status").assertIsDisplayed().assertIsFocused()
        compose.onNodeWithText("Manual address: 192.0.2.10:53530", substring = true).assertIsDisplayed()
        capture("receiver-ready")
        downTo("receiver-end")
        capture("receiver-bottom")
        repeat(3) { key(KeyEvent.KEYCODE_DPAD_UP) }
        compose.onNodeWithTag("receiver-status").assertIsFocused().assertIsDisplayed()
    }

    @Test fun everySettingsSectionAndSafeStopConfirmationCanBeReached() {
        show()
        // Reach the actual navigation through remote focus before activating it.
        key(KeyEvent.KEYCODE_DPAD_UP)
        repeat(3) {
            if (!focused("tab-Settings")) key(if (focused("tab-Technical")) KeyEvent.KEYCODE_DPAD_LEFT else KeyEvent.KEYCODE_DPAD_RIGHT)
        }
        compose.onNodeWithTag("tab-Settings").assertIsFocused()
        key(KeyEvent.KEYCODE_DPAD_CENTER)
        awaitFocus("settings-start")
        downTo("stop-receiver")
        key(KeyEvent.KEYCODE_DPAD_CENTER)
        compose.onNodeWithTag("cancel-stop").assertIsFocused().assertIsDisplayed()
        key(KeyEvent.KEYCODE_DPAD_CENTER)
        downTo("settings-end")
        capture("settings-bottom")
        key(KeyEvent.KEYCODE_BACK)
        compose.onNodeWithTag("receiver-status").assertIsFocused().assertIsDisplayed()
    }

    @Test fun technicalDetailsKeepTheCompleteErrorReachable() {
        val error = "Example diagnostic detail for testing. ".repeat(35) + "End of complete diagnostic."
        show(ReceiverState(receiverStatus = ReceiverStatus.ERROR, deviceName = "Office TV", localIpAddress = "192.0.2.11", errorMessage = error))
        selectTab(2)
        awaitFocus("technical-start")
        for (part in error.chunked(240).indices) {
            downTo("error-part-" + part)
            compose.onNodeWithText(error.chunked(240)[part], substring = true).assertIsDisplayed()
        }
        downTo("technical-end")
        capture("technical-bottom")
    }

    @Test fun largeTextAndLongNamesStillAllowRemoteAccessToAllInformation() {
        show(ReceiverState(deviceName = "A long family room projector name for the receiver", localIpAddress = "192.0.2.12"), fontScale = 1.3f)
        downTo("receiver-end")
        selectTab(1)
        awaitFocus("settings-start")
        downTo("settings-end")
        key(KeyEvent.KEYCODE_BACK)
        compose.onNodeWithTag("receiver-status").assertIsFocused()
        selectTab(2)
        awaitFocus("technical-start")
        downTo("technical-end")
        capture("large-text-bottom")
    }
}
