package pl.screenreceiver.tv.projector

import android.content.Context
import android.util.Log
import java.net.URI

object ProjectorControllers {
    private fun preferences(context: Context) = context.getSharedPreferences("projector_control", Context.MODE_PRIVATE)
    fun address(context: Context): String = preferences(context).getString("address", "").orEmpty()

    fun saveAddress(context: Context, address: String) {
        if (address.isNotBlank()) endpoint(address)
        check(preferences(context).edit().putString("address", address.trim()).commit()) { "Could not save address" }
    }

    private fun endpoint(address: String): Pair<String, Int> {
        val uri = URI("tcp://${address.trim()}")
        require(!uri.host.isNullOrBlank() && uri.userInfo == null && uri.path.isNullOrEmpty() &&
            uri.query == null && uri.fragment == null && (uri.port == -1 || uri.port in 1..65535)) {
            "Enter a valid projector host or host:port"
        }
        return uri.host to if (uri.port == -1) 4661 else uri.port
    }

    fun create(context: Context): ProjectorController {
        val address = address(context)
        if (address.isBlank()) return LocalProjectorController(context)
        val transport = runCatching {
            val (host, port) = endpoint(address)
            TcpProjectorTransport(host, port)
        }.getOrNull()
        return ViewSonicProjectorController(transport) { Log.i("ProjectorControl", it) }
    }

    fun powerOffUnavailableReason(context: Context): String? =
        if (address(context).isBlank() && !LocalProjectorController(context).authorized) {
            "Authorize automatic projector power-off in the receiver's Projector Control settings first"
        } else null
}
