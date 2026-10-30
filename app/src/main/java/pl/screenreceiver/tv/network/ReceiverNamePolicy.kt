package pl.screenreceiver.tv.network

object ReceiverNamePolicy {
    fun validate(value: String): String {
        val name = value.trim()
        require(name.isNotEmpty()) { "Enter a receiver name" }
        require(name.codePointCount(0, name.length) <= 48 && name.toByteArray(Charsets.UTF_8).size <= 180) {
            "Use a shorter receiver name (up to 48 characters)"
        }
        require(name.none { it.isISOControl() }) { "Receiver names cannot contain control characters" }
        return name
    }
}
