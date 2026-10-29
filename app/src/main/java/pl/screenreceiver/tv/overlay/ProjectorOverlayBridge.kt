package pl.screenreceiver.tv.overlay

/** Main-thread bridge installed/removed with the system-bound accessibility service. */
object ProjectorOverlayBridge {
    var toggle: (() -> Boolean)? = null
        internal set
    var showStream: (() -> Unit)? = null
        internal set
    var close: (() -> Unit)? = null
        internal set
    fun openOrClose(): Boolean = toggle?.invoke() ?: false
}
