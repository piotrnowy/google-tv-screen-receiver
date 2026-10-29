package pl.screenreceiver.tv.overlay

import android.accessibilityservice.AccessibilityService
import android.content.ActivityNotFoundException
import android.content.Intent
import android.graphics.PixelFormat
import android.media.AudioManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import android.util.Log
import android.view.Gravity
import android.view.KeyEvent
import android.view.WindowManager
import android.widget.Toast
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import pl.screenreceiver.tv.MainActivity
import pl.screenreceiver.tv.audio.AndroidMediaVolumeController
import pl.screenreceiver.tv.projector.ProjectorControllers
import pl.screenreceiver.tv.receiver.ReceiverRuntime
import pl.screenreceiver.tv.receiver.ReceiverService
import pl.screenreceiver.tv.receiver.StreamingStatus
import pl.screenreceiver.tv.receiver.streamingStatus
import pl.screenreceiver.tv.timer.SleepTimerManager

/** Owned by the AccessibilityService; TYPE_ACCESSIBILITY_OVERLAY needs no SYSTEM_ALERT_WINDOW. */
class ProjectorControlOverlay(private val service: AccessibilityService) {
    private val windows = service.getSystemService(WindowManager::class.java)
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val timer = SleepTimerManager(service)
    private val media = AndroidMediaVolumeController(service)
    private val receiver = ReceiverRuntime.get(service)
    private val dimmer = ScreenDimmer(service).apply { restore() }
    private var screen: ProjectorControlScreen? = null
    private var state = ProjectorControlState(
        screenBrightnessIndex = ScreenBrightnessPolicy.levels.indexOf(dimmer.brightness),
        appliedScreenBrightness = dimmer.brightness,
    )
    private var message = "Choose a value with LEFT/RIGHT; press OK to apply"

    init {
        scope.launch {
            while (isActive) {
                if (screen != null) render()
                delay(1000)
            }
        }
    }

    /** True only when the toggle actually succeeded, so a failed open does not swallow HOME. */
    fun toggle(): Boolean {
        if (screen != null) { close(); return true }
        state = state.copy(isOpen = false).reduce(OverlayEvent.Toggle)
        val view = ProjectorControlScreen(service, ::handleKey,
            { row -> state = state.reduce(OverlayEvent.Focus(row)) },
            { row -> state = state.reduce(OverlayEvent.Focus(row)); activate() })
        val density = service.resources.displayMetrics.density
        val width = minOf((850 * density).toInt(), service.resources.displayMetrics.widthPixels - (48 * density).toInt())
        val params = WindowManager.LayoutParams(
            width, WindowManager.LayoutParams.WRAP_CONTENT,
            WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
            WindowManager.LayoutParams.FLAG_DIM_BEHIND,
            PixelFormat.TRANSLUCENT,
        ).apply { gravity = Gravity.CENTER; dimAmount = 0.6f; setTitle("Projector Control") }
        return try {
            windows.addView(view, params)
            screen = view
            render()
            Log.i(TAG, "Overlay opened")
            true
        } catch (error: RuntimeException) {
            state = state.reduce(OverlayEvent.Close)
            Log.e(TAG, "Overlay could not open", error)
            false
        }
    }

    fun close() {
        val view = screen ?: return
        screen = null
        state = state.reduce(OverlayEvent.Close)
        runCatching { windows.removeViewImmediate(view) }.onFailure { Log.w(TAG, "Overlay removal failed", it) }
        Log.i(TAG, "Overlay closed")
    }

    fun handleKey(event: KeyEvent): Boolean {
        if (screen == null) return false
        if (event.keyCode !in navigationKeys) return false
        if (event.action == KeyEvent.ACTION_DOWN) {
            when (event.keyCode) {
                KeyEvent.KEYCODE_BACK -> close()
                KeyEvent.KEYCODE_DPAD_UP -> state = state.reduce(OverlayEvent.Move(-1))
                KeyEvent.KEYCODE_DPAD_DOWN -> state = state.reduce(OverlayEvent.Move(1))
                KeyEvent.KEYCODE_DPAD_LEFT -> change(-1)
                KeyEvent.KEYCODE_DPAD_RIGHT -> change(1)
                KeyEvent.KEYCODE_DPAD_CENTER, KeyEvent.KEYCODE_ENTER -> if (event.repeatCount == 0) activate()
            }
            render()
        }
        return true
    }

