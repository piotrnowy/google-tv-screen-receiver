package pl.screenreceiver.tv.projector

interface ProjectorController {
    suspend fun setLightSource(percent: Int): ProjectorCommandResult
    suspend fun powerOff(): ProjectorCommandResult
}

data class ProjectorCommandResult(val accepted: Boolean, val verified: Boolean, val message: String)

fun interface ProjectorTransport {
    suspend fun exchange(command: ByteArray): ByteArray
}
