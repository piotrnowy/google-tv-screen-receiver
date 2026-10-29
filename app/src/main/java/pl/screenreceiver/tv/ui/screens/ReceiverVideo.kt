package pl.screenreceiver.tv.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.key
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.viewinterop.AndroidView
import org.webrtc.RendererCommon
import org.webrtc.SurfaceViewRenderer
import pl.screenreceiver.tv.webrtc.VideoOutput

@Composable
fun ReceiverVideo(output: VideoOutput) {
    key(output) {
        AndroidView(
            modifier = Modifier.fillMaxSize().background(Color.Black),
            factory = { context ->
                output.engine.retain()
                val renderer = SurfaceViewRenderer(context)
                try {
                    renderer.apply {
                        init(output.engine.eglBase.eglBaseContext, null)
                        setScalingType(RendererCommon.ScalingType.SCALE_ASPECT_FIT)
                        setMirror(false)
                        setEnableHardwareScaler(true)
                        output.attach(this)
                    }
                } catch (error: RuntimeException) {
                    renderer.release()
                    output.engine.release()
                    throw error
                }
            },
            onRelease = { renderer ->
                output.detach(renderer)
                try {
                    renderer.release()
                } finally {
                    output.engine.release()
                }
            },
            update = {},
        )
    }
}
