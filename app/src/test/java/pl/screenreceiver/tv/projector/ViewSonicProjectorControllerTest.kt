package pl.screenreceiver.tv.projector

import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test

class ViewSonicProjectorControllerTest {
    private val ack = byteArrayOf(3, 0x14, 0, 0, 0, 0x14)
    private fun hex(value: ByteArray) = value.joinToString(" ") { "%02X".format(it.toInt() and 0xff) }

    @Test fun mapsAllFiveLevelsToPublishedCustomModePackets() {
        val fixtures = mapOf(20 to "06 14 00 04 00 34 11 10 04 71", 40 to "06 14 00 04 00 34 11 10 05 72",
            60 to "06 14 00 04 00 34 11 10 06 73", 80 to "06 14 00 04 00 34 11 10 07 74", 100 to "06 14 00 04 00 34 11 10 08 75")
        fixtures.forEach { (level, frame) -> assertEquals(frame, hex(ViewSonicProtocol.lightSource(level))) }
        assertEquals("06 14 00 04 00 34 11 01 00 5E", hex(ViewSonicProtocol.powerOff()))
        assertEquals("07 14 00 05 00 34 00 00 11 10 6E", hex(ViewSonicProtocol.readLightMode()))
        assertThrows(IllegalArgumentException::class.java) { ViewSonicProtocol.lightSource(30) }
    }

    @Test fun lightIsVerifiedOnlyAfterMatchingReadback() = runBlocking {
        var calls = 0
        val controller = ViewSonicProjectorController(ProjectorTransport {
            calls++
            if (calls == 1) ack else byteArrayOf(5, 0x14, 0, 3, 0, 0, 0, 6, 0x1d)
        })
        val result = controller.setLightSource(60)
        assertTrue(result.accepted)
        assertTrue(result.verified)
        assertEquals(2, calls)
    }

    @Test fun mismatchedOrCorruptReadbackNeverPretendsToConfirm() = runBlocking {
        for (mode in listOf(4, 100)) {
            var calls = 0
            val result = ViewSonicProjectorController(ProjectorTransport {
                if (++calls == 1) ack else byteArrayOf(5, 0x14, 0, 3, 0, 0, 0, mode.toByte(), 0x1b)
            }).setLightSource(60)
            assertFalse(result.verified)
        }
    }

    @Test fun missingTransportRejectionAndTimeoutReportFailure() = runBlocking {
        assertFalse(ViewSonicProjectorController(null).setLightSource(60).accepted)
        assertFalse(ViewSonicProjectorController(ProjectorTransport { byteArrayOf(0) }).powerOff().accepted)
        assertFalse(ViewSonicProjectorController(ProjectorTransport { throw java.io.IOException("timeout") }).powerOff().accepted)
    }

    @Test fun powerOffAcknowledgementDoesNotClaimVerifiedPowerState() = runBlocking {
        val result = ViewSonicProjectorController(ProjectorTransport { ack }).powerOff()
        assertTrue(result.accepted)
        assertFalse(result.verified)
    }
}
