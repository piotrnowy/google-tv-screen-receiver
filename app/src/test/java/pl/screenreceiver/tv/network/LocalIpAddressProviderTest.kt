package pl.screenreceiver.tv.network

import java.net.InetAddress
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class LocalIpAddressProviderTest {
    private fun select(vararg addresses: String): String? =
        selectLocalIpv4Address(addresses.map { InetAddress.getByName(it) })

    @Test
    fun prefersPrivateRangesOverPublicAddresses() {
        for (privateAddress in listOf("192.168.1.42", "10.0.0.2", "172.16.0.1", "172.31.255.254")) {
            assertEquals(privateAddress, select("8.8.8.8", privateAddress))
        }
    }

    @Test
    fun doesNotTreatAddressesOutsideThe172PrivateRangeAsPrivate() {
        assertEquals("192.168.0.2", select("172.15.255.254", "172.32.0.1", "192.168.0.2"))
    }

    @Test
    fun rejectsLoopbackIpv6LinkLocalUnspecifiedAndMulticast() {
        assertNull(select("127.0.0.1", "127.1.2.3", "::1", "fd00::1", "169.254.1.1", "0.0.0.0", "224.0.0.1"))
    }

    @Test
    fun returnsNullWhenNoAddressIsAvailable() {
        assertNull(select())
    }

    @Test
    fun usesOtherUsableIpv4WhenThereIsNoPrivateAddress() {
        assertEquals("100.64.0.2", select("::1", "100.64.0.2"))
    }
}
