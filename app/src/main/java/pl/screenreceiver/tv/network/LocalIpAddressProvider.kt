package pl.screenreceiver.tv.network

import java.net.Inet4Address
import java.net.InetAddress
import java.net.NetworkInterface
import java.net.SocketException
import java.util.Collections

class LocalIpAddressProvider {
    fun getLocalIpv4Address(): String? = try {
        val interfaces = NetworkInterface.getNetworkInterfaces()
        val addresses = if (interfaces == null) {
            emptyList()
        } else {
            Collections.list(interfaces)
                .filter { it.isUp && !it.isLoopback }
                .flatMap { Collections.list(it.inetAddresses) }
        }
        selectLocalIpv4Address(addresses)
    } catch (_: SocketException) {
        null
    } catch (_: SecurityException) {
        null
    }
}

// RFC 1918 addresses take priority over other usable IPv4 addresses.
internal fun selectLocalIpv4Address(addresses: List<InetAddress>): String? = addresses
    .filterIsInstance<Inet4Address>()
    .filterNot {
        it.isLoopbackAddress || it.isLinkLocalAddress ||
            it.isAnyLocalAddress || it.isMulticastAddress
    }
    .sortedByDescending { it.isSiteLocalAddress }
    .firstOrNull()
    ?.hostAddress
