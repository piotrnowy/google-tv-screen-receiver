package pl.screenreceiver.tv.webrtc

import org.webrtc.VideoFrame
import org.webrtc.VideoSink

/** Main-thread renderer changes never touch a native VideoTrack. Detach waits for in-flight frames. */
class VideoOutput(val engine: WebRtcEngine) : VideoSink {
    private var sink: VideoSink? = null

    @Synchronized
    fun attach(sink: VideoSink) {
        this.sink = sink
    }

    @Synchronized
    fun detach(sink: VideoSink) {
        if (this.sink === sink) this.sink = null
    }

    @Synchronized
    fun clear() {
        sink = null
    }

    @Synchronized
    override fun onFrame(frame: VideoFrame) {
        sink?.onFrame(frame)
    }
}
