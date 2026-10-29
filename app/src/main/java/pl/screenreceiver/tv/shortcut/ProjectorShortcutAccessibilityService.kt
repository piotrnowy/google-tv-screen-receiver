package pl.screenreceiver.tv.shortcut

import android.accessibilityservice.AccessibilityService
import android.accessibilityservice.AccessibilityServiceInfo
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.util.Log
import android.view.KeyEvent
import android.view.accessibility.AccessibilityEvent
import pl.screenreceiver.tv.overlay.ProjectorControlOverlay
import pl.screenreceiver.tv.overlay.ProjectorOverlayBridge
import pl.screenreceiver.tv.timer.SleepTimerManager

class ProjectorShortcutAccessibilityService : AccessibilityService() {
    private val homePress = HomePressTracker()
    private val handler = Handler(Looper.getMainLooper())
    private var overlay: ProjectorControlOverlay? = null
    private val consumedNavigation = mutableSetOf<Int>()
    private val longHome = LongHomeGesture {
        val handled = overlay?.toggle() == true
        if (handled) {
            Log.i(TAG, "LONG HOME detected")
            Log.i(TAG, "[ProjectorShortcut] LONG HOME -> Projector Control")
        }
        handled
    }
    private val longHomeTimer = Runnable { longHome.tick(SystemClock.uptimeMillis()) }

    override fun onServiceConnected() {
        super.onServiceConnected()
        handler.removeCallbacksAndMessages(null)
        overlay?.destroy()
        homePress.reset()
        longHome.reset()
        overlay = ProjectorControlOverlay(this)
        ProjectorOverlayBridge.toggle = { overlay?.toggle() == true }
        ProjectorOverlayBridge.close = { overlay?.close() }
        ProjectorOverlayBridge.showStream = {
            overlay?.close()
            runCatching { overlay?.openReceiver() }.onFailure { Log.e(TAG, "Fullscreen receiver could not open", it) }
        }
        SleepTimerManager(this).restore()
        serviceInfo = serviceInfo.apply {
            flags = flags or AccessibilityServiceInfo.FLAG_REQUEST_FILTER_KEY_EVENTS
            // Key filtering only: do not subscribe to screen/accessibility events.
            eventTypes = 0
        }
        Log.i(TAG, "[ProjectorShortcut] Service connected")
        Log.i(TAG, "Key filtering requested; short HOME passes through; long HOME threshold=800 ms")
    }

    override fun onKeyEvent(event: KeyEvent): Boolean {
        val keyName = KeyEvent.keyCodeToString(event.keyCode)
        val action = when (event.action) {
            KeyEvent.ACTION_DOWN -> "DOWN (ACTION_DOWN)"
            KeyEvent.ACTION_UP -> "UP (ACTION_UP)"
            else -> "ACTION_${event.action}"
        }
        Log.i(
            TAG,
            "[ProjectorShortcut] onKeyEvent: $keyName $action keyCode=${event.keyCode} repeat=${event.repeatCount} " +
                "eventTime=${event.eventTime} downTime=${event.downTime}",
        )
        ShortcutDiagnostics.recordKey(keyName)

        if (event.keyCode == KeyEvent.KEYCODE_HOME) {
            when (event.action) {
                KeyEvent.ACTION_DOWN -> {
                    homePress.onDown(event.downTime, event.repeatCount)
                    if (event.repeatCount == 0) {
                        handler.removeCallbacks(longHomeTimer)
                        handler.postAtTime(longHomeTimer, event.downTime + LongHomeGesture.THRESHOLD_MS)
                    }
                    return longHome.down(event.downTime, event.repeatCount, event.eventTime)
                }
                KeyEvent.ACTION_UP -> {
                    handler.removeCallbacks(longHomeTimer)
                    val duration = homePress.onUp(event.downTime, event.eventTime)
                    if (duration != null) {
                        ShortcutDiagnostics.recordHomeRelease(duration)
                        Log.i(TAG, "HOME duration=${duration} ms")
                    }
                    val consumed = longHome.up(event.downTime, event.eventTime, event.isCanceled)
                    if (!consumed) overlay?.close()
                    return consumed
                }
            }
        }

        if (event.action == KeyEvent.ACTION_UP && consumedNavigation.remove(event.keyCode)) return true
        val consumed = overlay?.handleKey(event) == true
        if (consumed && event.action == KeyEvent.ACTION_DOWN) consumedNavigation.add(event.keyCode)
        return consumed
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) = Unit

    override fun onInterrupt() {
        homePress.reset()
        longHome.reset()
        handler.removeCallbacks(longHomeTimer)
        consumedNavigation.clear()
        overlay?.close()
    }

    override fun onDestroy() {
        homePress.reset()
        longHome.reset()
        handler.removeCallbacksAndMessages(null)
        ProjectorOverlayBridge.toggle = null
        ProjectorOverlayBridge.close = null
        ProjectorOverlayBridge.showStream = null
        overlay?.destroy()
        overlay = null
        Log.i(TAG, "[ProjectorShortcut] Service destroyed")
        super.onDestroy()
    }

    private companion object {
        const val TAG = "ProjectorShortcut"
    }
}
