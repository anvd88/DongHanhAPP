package com.ketoanapk.hr.data

import androidx.test.core.app.ApplicationProvider
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Test

class ChatCacheStoreTest {
    @Test
    fun encryptedCacheRoundTripsConversationsAndRecentMessages() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val store = ChatCacheStore(context)
        store.clear()
        val conversation = ChatConversation(id = "c1", title = "Kế toán")
        val messages = (1L..120L).map { ChatMessage(id = it, body = "Tin $it") }

        store.saveConversations(listOf(conversation))
        store.saveMessages("c1", messages)
        val loaded = store.load()

        assertEquals(listOf(conversation), loaded.conversations)
        assertEquals(100, loaded.messages["c1"]?.size)
        assertEquals(21L, loaded.messages["c1"]?.first()?.id)
        store.clear()
    }
}
