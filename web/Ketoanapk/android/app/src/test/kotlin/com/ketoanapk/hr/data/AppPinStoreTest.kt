package com.ketoanapk.hr.data

import java.security.MessageDigest
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class AppPinStoreTest {
    @Test
    fun pinMustContainExactlySixDigits() {
        assertTrue(AppPinHasher.isValid("012345"))
        assertFalse(AppPinHasher.isValid("12345"))
        assertFalse(AppPinHasher.isValid("1234567"))
        assertFalse(AppPinHasher.isValid("12a456"))
    }

    @Test
    fun hashIsStableForSamePinAndSaltAndChangesForDifferentPin() {
        val salt = ByteArray(16) { it.toByte() }
        val expected = AppPinHasher.derive("123456", salt)
        assertTrue(MessageDigest.isEqual(expected, AppPinHasher.derive("123456", salt)))
        assertFalse(MessageDigest.isEqual(expected, AppPinHasher.derive("654321", salt)))
    }

    @Test
    fun repeatedFailuresUseEscalatingLockouts() {
        assertTrue(AppPinHasher.lockDurationMillis(4) == 0L)
        assertTrue(AppPinHasher.lockDurationMillis(5) == 30_000L)
        assertTrue(AppPinHasher.lockDurationMillis(10) == 5 * 60_000L)
        assertTrue(AppPinHasher.lockDurationMillis(15) == 30 * 60_000L)
    }
}
