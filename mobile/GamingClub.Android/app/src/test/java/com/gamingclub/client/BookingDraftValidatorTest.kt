package com.gamingclub.client

import org.junit.Assert.*
import org.junit.Test
import java.time.Duration
import java.time.Instant

class BookingDraftValidatorTest {
    private val start = Instant.parse("2030-01-05T12:00:00Z")
    private val end = start.plus(Duration.ofHours(1))

    @Test fun validHourlyWindow() {
        val draft = BookingDraftValidator.parse(1, 2, start.toString(), end.toString())
        assertNotNull(draft)
        assertEquals(1, draft!!.seatId)
        assertEquals(2, draft.tariffId)
    }

    @Test fun missingSeatIsRejected() {
        assertNull(BookingDraftValidator.parse(null, 1, start.toString(), end.toString()))
    }

    @Test fun invalidSeatIdIsRejected() {
        assertNull(BookingDraftValidator.parse(0, 1, start.toString(), end.toString()))
    }

    @Test fun missingTariffIsRejected() {
        assertNull(BookingDraftValidator.parse(1, null, start.toString(), end.toString()))
    }

    @Test fun negativeDurationIsRejected() {
        assertNull(BookingDraftValidator.parse(1, 2, end.toString(), start.toString()))
    }

    @Test fun zeroDurationIsRejected() {
        assertNull(BookingDraftValidator.parse(1, 2, start.toString(), start.toString()))
    }

    @Test fun moreThanSevenDaysIsRejected() {
        assertNull(BookingDraftValidator.parse(1, 2, start.toString(),
            start.plus(Duration.ofDays(7)).plusSeconds(60).toString()))
    }

    @Test fun exactlySevenDaysIsAccepted() {
        assertNotNull(BookingDraftValidator.parse(1, 2, start.toString(),
            start.plus(Duration.ofDays(7)).toString()))
    }

    @Test fun subMinuteStartIsRejected() {
        assertNull(BookingDraftValidator.parse(1, 2, start.plusNanos(1).toString(), end.toString()))
    }

    @Test fun subMinuteEndIsRejected() {
        assertNull(BookingDraftValidator.parse(1, 2, start.toString(), end.plusSeconds(1).toString()))
    }

    @Test fun malformedTimestampIsRejected() {
        assertNull(BookingDraftValidator.parse(1, 2, "tomorrow at noon", end.toString()))
    }

    @Test fun surroundingWhitespaceIsIgnored() {
        assertNotNull(BookingDraftValidator.parse(1, 2,
            "  ${start}  ", "  ${end} "))
    }
}
