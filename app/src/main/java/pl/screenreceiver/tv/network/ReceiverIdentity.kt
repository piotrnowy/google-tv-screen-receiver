package pl.screenreceiver.tv.network

import android.content.Context
import android.os.Build
import android.provider.Settings
import java.util.UUID

object ReceiverIdentity {
    private fun prefs(context: Context) = context.getSharedPreferences("receiver_identity", Context.MODE_PRIVATE)

    fun name(context: Context): String {
        val saved = prefs(context).getString("name", null)
        val system = runCatching { Settings.Global.getString(context.contentResolver, "device_name") }.getOrNull()
        return listOfNotNull(saved, system, Build.MODEL, "Google TV").firstNotNullOf {
            runCatching { ReceiverNamePolicy.validate(it) }.getOrNull()
        }
    }

    fun saveName(context: Context, value: String): String {
        val name = ReceiverNamePolicy.validate(value)
        check(prefs(context).edit().putString("name", name).commit()) { "Could not save receiver name" }
        return name
    }

    @Synchronized
    fun id(context: Context): String {
        prefs(context).getString("id", null)?.let { return it }
        val id = UUID.randomUUID().toString()
        check(prefs(context).edit().putString("id", id).commit()) { "Could not save receiver identity" }
        return id
    }
}
