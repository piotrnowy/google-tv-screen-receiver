package pl.screenreceiver.tv.shortcut

/** Keeps every existing service when adding our full or abbreviated component name. */
internal object ShortcutServiceSelection {
    const val COMPONENT = "pl.screenreceiver.tv/pl.screenreceiver.tv.shortcut.ProjectorShortcutAccessibilityService"
    private const val SHORT_COMPONENT = "pl.screenreceiver.tv/.shortcut.ProjectorShortcutAccessibilityService"

    fun containsProjector(services: String?): Boolean = services.orEmpty().split(':').any {
        it == COMPONENT || it == SHORT_COMPONENT
    }

    fun includeProjector(services: String?): String {
        if (containsProjector(services)) return services.orEmpty()
        val existing = services.orEmpty().split(':').filter { it.isNotBlank() && it != "null" }
        return (existing + COMPONENT).joinToString(":")
    }
}
