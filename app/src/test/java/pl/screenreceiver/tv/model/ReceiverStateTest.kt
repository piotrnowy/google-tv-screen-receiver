package pl.screenreceiver.tv.model

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ReceiverStateTest {
    @Test
    fun followsConnectionNegotiationAndStreamingLifecycle() {
        var state = ReceiverState(localIpAddress = "192.0.2.10")
        assertEquals(ReceiverStatus.WAITING, state.receiverStatus)
        assertEquals("192.0.2.10:53530", state.connectionAddress)
        state = state.reduce(ReceiverEvent.Connected)
        assertEquals(ReceiverStatus.IPHONE_CONNECTED, state.receiverStatus)
        state = state.reduce(ReceiverEvent.Hello("Example iPhone"))
        state = state.reduce(ReceiverEvent.Negotiating)
        assertEquals(ReceiverStatus.NEGOTIATING, state.receiverStatus)
        state = state.reduce(ReceiverEvent.VideoReceived)
        assertEquals(ReceiverStatus.STREAMING, state.receiverStatus)
        assertEquals("Example iPhone", state.connectedDeviceName)
        state = state.reduce(ReceiverEvent.Disconnected)
        assertEquals(ReceiverStatus.WAITING, state.receiverStatus)
        assertNull(state.connectedDeviceName)
        assertEquals("192.0.2.10:53530", state.connectionAddress)
    }

    @Test
    fun disconnectAndReconnectClearErrorAndSenderMetadata() {
        val failed = ReceiverState().reduce(ReceiverEvent.Connected)
            .reduce(ReceiverEvent.Hello("Old iPhone"))
            .reduce(ReceiverEvent.Failure("Invalid SDP"))
        assertEquals(ReceiverStatus.ERROR, failed.receiverStatus)
        assertEquals("Invalid SDP", failed.errorMessage)
        for (event in listOf(ReceiverEvent.Disconnected, ReceiverEvent.Connected)) {
            val cleared = failed.reduce(event)
            assertNull(cleared.connectedDeviceName)
            assertNull(cleared.errorMessage)
        }
    }

    @Test
    fun lateHelloDoesNotRegressStreamingStatus() {
        val state = ReceiverState().reduce(ReceiverEvent.VideoReceived)
            .reduce(ReceiverEvent.Hello("iPad"))
        assertEquals(ReceiverStatus.STREAMING, state.receiverStatus)
        assertEquals("iPad", state.connectedDeviceName)
    }

    @Test
    fun unavailableIpDoesNotProduceAnInvalidEndpoint() {
        assertNull(ReceiverState().connectionAddress)
        assertEquals("10.0.0.1:9999", ReceiverState(localIpAddress = "10.0.0.1", signalingPort = 9999).connectionAddress)
    }
}
