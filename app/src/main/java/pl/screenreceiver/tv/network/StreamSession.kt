package pl.screenreceiver.tv.network

interface StreamSession {
    suspend fun send(message: SignalingMessage)
    suspend fun close()
}
