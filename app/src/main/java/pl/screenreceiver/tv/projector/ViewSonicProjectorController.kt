package pl.screenreceiver.tv.projector

import kotlinx.coroutines.CancellationException

class ViewSonicProjectorController(
    private val transport: ProjectorTransport?,
    private val log: (String) -> Unit = {},
) : ProjectorController {
    override suspend fun setLightSource(percent: Int): ProjectorCommandResult {
        val command = ViewSonicProtocol.lightSource(percent)
        log("Light source request: $percent")
        return execute(command) {
            val mode = ViewSonicProtocol.readMode(it.exchange(ViewSonicProtocol.readLightMode()))
            if (mode == ViewSonicProtocol.lightMode(percent)) {
                ProjectorCommandResult(true, true, "Light source confirmed: $percent%")
            } else {
                ProjectorCommandResult(true, false, "Request acknowledged; light source not confirmed")
            }
        }
    }

    override suspend fun powerOff(): ProjectorCommandResult {
        log("Projector power-off request")
        return execute(ViewSonicProtocol.powerOff()) {
            ProjectorCommandResult(true, false, "Power-off command acknowledged")
        }
    }

    private suspend fun execute(
        command: ByteArray,
        onAcknowledged: suspend (ProjectorTransport) -> ProjectorCommandResult,
    ): ProjectorCommandResult {
        log("Prepared RS232/LAN command: " + command.joinToString(" ") { "%02X".format(it.toInt() and 0xff) })
        val link = transport ?: run {
            log("Projector disconnected: command logged, not sent")
            return ProjectorCommandResult(false, false, "Projector disconnected: command logged, not sent")
        }
        return try {
            if (!ViewSonicProtocol.acknowledged(link.exchange(command))) {
                ProjectorCommandResult(false, false, "Projector rejected command or returned an invalid acknowledgement")
            } else onAcknowledged(link)
        } catch (error: CancellationException) {
            throw error
        } catch (error: Exception) {
            log("Projector command failed: ${error.message}")
            ProjectorCommandResult(false, false, "Projector not confirmed: ${error.message}")
        }
    }
}
