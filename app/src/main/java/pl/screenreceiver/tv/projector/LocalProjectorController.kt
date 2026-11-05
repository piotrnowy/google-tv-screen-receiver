package pl.screenreceiver.tv.projector

import android.content.Context
import android.os.Build
import dadb.AdbKeyPair
import dadb.Dadb
import java.io.File
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import okio.source

/** Connects only to this Google TV's authorized, local ADB daemon. No computer is needed at expiry. */
class LocalProjectorController(context: Context) : ProjectorController {
    private val context = context.applicationContext
    private val prefs get() = context.getSharedPreferences("projector_control", Context.MODE_PRIVATE)
    private val keyDirectory get() = File(context.noBackupFilesDir, "projector_power")
    val authorized get() = prefs.getBoolean("local_authorized", false) && File(keyDirectory, "adbkey").exists()

    suspend fun authorize(): ProjectorCommandResult {
        val result = execute(powerOff = false, setup = true)
        check(prefs.edit().putBoolean("local_authorized", result.accepted).commit())
        return result
    }

    suspend fun forget() = withContext(Dispatchers.IO) {
        gate.withLock {
            check(prefs.edit().putBoolean("local_authorized", false).commit())
            File(keyDirectory, "adbkey").delete()
            File(keyDirectory, "adbkey.pub").delete()
        }
    }

    override suspend fun setLightSource(percent: Int) =
        ProjectorCommandResult(false, false, "Automatic power-off does not control projector light-source brightness")

    override suspend fun powerOff(): ProjectorCommandResult {
        if (!authorized) return ProjectorCommandResult(false, false, "Authorize automatic projector power-off in receiver settings first")
        return execute(powerOff = true, setup = false)
    }

    private suspend fun execute(powerOff: Boolean, setup: Boolean): ProjectorCommandResult = withContext(Dispatchers.IO) {
        // An alarm receiver has a short execution budget; never queue behind a permission dialog.
        val locked = if (setup) { gate.lock(); true } else gate.tryLock()
        if (!locked) return@withContext ProjectorCommandResult(false, false, "Power-control setup is in progress; projector standby was not sent")
        try {
            try {
                check(Build.VERSION.SDK_INT >= 26) { "Automatic projector power-off requires Android 8 or newer" }
                val privateKey = File(keyDirectory, "adbkey")
                val publicKey = File(keyDirectory, "adbkey.pub")
                if (!privateKey.exists()) {
                    check(setup) { "Power-control authorization was removed; authorize it again in receiver settings" }
                    AdbKeyPair.generate(privateKey, publicKey)
                }
                val key = AdbKeyPair.read(privateKey, publicKey)
                Dadb.create("127.0.0.1", 5555, key, connectTimeout = 1500, socketTimeout = if (setup) 30000 else 3500).use { adb ->
                    context.assets.open("projector-power.jar").use { input ->
                        adb.push(input.source(), LocalPowerCommand.remoteHelper, 384, System.currentTimeMillis())
                    }
                    val response = adb.shell(LocalPowerCommand.build(powerOff))
                    LocalPowerCommand.result(response.exitCode, response.allOutput, powerOff)
                }
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                ProjectorCommandResult(false, false, "Projector control failed: ${error.message ?: error.javaClass.simpleName}. Enable network debugging on Google TV and authorize power control in receiver settings.")
            }
        } finally { gate.unlock() }
    }

    companion object { private val gate = Mutex() }
}
