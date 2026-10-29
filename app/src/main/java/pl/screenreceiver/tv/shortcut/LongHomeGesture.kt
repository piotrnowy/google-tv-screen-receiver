package pl.screenreceiver.tv.shortcut

/** Pure gesture state. The initial DOWN and every short press always pass through. */
internal class LongHomeGesture(private val activate: () -> Boolean) {
    private var downTime: Long? = null
    private var attempted = false
    private var active = false

    fun down(time: Long, repeatCount: Int, eventTime: Long): Boolean {
        if (repeatCount == 0) {
            downTime = time
            attempted = false
            active = false
            return false
        }
        if (downTime == time) tick(eventTime)
        return active
    }

    fun tick(now: Long) {
        val start = downTime ?: return
        if (!attempted && now - start >= THRESHOLD_MS) {
            attempted = true
            active = activate()
        }
    }

    fun up(time: Long, eventTime: Long, canceled: Boolean): Boolean {
        if (downTime != time) return false
        if (!canceled) tick(eventTime)
        val consume = active
        reset()
        return consume
    }

    fun reset() {
        downTime = null
        attempted = false
        active = false
    }

    companion object { const val THRESHOLD_MS = 800L }
}