    private fun change(delta: Int) {
        if (state.row == ControlRow.VOLUME) {
            val current = media.state()
            if (!current.fixed) media.adjust(if (delta > 0) AudioManager.ADJUST_RAISE else AudioManager.ADJUST_LOWER)
            else message = "Google TV uses fixed-volume output"
        } else state = state.reduce(OverlayEvent.Change(delta))
    }

    private fun activate() {
        when (state.row) {
            ControlRow.SCREEN_BRIGHTNESS -> {
                message = dimmer.apply(state.selectedScreenBrightness)
                state = state.reduce(OverlayEvent.ScreenBrightnessApplied(dimmer.brightness))
            }
            ControlRow.SLEEP -> {
                if (state.selectedSleep > 0 && !timer.canSchedule()) {
                    message = "Allow Alarms & reminders, then select the timer again"
                    openTimerSettings()
                } else message = runCatching {
                    timer.set(state.selectedSleep) + if (state.selectedSleep > 0 && ProjectorControllers.address(service).isBlank()) {
                        "; projector disconnected, power-off will only be logged"
                    } else ""
                }.getOrElse { "Timer not set: ${it.message}" }
            }
            ControlRow.STREAMING -> {
                if (!ReceiverService.hasLanPermission(service)) {
                    close()
                    openReceiver()
                    return
                }
                if (!receiver.listening.value) {
                    if (ReceiverService.start(service)) {
                        message = "Starting receiver…"
                    } else {
                        close()
                        openReceiver()
                        return
                    }
                } else {
                    message = "${receiver.uiState.value.connectionAddress ?: "Address unavailable"} · ${receiver.uiState.value.connectedDeviceName ?: "Waiting for sender"}"
                    if (receiver.videoOutput.value != null) { close(); openReceiver() }
                }
            }
            ControlRow.VOLUME -> message = "LEFT/RIGHT adjusts Google TV media volume"
        }
        render()
    }

    private fun render() {
        val plan = timer.active()
        val remaining = plan?.let { timer.remainingMs(it) }
        val selected = if (state.selectedSleep == 0) "Off" else "${state.selectedSleep} min"
        val sleep = "[$selected]" + if (remaining != null) {
            " · ${remaining / 60_000}:${((remaining / 1000) % 60).toString().padStart(2, '0')} remaining"
        } else " · Off"
        val status = streamingStatus(receiver.listening.value, receiver.connected.value, receiver.uiState.value.errorMessage)
        val streaming = when (status) {
            StreamingStatus.READY -> "Ready · ${receiver.uiState.value.connectionAddress ?: "address unavailable"}"
            StreamingStatus.CONNECTED -> "Connected"
            StreamingStatus.ERROR -> "Error · select to retry"
            StreamingStatus.STOPPED -> "Not running · OK to start"
        }
        val volume = media.state().let { "${it.current} / ${it.maximum}${if (it.muted) " · Muted" else ""}${if (it.fixed) " · Fixed" else ""}" }
        val timerResult = timer.lastResult().takeIf { it.startsWith("Sleep timer expired:") || it.startsWith("Sleep timer failed:") }
        screen?.render(state, sleep, streaming, volume, listOfNotNull(message, timerResult).joinToString("\n"))
    }

    private fun openTimerSettings() {
        if (Build.VERSION.SDK_INT < 31) return
        close()
        try {
            service.startActivity(Intent(Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM,
                Uri.parse("package:${service.packageName}")).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        } catch (_: ActivityNotFoundException) {
            Toast.makeText(service, "Open Settings → Apps → Special app access → Alarms & reminders", Toast.LENGTH_LONG).show()
        }
    }

    fun openReceiver() {
        service.startActivity(Intent(service, MainActivity::class.java)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP))
    }

    fun destroy() { close(); dimmer.destroy(); scope.cancel() }

    private companion object {
        const val TAG = "ProjectorOverlay"
        val navigationKeys = setOf(KeyEvent.KEYCODE_BACK, KeyEvent.KEYCODE_DPAD_UP, KeyEvent.KEYCODE_DPAD_DOWN,
            KeyEvent.KEYCODE_DPAD_LEFT, KeyEvent.KEYCODE_DPAD_RIGHT, KeyEvent.KEYCODE_DPAD_CENTER, KeyEvent.KEYCODE_ENTER)
    }
}
