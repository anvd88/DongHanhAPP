package com.ketoanapk.hr.data

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Quy tắc "quá 7 ngày không mở app (có mạng) thì phải đăng nhập lại". */
class SessionIdleTest {
    private val day = 24L * 60 * 60 * 1000
    private val now = 1_800_000_000_000L // mốc giả định, không phụ thuộc giờ chạy test

    @Test
    fun `vua cham may chu thi con han`() {
        assertFalse(sessionIdleExpired(now - day, now))
    }

    @Test
    fun `dung mốc 7 ngày vẫn còn hạn`() {
        assertFalse(sessionIdleExpired(now - SESSION_IDLE_DAYS * day, now))
    }

    @Test
    fun `quá 7 ngày thì hết hạn`() {
        assertTrue(sessionIdleExpired(now - SESSION_IDLE_DAYS * day - 1, now))
        assertTrue(sessionIdleExpired(now - 30 * day, now))
    }

    /** Chưa có mốc (bản app cũ nâng cấp lên) → không được đá người dùng ra. */
    @Test
    fun `chua co moc thi khong het han`() {
        assertFalse(sessionIdleExpired(0L, now))
    }

    /** Người dùng chỉnh lại đồng hồ máy về quá khứ → không đá nhầm. */
    @Test
    fun `dong ho chay lui thi khong het han`() {
        assertFalse(sessionIdleExpired(now + 30 * day, now))
    }
}
