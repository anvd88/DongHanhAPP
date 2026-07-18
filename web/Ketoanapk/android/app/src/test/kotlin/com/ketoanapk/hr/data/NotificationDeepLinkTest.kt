package com.ketoanapk.hr.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class NotificationDeepLinkTest {
    @Test
    fun extractsRequestIdFromRequestAndInboxNotifications() {
        assertEquals("request-123", requestIdFromNotificationId("req:request-123:approved"))
        assertEquals("request-456", requestIdFromNotificationId("inbox:request-456"))
    }

    @Test
    fun ignoresUnrelatedOrMalformedNotifications() {
        assertNull(requestIdFromNotificationId("pen:123"))
        assertNull(requestIdFromNotificationId("inbox:"))
        assertNull(requestIdFromNotificationId(""))
    }

    @Test
    fun extractsConversationIdFromChatNotification() {
        assertEquals("conversation-1", entityIdFromNotificationId("chat:conversation-1:42"))
    }
}
