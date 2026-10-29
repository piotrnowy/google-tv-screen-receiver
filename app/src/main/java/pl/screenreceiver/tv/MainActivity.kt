package pl.screenreceiver.tv

import android.os.Bundle
import android.Manifest
import android.content.ActivityNotFoundException
import android.content.ComponentName
import android.content.Intent
import android.content.pm.PackageManager
import android.database.ContentObserver
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.net.Uri
import android.provider.Settings
import android.util.Log
import android.view.WindowManager
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.compose.setContent
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import pl.screenreceiver.tv.model.ReceiverStatus
import pl.screenreceiver.tv.shortcut.ProjectorShortcutAccessibilityService
import pl.screenreceiver.tv.shortcut.ShortcutDiagnostics
import pl.screenreceiver.tv.shortcut.ShortcutStartupRecovery
import pl.screenreceiver.tv.ui.screens.ReceiverScreen
import pl.screenreceiver.tv.ui.screens.ReceiverVideo
import pl.screenreceiver.tv.ui.theme.ScreenReceiverTheme
import pl.screenreceiver.tv.viewmodel.ReceiverViewModel
import pl.screenreceiver.tv.timer.SleepTimerManager

class MainActivity : ComponentActivity() {
    private var shortcutServiceEnabled by mutableStateOf(false)
    private var shortcutRecoveryAvailable by mutableStateOf(false)
    private var shortcutRecoveryEnabled by mutableStateOf(false)
    private var timerAllowed by mutableStateOf(false)
    private var notificationsAllowed by mutableStateOf(false)
    private val notificationPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        notificationsAllowed = granted
    }
    private val shortcutSettingsObserver = object : ContentObserver(Handler(Looper.getMainLooper())) {
        override fun onChange(selfChange: Boolean) {
            refreshShortcutServiceStatus()
        }
    }
    private var localNetworkPermissionRequested = false
    private val localNetworkPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) {
            if (lifecycle.currentState.isAtLeast(Lifecycle.State.STARTED)) receiverViewModel.start()
        } else {
            receiverViewModel.localNetworkPermissionDenied()
        }
    }

    private val receiverViewModel by lazy {
        ViewModelProvider(this)[ReceiverViewModel::class.java]
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        localNetworkPermissionRequested = savedInstanceState?.getBoolean("localNetworkPermissionRequested") ?: false
        WindowCompat.setDecorFitsSystemWindows(window, false)
        WindowInsetsControllerCompat(window, window.decorView).apply {
            hide(WindowInsetsCompat.Type.systemBars())
            systemBarsBehavior =
                WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        }
        setContent {
            ScreenReceiverTheme {
                val state by receiverViewModel.uiState.collectAsStateWithLifecycle()
                val video by receiverViewModel.videoOutput.collectAsStateWithLifecycle()
                val output = video
                if (state.receiverStatus == ReceiverStatus.STREAMING && output != null) {
                    ReceiverVideo(output)
                } else {
                    val diagnostic by ShortcutDiagnostics.state.collectAsStateWithLifecycle()
                    ReceiverScreen(
                        state = state,
                        shortcutDiagnostic = diagnostic,
                        shortcutServiceEnabled = shortcutServiceEnabled,
                        onOpenAccessibilitySettings = ::openAccessibilitySettings,
                        shortcutRecoveryAvailable = shortcutRecoveryAvailable,
                        shortcutRecoveryEnabled = shortcutRecoveryEnabled,
                        onToggleShortcutRecovery = ::toggleShortcutRecovery,
                        timerAllowed = timerAllowed,
                        onOpenTimerSettings = ::openTimerSettings,
                        notificationsAllowed = notificationsAllowed,
                        onRequestNotifications = {
                            if (Build.VERSION.SDK_INT >= 33) notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
                        },
                    )
                }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        refreshShortcutServiceStatus()
        timerAllowed = SleepTimerManager(this).canSchedule()
        notificationsAllowed = Build.VERSION.SDK_INT < 33 || checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED
        SleepTimerManager(this).restore()
    }

    private fun refreshShortcutServiceStatus() {
        shortcutRecoveryAvailable = ShortcutStartupRecovery.available(this)
        shortcutRecoveryEnabled = ShortcutStartupRecovery.enabled(this)
        val component = ComponentName(this, ProjectorShortcutAccessibilityService::class.java)
        // Read the user's enabled setting, without waiting for asynchronous service binding.
        val enabledServices = Settings.Secure.getString(
            contentResolver, Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES,
        ).orEmpty()
        shortcutServiceEnabled = Settings.Secure.getInt(
            contentResolver, Settings.Secure.ACCESSIBILITY_ENABLED, 0,
        ) == 1 && enabledServices.split(':').any {
            ComponentName.unflattenFromString(it) == component
        }
        Log.i(
            "ProjectorShortcut",
            "[ProjectorShortcut] Accessibility service status: " +
                if (shortcutServiceEnabled) "ENABLED" else "DISABLED",
        )
    }

    private fun toggleShortcutRecovery() {
        if (!shortcutRecoveryAvailable) {
            Toast.makeText(this, R.string.shortcut_recovery_setup_hint, Toast.LENGTH_LONG).show()
            return
        }
        runCatching { ShortcutStartupRecovery.setEnabled(this, !shortcutRecoveryEnabled) }
            .onFailure { Toast.makeText(this, R.string.shortcut_recovery_save_failed, Toast.LENGTH_LONG).show() }
        refreshShortcutServiceStatus()
    }

    private fun openAccessibilitySettings() {
        try {
            startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))
        } catch (_: ActivityNotFoundException) {
            Toast.makeText(this, R.string.shortcut_settings_unavailable, Toast.LENGTH_LONG).show()
        }
    }

    private fun openTimerSettings() {
        if (Build.VERSION.SDK_INT < 31) return
        try {
            startActivity(Intent(Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM, Uri.parse("package:$packageName")))
        } catch (_: ActivityNotFoundException) {
            Toast.makeText(this, "Open Settings → Apps → Special app access → Alarms & reminders", Toast.LENGTH_LONG).show()
        }
    }

    override fun onStart() {
        super.onStart()
        listOf(
            Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES,
            Settings.Secure.ACCESSIBILITY_ENABLED,
        ).forEach { setting ->
            contentResolver.registerContentObserver(
                Settings.Secure.getUriFor(setting), false, shortcutSettingsObserver,
            )
        }
        refreshShortcutServiceStatus()
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        if (Build.VERSION.SDK_INT < 37 ||
            checkSelfPermission(Manifest.permission.ACCESS_LOCAL_NETWORK) == PackageManager.PERMISSION_GRANTED
        ) {
            receiverViewModel.start()
        } else if (!localNetworkPermissionRequested) {
            localNetworkPermissionRequested = true
            localNetworkPermission.launch(Manifest.permission.ACCESS_LOCAL_NETWORK)
        } else {
            receiverViewModel.localNetworkPermissionDenied()
        }
    }

    override fun onSaveInstanceState(outState: Bundle) {
        outState.putBoolean("localNetworkPermissionRequested", localNetworkPermissionRequested)
        super.onSaveInstanceState(outState)
    }

    override fun onStop() {
        contentResolver.unregisterContentObserver(shortcutSettingsObserver)
        window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        super.onStop()
    }
}
