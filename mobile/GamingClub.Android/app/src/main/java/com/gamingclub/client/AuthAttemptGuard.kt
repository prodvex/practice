package com.gamingclub.client

/**
 * UI-thread-only sequence guard. A stale login/register response must never
 * reactivate an account after logout or overwrite a newer login attempt.
 */
class AuthAttemptGuard {
    private var generation = 0L

    fun begin(): Long = ++generation

    fun invalidate() {
        generation++
    }

    fun isCurrent(ticket: Long): Boolean = ticket == generation
}
