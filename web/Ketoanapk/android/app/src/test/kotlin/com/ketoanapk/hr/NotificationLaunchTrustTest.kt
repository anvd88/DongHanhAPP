package com.ketoanapk.hr

import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class NotificationLaunchTrustTest {
    @Test
    fun tokenIsOpaqueAndCanBeConsumedOnlyOnce() {
        val token = NotificationLaunchTrust.issue()

        assertTrue(token.isNotBlank())
        assertTrue(NotificationLaunchTrust.consume(token))
        assertFalse(NotificationLaunchTrust.consume(token))
        assertFalse(NotificationLaunchTrust.consume(null))
        assertFalse(NotificationLaunchTrust.consume("attacker-controlled-marker"))
    }

    @Test
    fun concurrentNotificationEntriesReceiveIndependentTokens() {
        val first = NotificationLaunchTrust.issue()
        val second = NotificationLaunchTrust.issue()

        assertNotEquals(first, second)
        assertTrue(NotificationLaunchTrust.consume(second))
        assertTrue(NotificationLaunchTrust.consume(first))
    }
}
