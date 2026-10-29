package pl.screenreceiver.tv.audio

import android.content.Context
import android.media.AudioManager
import kotlin.math.roundToInt

/** Prepared for future explicit remote controls; never constructed/called by session startup. */
interface MediaVolumeController {
    data class State(val minimum: Int, val maximum: Int, val current: Int, val muted: Boolean, val fixed: Boolean = false)
    fun state(): State
    fun setVolume(value: Double)
    fun setMuted(muted: Boolean)
    fun adjust(direction: Int)
}

class AndroidMediaVolumeController(context: Context) : MediaVolumeController {
    private val manager = context.applicationContext.getSystemService(Context.AUDIO_SERVICE) as AudioManager
    override fun state(): MediaVolumeController.State = MediaVolumeController.State(
        if (android.os.Build.VERSION.SDK_INT >= 28) manager.getStreamMinVolume(AudioManager.STREAM_MUSIC) else 0,
        manager.getStreamMaxVolume(AudioManager.STREAM_MUSIC),
        manager.getStreamVolume(AudioManager.STREAM_MUSIC),
        manager.isStreamMute(AudioManager.STREAM_MUSIC),
        manager.isVolumeFixed,
    )
    override fun setVolume(value: Double) {
        val current = state()
        manager.setStreamVolume(AudioManager.STREAM_MUSIC, mediaVolumeIndex(value, current.minimum, current.maximum), 0)
    }
    override fun setMuted(muted: Boolean) {
        manager.adjustStreamVolume(AudioManager.STREAM_MUSIC, if (muted) AudioManager.ADJUST_MUTE else AudioManager.ADJUST_UNMUTE, 0)
    }
    override fun adjust(direction: Int) {
        manager.adjustStreamVolume(AudioManager.STREAM_MUSIC, direction, 0)
    }
}

/** Applies remote keyboard actions to media playback only and returns the state acknowledgement. */
class RemoteVolumeController(
    private val media: MediaVolumeController,
    private val log: (String) -> Unit = {},
) {
    data class Result(val current: Int, val max: Int, val muted: Boolean, val available: Boolean)

    fun current(): Result {
        val state = media.state()
        if (state.fixed) log("[Volume] Device uses fixed-volume output")
        return state.toResult()
    }

    fun apply(action: String): Result {
        require(action in setOf("up", "down", "muteToggle")) { "Unsupported volume action" }
        val before = media.state()
        if (before.fixed) {
            log("[Volume] Device uses fixed-volume output")
            return before.toResult(available = false)
        }
        when (action) {
            "up" -> media.adjust(android.media.AudioManager.ADJUST_RAISE)
            "down" -> media.adjust(android.media.AudioManager.ADJUST_LOWER)
            "muteToggle" -> media.setMuted(!before.muted)
        }
        return media.state().toResult()
    }

    private fun MediaVolumeController.State.toResult(available: Boolean = !fixed) = Result(current, maximum, muted, available)
}

internal fun mediaVolumeIndex(value: Double, minimum: Int, maximum: Int): Int {
    require(value.isFinite() && value in 0.0..1.0 && minimum >= 0 && maximum >= minimum)
    return minimum + (value * (maximum - minimum)).roundToInt()
}
