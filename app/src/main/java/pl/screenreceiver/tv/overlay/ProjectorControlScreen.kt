package pl.screenreceiver.tv.overlay

import android.content.Context
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.view.KeyEvent
import android.view.ViewGroup
import android.widget.LinearLayout
import android.widget.TextView

/** Native focusable TV controls; no Activity, screen inspection, or Compose owner is required. */
class ProjectorControlScreen(
    context: Context,
    private val onKey: (KeyEvent) -> Boolean,
    onFocus: (ControlRow) -> Unit,
    onSelect: (ControlRow) -> Unit,
) : LinearLayout(context) {
    private val rows = ControlRow.entries.map { row ->
        TextView(context).apply {
            textSize = 24f
            setTextColor(Color.WHITE)
            setPadding(dp(18), dp(12), dp(18), dp(12))
            isFocusable = true
            isFocusableInTouchMode = true
            setOnKeyListener { _, _, event -> onKey(event) }
            setOnClickListener { onSelect(row) }
            setOnFocusChangeListener { view, focused ->
                view.background = rounded(if (focused) Color.rgb(44, 88, 101) else Color.rgb(24, 34, 53))
                if (focused) onFocus(row)
            }
            background = rounded(Color.rgb(24, 34, 53))
        }
    }
    private val details = TextView(context).apply { textSize = 17f; setTextColor(Color.rgb(172, 208, 215)) }

    init {
        orientation = VERTICAL
        setPadding(dp(28), dp(22), dp(28), dp(22))
        background = rounded(Color.rgb(12, 19, 34))
        addView(TextView(context).apply {
            text = "PROJECTOR CONTROL"
            textSize = 30f
            setTypeface(typeface, Typeface.BOLD)
            setTextColor(Color.WHITE)
            setPadding(0, 0, 0, dp(16))
        })
        rows.forEach { row ->
            addView(row, LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                bottomMargin = dp(8)
            })
        }
        addView(details, LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        addView(TextView(context).apply {
            text = "↑ ↓ select   •   ← → change   •   OK apply   •   BACK to close"
            textSize = 16f
            setTextColor(Color.LTGRAY)
            setPadding(0, dp(14), 0, 0)
        })
    }

    fun render(state: ProjectorControlState, sleep: String, streaming: String, volume: String, message: String) {
        rows[ControlRow.SCREEN_BRIGHTNESS.ordinal].text = "Screen brightness   " + ScreenBrightnessPolicy.levels.joinToString(" / ") {
            if (it == state.selectedScreenBrightness) "[$it]" else "$it"
        }
        rows[ControlRow.SLEEP.ordinal].text = "Sleep timer     $sleep"
        rows[ControlRow.STREAMING.ordinal].text = "Streaming        $streaming"
        rows[ControlRow.VOLUME.ordinal].text = "Volume             $volume"
        details.text = "Screen: ${state.appliedScreenBrightness}% (software dimming)\n$message"
        if (!rows[state.row.ordinal].hasFocus()) rows[state.row.ordinal].requestFocus()
    }

    private fun rounded(color: Int) = GradientDrawable().apply { setColor(color); cornerRadius = dp(12).toFloat() }
    private fun dp(value: Int) = (value * resources.displayMetrics.density).toInt()
}
