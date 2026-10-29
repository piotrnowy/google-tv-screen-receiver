package pl.screenreceiver.tv.overlay

import android.accessibilityservice.AccessibilityService
import android.graphics.Color
import android.graphics.PixelFormat
import android.os.Build
import android.util.Log
import android.view.Gravity
import android.view.View
import android.view.WindowManager

/** Noninteractive black composition layer below Projector Control, above ordinary app windows. */
class ScreenDimmer(private val service: AccessibilityService) {
    private val windows = service.getSystemService(WindowManager::class.java)
    private val preferences = service.getSharedPreferences("screen_dimming", AccessibilityService.MODE_PRIVATE)
    private var view: View? = null
    private val parameters = WindowManager.LayoutParams(
        WindowManager.LayoutParams.MATCH_PARENT, WindowManager.LayoutParams.MATCH_PARENT,
        WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
        WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
            WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE or
            WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
        PixelFormat.TRANSLUCENT,
    ).apply {
        gravity = Gravity.TOP or Gravity.START
        alpha = 0f
        setTitle("Projector Screen Dimming")
        if (Build.VERSION.SDK_INT >= 30) setFitInsetsTypes(0)
    }

    var brightness: Int = 100
        private set

    fun restore() {
        val saved = ScreenBrightnessPolicy.restoredLevel(preferences.getInt("brightness", 100))
        runCatching { applyWindow(saved) }
            .onSuccess { Log.i(TAG, "Screen brightness restored: $brightness%; software dimming only") }
            .onFailure { Log.e(TAG, "Screen dimming could not restore; keeping normal brightness", it) }
    }

    fun apply(percent: Int): String {
        return try {
            applyWindow(percent)
            val saved = preferences.edit().putInt("brightness", percent).commit()
            Log.i(TAG, "Screen brightness applied: $percent%; software dimming only; saved=$saved")
            "Screen brightness: $percent%; LED power unchanged" + if (saved) "" else "; could not save for restart"
        } catch (error: RuntimeException) {
            Log.e(TAG, "Screen brightness could not change", error)
            "Screen brightness unchanged: could not apply dimming"
        }
    }

    private fun applyWindow(percent: Int) {
        val previousAlpha = parameters.alpha
        parameters.alpha = ScreenBrightnessPolicy.opacity(percent)
        try {
            val current = view
            if (current == null) {
                val layer = View(service).apply {
                    setBackgroundColor(Color.BLACK)
                    importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
                }
                // Keep the zero-opacity layer at 100% too. It is created before the control
                // panel, so changing brightness never places it over/darkens those controls.
                windows.addView(layer, parameters)
                view = layer
            } else {
                windows.updateViewLayout(current, parameters)
            }
            brightness = percent
        } catch (error: RuntimeException) {
            parameters.alpha = previousAlpha
            throw error
        }
    }

    fun destroy() {
        val current = view ?: return
        runCatching { windows.removeViewImmediate(current) }
            .onFailure { Log.w(TAG, "Screen dimming layer removal failed", it) }
        view = null
        brightness = 100
    }

    private companion object { const val TAG = "ProjectorOverlay" }
}
