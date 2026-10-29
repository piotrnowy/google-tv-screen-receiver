package pl.screenreceiver.tv.projector

import java.io.DataInputStream
import java.net.InetSocketAddress
import java.net.Socket
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** For a verified LAN endpoint or transparent TCP-to-RS232 bridge; never assume the TV IP is one. */
class TcpProjectorTransport(private val host: String, private val port: Int = 4661) : ProjectorTransport {
    init { require(host.isNotBlank() && port in 1..65535) }

    override suspend fun exchange(command: ByteArray): ByteArray = withContext(Dispatchers.IO) {
        Socket().use { socket ->
            socket.connect(InetSocketAddress(host, port), 2500)
            socket.soTimeout = 2500
            socket.getOutputStream().apply { write(command); flush() }
            val input = DataInputStream(socket.getInputStream())
            val header = ByteArray(5).also { input.readFully(it) }
            val length = (header[3].toInt() and 0xff) or ((header[4].toInt() and 0xff) shl 8)
            require(length in 0..32) { "Invalid projector response length" }
            header + ByteArray(length + 1).also { input.readFully(it) }
        }
    }
}
