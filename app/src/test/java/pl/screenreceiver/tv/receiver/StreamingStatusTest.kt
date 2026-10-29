package pl.screenreceiver.tv.receiver

import org.junit.Assert.assertEquals
import org.junit.Test

class StreamingStatusTest {
    @Test fun readyRequiresActualListeningAndErrorsAreVisible() {
        assertEquals(StreamingStatus.STOPPED, streamingStatus(false, false, null))
        assertEquals(StreamingStatus.READY, streamingStatus(true, false, null))
        assertEquals(StreamingStatus.ERROR, streamingStatus(false, false, "Bind failed"))
        assertEquals(StreamingStatus.ERROR, streamingStatus(true, false, "Error"))
    }

    @Test fun anExistingConnectionRemainsConnectedDuringNegotiationOrPeerErrors() {
        assertEquals(StreamingStatus.CONNECTED, streamingStatus(true, true, null))
        assertEquals(StreamingStatus.CONNECTED, streamingStatus(true, true, "Peer error"))
    }
}
