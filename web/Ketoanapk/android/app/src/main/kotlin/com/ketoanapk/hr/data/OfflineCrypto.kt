package com.ketoanapk.hr.data

import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * Mã hoá tại chỗ cho hàng đợi chấm công ngoại tuyến (chứa ảnh khuôn mặt) bằng AES-256-GCM với khóa
 * nằm trong Android Keystore (không rời khỏi thiết bị, ứng dụng khác không lấy được). Định dạng gói:
 * [IV 12 byte] + [ciphertext + tag GCM].
 */
object OfflineCrypto {
    private const val KEY_ALIAS = "km_offline_attendance_key"
    private const val TRANSFORM = "AES/GCM/NoPadding"
    private const val IV_LEN = 12
    private const val TAG_BITS = 128

    private fun secretKey(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getEntry(KEY_ALIAS, null) as? KeyStore.SecretKeyEntry)?.let { return it.secretKey }
        val kg = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        kg.init(
            KeyGenParameterSpec.Builder(
                KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
            )
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build(),
        )
        return kg.generateKey()
    }

    fun encrypt(plain: ByteArray): ByteArray {
        val cipher = Cipher.getInstance(TRANSFORM)
        cipher.init(Cipher.ENCRYPT_MODE, secretKey())
        val iv = cipher.iv
        val ct = cipher.doFinal(plain)
        return iv + ct
    }

    fun decrypt(packed: ByteArray): ByteArray {
        require(packed.size > IV_LEN) { "Dữ liệu mã hoá không hợp lệ." }
        val cipher = Cipher.getInstance(TRANSFORM)
        val iv = packed.copyOfRange(0, IV_LEN)
        val ct = packed.copyOfRange(IV_LEN, packed.size)
        cipher.init(Cipher.DECRYPT_MODE, secretKey(), GCMParameterSpec(TAG_BITS, iv))
        return cipher.doFinal(ct)
    }
}
