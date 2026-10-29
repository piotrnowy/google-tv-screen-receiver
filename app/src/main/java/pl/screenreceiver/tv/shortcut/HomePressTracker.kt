package pl.screenreceiver.tv.shortcut

/** Matches a HOME release to its original press without restarting the clock on repeats. */
internal class HomePressTracker {
    private var homeDownTime: Long? = null

    fun onDown(downTime: Long, repeatCount: Int) {
        if (repeatCount == 0) homeDownTime = downTime
    }

    fun onUp(downTime: Long, eventTime: Long): Long? {
        val startedAt = homeDownTime
        reset()
        return if (startedAt == downTime && eventTime >= downTime) {
            eventTime - downTime
        } else {
            null
        }
    }

    fun reset() {
        homeDownTime = null
    }

    companion object {
        const val LONG_PRESS_THRESHOLD_MS = 800L
    }
}
