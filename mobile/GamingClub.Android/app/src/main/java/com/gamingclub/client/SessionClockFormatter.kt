package com.gamingclub.client

import java.util.Locale

/** Pure formatter for authoritative remainingSeconds returned by GamingClub.API. */
object SessionClockFormatter {
    fun format(secondsRemaining: Int): String {
        val safe = secondsRemaining.coerceAtLeast(0)
        val hours = safe / 3600
        val minutes = (safe % 3600) / 60
        val seconds = safe % 60
        return String.format(Locale.ROOT, "%02d:%02d:%02d", hours, minutes, seconds)
    }
}
