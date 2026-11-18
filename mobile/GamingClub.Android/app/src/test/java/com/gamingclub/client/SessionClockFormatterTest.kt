package com.gamingclub.client

import org.junit.Assert.assertEquals
import org.junit.Test

class SessionClockFormatterTest {
    @Test fun zero() = assertEquals("00:00:00", SessionClockFormatter.format(0))
    @Test fun negativeBecomesZero() = assertEquals("00:00:00", SessionClockFormatter.format(-10))
    @Test fun singleSecond() = assertEquals("00:00:01", SessionClockFormatter.format(1))
    @Test fun exactMinute() = assertEquals("00:01:00", SessionClockFormatter.format(60))
    @Test fun hourAndMinutes() = assertEquals("01:24:36", SessionClockFormatter.format(5076))
    @Test fun longerThanDay() = assertEquals("27:01:01", SessionClockFormatter.format(97261))
}
