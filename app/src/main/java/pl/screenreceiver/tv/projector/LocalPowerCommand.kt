package pl.screenreceiver.tv.projector

/** Only two fixed commands are exposed; no host, executable or shell text comes from a sender. */
object LocalPowerCommand {
    const val remoteHelper = "/data/local/tmp/pl.screenreceiver.tv-power.jar"
    fun build(powerOff: Boolean): String =
        "CLASSPATH=$remoteHelper /system/bin/app_process /system/bin pl.screenreceiver.tv.projector.CecStandbyCommand ${if (powerOff) "off" else "status"}"

    fun result(exitCode: Int, output: String, powerOff: Boolean): ProjectorCommandResult {
        val token = if (powerOff) "PROJECTOR_CEC_STANDBY_REQUESTED" else "PROJECTOR_CEC_READY"
        val accepted = exitCode == 0 && output.lineSequence().any { it.trim() == token }
        return if (accepted) {
            ProjectorCommandResult(true, false, if (powerOff) "HDMI-CEC projector standby command sent" else "Automatic projector power-off is ready")
        } else {
            ProjectorCommandResult(false, false, output.trim().take(500).ifBlank { "Projector power control failed (exit $exitCode)" })
        }
    }
}
