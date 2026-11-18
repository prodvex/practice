package com.gamingclub.client

import org.junit.Assert.*
import org.junit.Test

class AuthAttemptGuardTest {
    @Test fun currentAttemptIsAccepted() {
        val guard = AuthAttemptGuard()
        val ticket = guard.begin()
        assertTrue(guard.isCurrent(ticket))
    }

    @Test fun newerAttemptInvalidatesPreviousOne() {
        val guard = AuthAttemptGuard()
        val first = guard.begin()
        val next = guard.begin()
        assertFalse(guard.isCurrent(first))
        assertTrue(guard.isCurrent(next))
    }

    @Test fun logoutInvalidatesPendingLogin() {
        val guard = AuthAttemptGuard()
        val ticket = guard.begin()
        guard.invalidate()
        assertFalse(guard.isCurrent(ticket))
    }

    @Test fun tokenCannotBeReactivatedAfterLogout() {
        val guard = AuthAttemptGuard()
        val stale = guard.begin()
        guard.invalidate()
        val fresh = guard.begin()
        assertFalse(guard.isCurrent(stale))
        assertTrue(guard.isCurrent(fresh))
    }
}
