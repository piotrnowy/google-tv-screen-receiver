package pl.screenreceiver.tv.webrtc

import android.content.Context
import android.media.AudioAttributes
import android.util.Log
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import org.webrtc.DefaultVideoDecoderFactory
import org.webrtc.DefaultVideoEncoderFactory
import org.webrtc.EglBase
import org.webrtc.PeerConnectionFactory
import org.webrtc.audio.JavaAudioDeviceModule

/** One factory per ViewModel. Native SDK initialization happens once per application process. */
class WebRtcEngine private constructor(
    val eglBase: EglBase,
    val factory: PeerConnectionFactory,
) {
    private var references = 1 // ViewModel ownership; each visible renderer also holds a lease.

    @Synchronized
    fun retain() {
        check(references > 0)
        references++
    }

    @Synchronized
    fun release() {
        check(references > 0)
        references--
        if (references == 0) {
            CoroutineScope(Dispatchers.IO).launch {
                factory.dispose()
                eglBase.release()
            }
        }
    }

    companion object {
        private var initialized = false

        @Synchronized
        fun create(context: Context): WebRtcEngine {
            if (!initialized) {
                PeerConnectionFactory.initialize(
                    PeerConnectionFactory.InitializationOptions.builder(context.applicationContext)
                        .createInitializationOptions(),
                )
                initialized = true
            }
            val egl = EglBase.create()
            return try {
                val audio = JavaAudioDeviceModule.builder(context.applicationContext)
                    .setUseStereoOutput(true)
                    .setUseHardwareAcousticEchoCanceler(false)
                    .setUseHardwareNoiseSuppressor(false)
                    .setAudioTrackStateCallback(object : JavaAudioDeviceModule.AudioTrackStateCallback {
                        override fun onWebRtcAudioTrackStart() { Log.i("WebRtcReceiver", "[Audio Playout] started") }
                        override fun onWebRtcAudioTrackStop() { Log.i("WebRtcReceiver", "[Audio Playout] stopped") }
                    })
                    .setAudioTrackErrorCallback(object : JavaAudioDeviceModule.AudioTrackErrorCallback {
                        override fun onWebRtcAudioTrackInitError(message: String) {
                            Log.e("WebRtcReceiver", "[Audio Playout] Initialization failed", IllegalStateException(message))
                        }
                        override fun onWebRtcAudioTrackStartError(code: JavaAudioDeviceModule.AudioTrackStartErrorCode, message: String) {
                            Log.e("WebRtcReceiver", "[Audio Playout] Start failed: $code", IllegalStateException(message))
                        }
                        override fun onWebRtcAudioTrackError(message: String) {
                            Log.e("WebRtcReceiver", "[Audio Playout] Playback failed", IllegalStateException(message))
                        }
                    })
                    .setAudioAttributes(AudioAttributes.Builder()
                        .setUsage(AudioAttributes.USAGE_MEDIA)
                        .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                        .build())
                    .createAudioDeviceModule()
                Log.i("WebRtcReceiver", "[Audio Playout] initialized: stereo, USAGE_MEDIA/CONTENT_TYPE_MUSIC; microphone recording disabled")
                try {
                    val factory = PeerConnectionFactory.builder()
                        .setAudioDeviceModule(audio)
                        .setVideoDecoderFactory(DefaultVideoDecoderFactory(egl.eglBaseContext))
                        .setVideoEncoderFactory(DefaultVideoEncoderFactory(egl.eglBaseContext, true, true))
                        .createPeerConnectionFactory()
                    WebRtcEngine(egl, factory)
                } finally {
                    // The native factory retains its own module reference.
                    audio.release()
                }
            } catch (error: Throwable) {
                egl.release()
                throw error
            }
        }
    }
}
