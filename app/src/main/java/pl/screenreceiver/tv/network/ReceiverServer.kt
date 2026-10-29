package pl.screenreceiver.tv.network

import android.util.Log
import java.io.BufferedReader
import java.io.IOException
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.util.concurrent.atomic.AtomicBoolean
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** One active sender. Closing sockets unblocks accept/read/write during lifecycle shutdown. */
class ReceiverServer internal constructor(
    private val port: Int,
    private val log: (String, Throwable?) -> Unit,
) {
    constructor(port: Int = 53530) : this(port, { message, error ->
        if (error == null) Log.d(TAG, message) else Log.e(TAG, message, error)
    })

    companion object {
        private const val TAG = "ReceiverServer"
    }
    interface Listener {
        fun onListening(port: Int) = Unit
        suspend fun onConnected(session: StreamSession)
        suspend fun onMessage(session: StreamSession, message: SignalingMessage)
        suspend fun onDisconnected(session: StreamSession)
    }

    private val lock = Any()
    private var stopped = false
    private var server: ServerSocket? = null
    private var client: Socket? = null

    suspend fun run(listener: Listener) = withContext(Dispatchers.IO) {
        val socket = ServerSocket()
        // Cancellation cannot interrupt Java accept/read by itself. Close both sockets
        // from another IO coroutine so a cancelled server can finish its session finally.
        val cancellationWatcher = launch(start = CoroutineStart.UNDISPATCHED) {
            try {
                awaitCancellation()
            } finally {
                withContext(NonCancellable) { stop() }
            }
        }
        try {
            synchronized(lock) {
                if (stopped) return@withContext
                server = socket
                socket.reuseAddress = true
                socket.bind(InetSocketAddress(port), 1)
            }
            log("Listening on ${socket.localPort}", null)
            listener.onListening(socket.localPort)
            while (currentCoroutineContext().isActive && !isStopped()) {
                val accepted = try {
                    socket.accept()
                } catch (error: IOException) {
                    if (isStopped()) break
                    log("Exception: accepting client", error)
                    throw error
                }
                val useClient = synchronized(lock) {
                    if (stopped) false else {
                        client = accepted
                        true
                    }
                }
                if (!useClient) {
                    accepted.close()
                    break
                }
                log("Client accepted: ${accepted.inetAddress.hostAddress}", null)
                try {
                    serveClient(accepted, listener)
                } finally {
                    synchronized(lock) { client = null }
                }
            }
        } finally {
            withContext(NonCancellable) {
                cancellationWatcher.cancelAndJoin()
                socket.close()
            }
        }
    }

    suspend fun stop() = withContext(Dispatchers.IO) {
        synchronized(lock) {
            stopped = true
            log("Stopping server", null)
            try { client?.close() } catch (error: IOException) {
                log("Exception: closing active client during shutdown", error)
            }
            try { server?.close() } catch (error: IOException) {
                log("Exception: closing listener during shutdown", error)
            }
        }
    }

    private fun isStopped(): Boolean = synchronized(lock) { stopped }

    private suspend fun serveClient(socket: Socket, listener: Listener) = coroutineScope {
        val session = TcpStreamSession(socket, log)
        var connected = false
        val writer = launch(Dispatchers.IO) {
            try {
                val output = socket.getOutputStream().bufferedWriter(Charsets.UTF_8)
                for (message in session.outgoing) {
                    log("Sending: ${message.typeName()}", null)
                    output.write(SignalingCodec.encode(message))
                    output.write("\n")
                    output.flush()
                }
            } catch (error: IOException) {
                log("Exception: writing client signaling", error)
                session.close()
            }
        }
        try {
            socket.tcpNoDelay = true
            socket.keepAlive = true
            connected = true
            listener.onConnected(session)
            val input = socket.getInputStream().bufferedReader(Charsets.UTF_8)
            while (currentCoroutineContext().isActive && !socket.isClosed) {
                val line = input.readBoundedLine()
                if (line == null) {
                    log("Client EOF", null)
                    break
                }
                val message = SignalingCodec.parse(line)
                if (message == null) {
                    log("Ignoring malformed or unsupported signaling message", null)
                    continue
                }
                log("Received: ${message.typeName()}", null)
                if (message == SignalingMessage.Bye) break
                try {
                    listener.onMessage(session, message)
                } catch (cancelled: CancellationException) {
                    throw cancelled
                } catch (error: Exception) {
                    // A bad message/SDP/ICE must not end the TCP session or accept loop.
                    log("Exception: handling ${message.typeName()}; continuing session", error)
                }
            }
        } catch (error: IOException) {
            log("Exception: reading client signaling (stopping=${isStopped()}, closed=${socket.isClosed})", error)
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (error: Exception) {
            log("Exception: initializing client session", error)
        } finally {
            withContext(NonCancellable) {
                session.close()
                writer.cancelAndJoin()
                if (connected) {
                    try {
                        listener.onDisconnected(session)
                    } catch (error: Exception) {
                        log("Exception: cleaning up client session", error)
                    }
                }
            }
        }
    }
}

private class TcpStreamSession(
    private val socket: Socket,
    private val log: (String, Throwable?) -> Unit,
) : StreamSession {
    val outgoing = Channel<SignalingMessage>(64)
    private val closed = AtomicBoolean(false)

    override suspend fun send(message: SignalingMessage) {
        outgoing.send(message)
    }

    override suspend fun close() = withContext(Dispatchers.IO) {
        if (!closed.compareAndSet(false, true)) return@withContext
        outgoing.close()
        try {
            socket.close()
            log("Client socket closed", null)
        } catch (error: IOException) {
            log("Exception: closing client socket", error)
        }
        Unit
    }
}

private fun SignalingMessage.typeName(): String = when (this) {
    is SignalingMessage.Hello -> "hello"
    is SignalingMessage.Offer -> "offer"
    is SignalingMessage.Answer -> "answer"
    is SignalingMessage.Ice -> "ice"
    SignalingMessage.Bye -> "bye"
    is SignalingMessage.Volume -> "volume"
    is SignalingMessage.Mute -> "mute"
    is SignalingMessage.VolumeAction -> "volume"
    SignalingMessage.GetVolume -> "getVolume"
    is SignalingMessage.VolumeState -> "volumeState"
}

/** Avoid BufferedReader.readLine's unbounded allocation for a broken sender. */
internal fun BufferedReader.readBoundedLine(): String? {
    val line = StringBuilder()
    while (true) {
        val char = read()
        if (char == -1) return null // A message is only complete with its newline delimiter.
        if (char == '\n'.code) return line.toString().removeSuffix("\r")
        if (line.length >= SignalingCodec.MAX_LINE_CHARS) throw IOException("Signaling line too large")
        line.append(char.toChar())
    }
}
