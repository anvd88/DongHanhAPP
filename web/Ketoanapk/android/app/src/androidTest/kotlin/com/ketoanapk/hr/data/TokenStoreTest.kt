package com.ketoanapk.hr.data

import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class TokenStoreTest {
    private val store = TokenStore(ApplicationProvider.getApplicationContext())

    @After
    fun cleanUp() = runBlocking { store.clearToken() }

    @Test
    fun tokenRoundTripsThroughKeystoreAndCanBeCleared() = runBlocking {
        val token = "header.payload.signature"
        store.saveToken(token)
        assertEquals(token, store.token())

        store.clearToken()
        assertNull(store.token())
    }

    /** Hồ sơ đã lưu là thứ giúp mở app khi mất mạng — phải qua được Keystore và bị xoá khi đăng xuất. */
    @Test
    fun cachedUserRoundTripsAndIsClearedWithTheToken() = runBlocking {
        val user = HrUser(id = "1", username = "an", fullName = "Nguyễn Văn An", role = "admin")
        store.saveCachedUser(user)
        assertEquals(user, store.cachedUser())

        store.clearToken()
        assertNull(store.cachedUser())
    }

    @Test
    fun lastOnlineStampIsRecordedAndClearedWithTheToken() = runBlocking {
        val before = System.currentTimeMillis()
        store.touchOnline()
        assertTrue(store.lastOnlineAt() >= before)

        store.clearToken()
        assertEquals(0L, store.lastOnlineAt())
    }
}
