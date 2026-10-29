package pl.screenreceiver.tv.network

import java.io.IOException
import java.io.StringReader
import java.net.ServerSocket
import java.net.Socket
import java.net.SocketTimeoutException
import java.util.concurrent.CopyOnWriteArrayList
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class ReceiverServerTest {
    @Test(timeout = 15000)
    fun acceptsMessagesSendsAnswerAndAcceptsNextSenderAfterByeAndEof() = runBlocking {
        val port = ServerSocket(0).use { it.localPort }
        val server = ReceiverServer(port) { _, _ -> }
        val connected = Channel<StreamSession>(4)
        val messages = Channel<SignalingMessage>(8)
        val disconnected = Channel<Unit>(4)
        val job = launch(Dispatchers.IO) {
            server.run(object : ReceiverServer.Listener {
                override suspend fun onConnected(session: StreamSession) { connected.send(session) }
                override suspend fun onMessage(session: StreamSession, message: SignalingMessage) {
                    messages.send(message)
                    if (message is SignalingMessage.Offer) session.send(SignalingMessage.Answer("answer\r\n"))
                }
                override suspend fun onDisconnected(session: StreamSession) { disconnected.send(Unit) }
            })
        }
        try {
            connect(port).use { socket ->
                withTimeout(3000) { connected.receive() }
                socket.soTimeout = 3000
                val writer = socket.getOutputStream().bufferedWriter(Charsets.UTF_8)
                // A malformed line must not terminate the connection or poison the next message.
                writer.write("broken JSON\n")
                writer.write(SignalingCodec.encode(SignalingMessage.Hello("Test iPad")) + "\n")
                writer.write(SignalingCodec.encode(SignalingMessage.Offer("offer\r\n")) + "\n")
                writer.flush()
                assertEquals(SignalingMessage.Hello("Test iPad"), withTimeout(3000) { messages.receive() })
                assertEquals(SignalingMessage.Offer("offer\r\n"), withTimeout(3000) { messages.receive() })
                val response = async(Dispatchers.IO) { socket.getInputStream().bufferedReader().readLine() }
                assertEquals(SignalingMessage.Answer("answer\r\n"), SignalingCodec.parse(response.await()))
                writer.write("{\"type\":\"bye\"}\n")
                writer.flush()
                withTimeout(3000) { disconnected.receive() }
            }
            connect(port).use {
                withTimeout(3000) { connected.receive() }
            }
            withTimeout(3000) { disconnected.receive() }
            connect(port).use {
                withTimeout(3000) { connected.receive() }
                // Closing the server also unblocks a client's otherwise idle read.
                server.stop()
                withTimeout(3000) { disconnected.receive() }
            }
            withTimeout(3000) { job.join() }
        } finally {
            server.stop()
            job.join()
        }
    }

    @Test(timeout = 5000)
    fun stopBeforeStartIsSafe() = runBlocking {
        val server = ReceiverServer(0) { _, _ -> }
        server.stop()
        server.run(object : ReceiverServer.Listener {
            override suspend fun onConnected(session: StreamSession) = error("Unexpected connection")
            override suspend fun onMessage(session: StreamSession, message: SignalingMessage) = Unit
            override suspend fun onDisconnected(session: StreamSession) = Unit
        })
    }

    @Test
    fun framingHandlesCrLfMultipleMessagesAndIncompleteEof() {
        val reader = StringReader("first\r\nsecond\nunfinished").buffered()
        assertEquals("first", reader.readBoundedLine())
        assertEquals("second", reader.readBoundedLine())
        assertNull(reader.readBoundedLine())
    }

    @Test(expected = IOException::class)
    fun framingRejectsUnboundedLines() {
        StringReader("x".repeat(SignalingCodec.MAX_LINE_CHARS + 1) + "\n").buffered().readBoundedLine()
    }

    @Test(timeout = 10000)
    fun connectionRemainsAliveAfterHello() = withServer {
        connect(port).use { socket ->
            connected.receive()
            socket.send(SignalingMessage.Hello("Windows"))
            assertEquals(SignalingMessage.Hello("Windows"), messages.receive())
            assertIdleConnection(socket)
            socket.send(SignalingMessage.Offer("offer"))
            assertEquals(SignalingMessage.Offer("offer"), messages.receive())
        }
    }

    @Test(timeout = 10000)
    fun connectionRemainsAliveAfterOfferAndAnswer() = withServer {
        connect(port).use { socket ->
            connected.receive()
            socket.send(SignalingMessage.Offer("offer"))
            assertEquals(SignalingMessage.Offer("offer"), messages.receive())
            socket.soTimeout = 3000
            assertEquals(SignalingMessage.Answer("answer"),
                SignalingCodec.parse(socket.getInputStream().bufferedReader().readLine()))
            assertIdleConnection(socket)
            socket.send(SignalingMessage.Hello("Still connected"))
            assertEquals(SignalingMessage.Hello("Still connected"), messages.receive())
            assertTrue(logs.contains("Sending: answer"))
        }
    }

    @Test(timeout = 10000)
    fun multipleEarlyIceMessagesDoNotCloseSession() = withServer {
        connect(port).use { socket ->
            val session = connected.receive()
            // The listener models asynchronous SDP work: all early ICE must reach it.
            socket.send(SignalingMessage.Hello("Windows"))
            socket.send(SignalingMessage.Offer("offer"))
            val candidates = (1..5).map { SignalingMessage.Ice("0", 0, "candidate:$it") }
            candidates.forEach { socket.send(it) }
            assertEquals(SignalingMessage.Hello("Windows"), messages.receive())
            assertEquals(SignalingMessage.Offer("offer"), messages.receive())
            candidates.forEach { assertEquals(it, messages.receive()) }
            socket.soTimeout = 3000
            val reader = socket.getInputStream().bufferedReader()
            assertEquals(SignalingMessage.Answer("answer"), SignalingCodec.parse(reader.readLine()))
            session.send(candidates.first())
            assertEquals(candidates.first(), SignalingCodec.parse(reader.readLine()))
            assertIdleConnection(socket)
            assertEquals(5, logs.count { it == "Received: ice" })
            assertTrue(logs.contains("Sending: ice"))
        }
    }

    @Test(timeout = 10000)
    fun byeClosesSessionAndNextClientCanConnect() = withServer {
        connect(port).use { socket ->
            connected.receive()
            socket.send(SignalingMessage.Bye)
            disconnected.receive()
            socket.soTimeout = 3000
            assertEquals(-1, socket.getInputStream().read())
            assertTrue(logs.contains("Received: bye"))
            assertTrue(logs.contains("Client socket closed"))
        }
        connect(port).use { socket ->
            connected.receive()
            socket.send(SignalingMessage.Hello("Next sender"))
            assertEquals(SignalingMessage.Hello("Next sender"), messages.receive())
        }
    }

    @Test(timeout = 10000)
    fun eofClosesSessionAndNextClientCanConnect() = withServer {
        connect(port).use { socket ->
            connected.receive()
            socket.shutdownOutput()
            disconnected.receive()
            socket.soTimeout = 3000
            assertEquals(-1, socket.getInputStream().read())
            assertTrue(logs.contains("Client EOF"))
        }
        connect(port).use {
            connected.receive()
            assertIdleConnection(it)
        }
    }

    @Test(timeout = 10000)
    fun malformedMessagesAreIgnoredWithoutClosingSession() = withServer {
        connect(port).use { socket ->
            connected.receive()
            socket.getOutputStream().write(
                "broken JSON\n[]\n{\"type\":\"offer\"}\n{\"type\":\"unknown\"}\n".toByteArray(Charsets.UTF_8),
            )
            socket.send(SignalingMessage.Hello("After malformed messages"))
            assertEquals(SignalingMessage.Hello("After malformed messages"), messages.receive())
            assertIdleConnection(socket)
            assertEquals(4, logs.count { it.startsWith("Ignoring malformed") })
        }
    }

    @Test(timeout = 10000)
    fun recoverableHandlerFailureDoesNotCloseSessionOrListener() = withServer(
        onMessage = { _, message ->
            if (message is SignalingMessage.Offer) throw IllegalArgumentException("Bad SDP")
        },
    ) {
        connect(port).use { socket ->
            connected.receive()
            socket.send(SignalingMessage.Offer("bad offer"))
            assertEquals(SignalingMessage.Offer("bad offer"), messages.receive())
            socket.send(SignalingMessage.Ice("0", 0, "next candidate"))
            assertEquals(SignalingMessage.Ice("0", 0, "next candidate"), messages.receive())
            assertIdleConnection(socket)
            assertTrue(logs.any { it.startsWith("Exception: handling offer") })
            socket.send(SignalingMessage.Bye)
            disconnected.receive()
        }
        connect(port).use { connected.receive() }
    }

    @Test(timeout = 10000)
    fun serverCancellationClosesIdleClient() = withServer {
        connect(port).use { socket ->
            connected.receive()
            job.cancelAndJoin()
            socket.soTimeout = 3000
            assertEquals(-1, socket.getInputStream().read())
            disconnected.receive()
        }
    }

    @Test(timeout = 10000)
    fun serverCancellationUnblocksAccept() = withServer {
        job.cancelAndJoin()
        assertTrue(job.isCompleted)
    }

    private class Fixture(
        val port: Int,
        val connected: Channel<StreamSession>,
        val messages: Channel<SignalingMessage>,
        val disconnected: Channel<Unit>,
        val logs: List<String>,
        val job: kotlinx.coroutines.Job,
    )

    private fun withServer(
        onMessage: suspend (StreamSession, SignalingMessage) -> Unit = { session, message ->
            if (message is SignalingMessage.Offer) session.send(SignalingMessage.Answer("answer"))
        },
        block: suspend Fixture.() -> Unit,
    ) = runBlocking {
        val ready = CompletableDeferred<Int>()
        val logs = CopyOnWriteArrayList<String>()
        val server = ReceiverServer(0) { message, _ ->
            logs.add(message)
            if (message.startsWith("Listening on ")) ready.complete(message.substringAfterLast(' ').toInt())
        }
        val connected = Channel<StreamSession>(8)
        val messages = Channel<SignalingMessage>(16)
        val disconnected = Channel<Unit>(8)
        val job = launch(Dispatchers.IO) {
            server.run(object : ReceiverServer.Listener {
                override suspend fun onConnected(session: StreamSession) { connected.send(session) }
                override suspend fun onMessage(session: StreamSession, message: SignalingMessage) {
                    messages.send(message)
                    onMessage(session, message)
                }
                override suspend fun onDisconnected(session: StreamSession) { disconnected.send(Unit) }
            })
        }
        try {
            withTimeout(6000) {
                Fixture(ready.await(), connected, messages, disconnected, logs, job).block()
            }
        } finally {
            server.stop()
            job.cancelAndJoin()
        }
    }

    private fun Socket.send(message: SignalingMessage) {
        getOutputStream().write((SignalingCodec.encode(message) + "\n").toByteArray(Charsets.UTF_8))
        getOutputStream().flush()
    }

    private fun assertIdleConnection(socket: Socket) {
        val previousTimeout = socket.soTimeout
        socket.soTimeout = 150
        try {
            // A timeout proves TCP remains open; EOF would return -1 immediately.
            assertThrows(SocketTimeoutException::class.java) { socket.getInputStream().read() }
        } finally {
            socket.soTimeout = previousTimeout
        }
    }

    private suspend fun connect(port: Int): Socket = withTimeout(3000) {
        var result: Socket? = null
        while (result == null) {
            result = try { Socket("127.0.0.1", port) } catch (_: IOException) { delay(20); null }
        }
        result
    }
}
