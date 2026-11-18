package com.gamingclub.client

import java.time.Duration
import java.time.Instant
import java.time.temporal.ChronoUnit

/** Client-side validation only. The server still owns conflicts, prices and time rules. */
data class BookingDraft(
    val seatId: Int,
    val tariffId: Int,
    val startUtc: Instant,
    val endUtc: Instant
)

object BookingDraftValidator {
    fun parse(seatId: Int?, tariffId: Int?, startText: String, endText: String): BookingDraft? {
        if (seatId == null || seatId <= 0 || tariffId == null || tariffId <= 0) return null
        val start = runCatching { Instant.parse(startText.trim()) }.getOrNull() ?: return null
        val end = runCatching { Instant.parse(endText.trim()) }.getOrNull() ?: return null
        if (end <= start || Duration.between(start, end) > Duration.ofDays(7)) return null
        if (start.truncatedTo(ChronoUnit.MINUTES) != start ||
            end.truncatedTo(ChronoUnit.MINUTES) != end) return null
        return BookingDraft(seatId, tariffId, start, end)
    }
}
