package pl.screenreceiver.tv.webrtc

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test

class WebRtcCommandQueueTest {
    @Test(timeout = 5000)
    fun failedSdpOrIceCommandKeepsWorkerAliveForLaterCallbacks() = runBlocking {
        val commands = Channel<suspend () -> Unit>(8)
        val errors = mutableListOf<Exception>()
        val processed = mutableListOf<String>()
        val completed = CompletableDeferred<Unit>()
        var disposed = false
        val worker = launch {
            try {
                processWebRtcCommands(commands) { errors.add(it) }
            } finally {
                disposed = true
            }
        }
        try {
            val invalidSdp = IllegalArgumentException("Set SDP failed")
            val rejectedIce = IllegalStateException("Remote ICE candidate was rejected")
            commands.send { throw invalidSdp }
            commands.send { processed.add("ice") }
            commands.send { throw rejectedIce }
            commands.send { processed.add("local answer set") }
            commands.send { processed.add("answer sent"); completed.complete(Unit) }
            withTimeout(3000) { completed.await() }

            assertEquals(listOf("ice", "local answer set", "answer sent"), processed)
            assertEquals(listOf(invalidSdp, rejectedIce), errors)
            assertFalse("Per-message errors must not reach session cleanup", disposed)
            assertTrue(worker.isActive)
        } finally {
            commands.close()
            worker.cancelAndJoin()
        }
        assertTrue(disposed)
    }

    @Test(timeout = 5000)
    fun cancellationStillEndsWorkerAndRunsSessionCleanup() = runBlocking {
        val commands = Channel<suspend () -> Unit>(2)
        val cancellation = CancellationException("Session stopped")
        var reportedError: Exception? = null
        var disposed = false
        var laterCallbackRan = false
        val worker = launch {
            try {
                processWebRtcCommands(commands) { reportedError = it }
            } finally {
                disposed = true
            }
        }
        commands.send { throw cancellation }
        commands.send { laterCallbackRan = true }
        withTimeout(3000) { worker.join() }
        commands.close()

        assertTrue(worker.isCancelled)
        assertTrue(disposed)
        assertFalse(laterCallbackRan)
        assertSame(null, reportedError)
    }
}
