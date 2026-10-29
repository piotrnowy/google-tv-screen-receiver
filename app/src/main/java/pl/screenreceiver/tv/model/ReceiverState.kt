package pl.screenreceiver.tv.model

data class ReceiverState(
    val receiverStatus: ReceiverStatus = ReceiverStatus.WAITING,
    val deviceName: String = "Google TV",
    val localIpAddress: String? = null,
    val connectedDeviceName: String? = null,
    val errorMessage: String? = null,
    val signalingPort: Int = 53530,
) {
    val connectionAddress: String?
        get() = localIpAddress?.let { "$it:$signalingPort" }

    fun reduce(event: ReceiverEvent): ReceiverState = when (event) {
        ReceiverEvent.Connected -> copy(
            receiverStatus = ReceiverStatus.IPHONE_CONNECTED,
            connectedDeviceName = null,
            errorMessage = null,
        )
        is ReceiverEvent.Hello -> copy(connectedDeviceName = event.deviceName)
        ReceiverEvent.Negotiating -> copy(receiverStatus = ReceiverStatus.NEGOTIATING)
        ReceiverEvent.VideoReceived -> copy(receiverStatus = ReceiverStatus.STREAMING)
        is ReceiverEvent.Failure -> copy(receiverStatus = ReceiverStatus.ERROR, errorMessage = event.message)
        ReceiverEvent.Disconnected -> copy(
            receiverStatus = ReceiverStatus.WAITING,
            connectedDeviceName = null,
            errorMessage = null,
        )
    }
}

sealed interface ReceiverEvent {
    data object Connected : ReceiverEvent
    data class Hello(val deviceName: String) : ReceiverEvent
    data object Negotiating : ReceiverEvent
    data object VideoReceived : ReceiverEvent
    data class Failure(val message: String) : ReceiverEvent
    data object Disconnected : ReceiverEvent
}
