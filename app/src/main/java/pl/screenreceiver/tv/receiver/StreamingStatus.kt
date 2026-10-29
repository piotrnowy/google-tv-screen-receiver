package pl.screenreceiver.tv.receiver

enum class StreamingStatus { STOPPED, READY, CONNECTED, ERROR }

fun streamingStatus(listening: Boolean, connected: Boolean, error: String?): StreamingStatus = when {
    connected -> StreamingStatus.CONNECTED
    error != null -> StreamingStatus.ERROR
    listening -> StreamingStatus.READY
    else -> StreamingStatus.STOPPED
}
