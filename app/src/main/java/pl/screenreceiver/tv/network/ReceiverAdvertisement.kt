package pl.screenreceiver.tv.network

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.util.Base64

/** DNS-SD only; discovery never opens the signaling socket or creates a media peer. */
class ReceiverAdvertisement(context: Context) {
    private val context = context.applicationContext
    private val manager = context.getSystemService(NsdManager::class.java)
    private val handler = Handler(Looper.getMainLooper())
    private data class Description(val name: String, val port: Int, val busy: Boolean)
    private var desired: Description? = null
    private var advertised: Description? = null
    private var listener: NsdManager.RegistrationListener? = null
    private var registered = false
    private var unregistering = false
    private var closed = false

    fun update(listening: Boolean, name: String, port: Int, busy: Boolean) {
        handler.post {
            if (!closed) {
                desired = if (listening) Description(name, port, busy) else null
                reconcile()
            }
        }
    }

    fun close() {
        handler.post { closed = true; desired = null; reconcile() }
    }

    private fun retry() { handler.postDelayed({ reconcile() }, 5_000) }

    private fun reconcile() {
        if (listener != null) {
            if (registered && !unregistering && desired != advertised) {
                unregistering = true
                try { manager.unregisterService(listener) }
                catch (error: RuntimeException) {
                    unregistering = false
                    Log.w("ScreenReceiver", "Discovery withdrawal failed", error)
                    retry()
                }
            }
            return
        }
        val description = desired ?: return
        try {
            val id = ReceiverIdentity.id(context)
            val info = NsdServiceInfo().apply {
                serviceName = "ScreenReceiver-${id.take(8)}"
                serviceType = "_screenreceiver._tcp."
                port = description.port
                setAttribute("id", id)
                // The pinned Windows DNS parser only accepts ASCII TXT strings.
                setAttribute("name64", Base64.encodeToString(description.name.toByteArray(Charsets.UTF_8), Base64.NO_WRAP))
                setAttribute("protocol", "1")
                setAttribute("busy", if (description.busy) "1" else "0")
            }
            val registration = object : NsdManager.RegistrationListener {
                override fun onServiceRegistered(serviceInfo: NsdServiceInfo) { handler.post {
                    if (listener === this) { registered = true; reconcile() }
                } }
                override fun onRegistrationFailed(serviceInfo: NsdServiceInfo, errorCode: Int) { handler.post {
                    if (listener === this) {
                        listener = null; registered = false
                        Log.w("ScreenReceiver", "Discovery registration failed: $errorCode; streaming unchanged")
                        retry()
                    }
                } }
                override fun onServiceUnregistered(serviceInfo: NsdServiceInfo) { handler.post {
                    if (listener === this) {
                        listener = null; registered = false; unregistering = false
                        reconcile()
                    }
                } }
                override fun onUnregistrationFailed(serviceInfo: NsdServiceInfo, errorCode: Int) { handler.post {
                    if (listener === this) {
                        unregistering = false
                        Log.w("ScreenReceiver", "Discovery withdrawal failed: $errorCode")
                        retry()
                    }
                } }
            }
            advertised = description
            listener = registration
            manager.registerService(info, NsdManager.PROTOCOL_DNS_SD, registration)
        } catch (error: RuntimeException) {
            listener = null; registered = false
            Log.w("ScreenReceiver", "Discovery unavailable; manual connection remains available", error)
            retry()
        }
    }
}
