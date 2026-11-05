package pl.screenreceiver.tv.projector

import org.junit.Assert.*
import org.junit.Test

class LocalPowerCommandTest {
    @Test fun exposesOnlyStatusAndIdempotentStandby() {
        val status = LocalPowerCommand.build(false)
        val off = LocalPowerCommand.build(true)
        assertTrue(status.endsWith("CecStandbyCommand status"))
        assertTrue(off.endsWith("CecStandbyCommand off"))
        assertFalse(off.contains("keyevent"))
    }

    @Test fun helperPathAndExecutableAreFixed() {
        assertEquals("CLASSPATH=/data/local/tmp/pl.screenreceiver.tv-power.jar /system/bin/app_process /system/bin pl.screenreceiver.tv.projector.CecStandbyCommand status", LocalPowerCommand.build(false))
    }

    @Test fun requiresSuccessfulExitAndTheExactCompletionToken() {
        assertTrue(LocalPowerCommand.result(0, "PROJECTOR_CEC_STANDBY_REQUESTED\n", true).accepted)
        assertFalse(LocalPowerCommand.result(1, "PROJECTOR_CEC_STANDBY_REQUESTED\n", true).accepted)
        assertFalse(LocalPowerCommand.result(0, "error: PROJECTOR_CEC_STANDBY_REQUESTED", true).accepted)
        assertFalse(LocalPowerCommand.result(0, "PROJECTOR_CEC_READY", true).accepted)
        assertFalse(LocalPowerCommand.result(0, "", true).accepted)
        assertTrue(LocalPowerCommand.result(0, "PROJECTOR_CEC_READY", false).accepted)
    }

    @Test fun commandAcceptanceNeverClaimsVerifiedPhysicalPowerState() {
        val result = LocalPowerCommand.result(0, "PROJECTOR_CEC_STANDBY_REQUESTED", true)
        assertFalse(result.verified)
        assertTrue(result.message.contains("standby command sent"))
        assertEquals(500, LocalPowerCommand.result(1, "x".repeat(2000), true).message.length)
    }
}
