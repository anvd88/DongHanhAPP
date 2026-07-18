package com.ketoanapk.hr.ui

import com.ketoanapk.hr.data.QrActionEnvelope
import com.ketoanapk.hr.data.QrClientAction
import com.ketoanapk.hr.data.QrPresentation
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class QrActionInterpreterTest {
    @Test
    fun confirmationIsGenericAndRequiresDecisionToken() {
        val valid = QrActionEnvelope(
            protocolVersion = 1,
            presentation = QrPresentation("Xác nhận", "Nội dung từ server"),
            decisionToken = "opaque",
            actions = listOf(
                QrClientAction("yes", QrActionTypes.ServerDecision, "Đồng ý", "primary"),
                QrClientAction("no", QrActionTypes.ServerDecision, "Từ chối", "danger", closeOnSelect = true),
            ),
            dismissActionId = "no",
        )
        val dialog = QrActionInterpreter.interpret(valid) as QrUiEnvelope.Dialog
        assertEquals("opaque", dialog.decisionToken)
        assertEquals(listOf("yes", "no"), dialog.actions.map { it.id })
        assertEquals("no", dialog.dismissActionId)
        // Máy chủ không có đường nào nhét nội dung vào bảng nháy: chỉ nhánh app tự đọc mã mới đặt copyText.
        assertNull(dialog.copyText)

        val unsafeDismiss = QrActionInterpreter.interpret(valid.copy(dismissActionId = "yes")) as QrUiEnvelope.Dialog
        assertNull(unsafeDismiss.dismissActionId)

        val missingToken = valid.copy(decisionToken = null)
        assertTrue(QrActionInterpreter.interpret(missingToken) is QrUiEnvelope.Unsupported)
    }

    @Test
    fun messageAndUnknownProtocolDoNotBecomeExecutableActions() {
        val message = QrActionEnvelope(protocolVersion = 1, presentation = QrPresentation("Thông báo", "Từ máy chủ"))
        assertTrue(QrActionInterpreter.interpret(message) is QrUiEnvelope.Message)

        assertTrue(
            QrActionInterpreter.interpret(QrActionEnvelope(presentation = message.presentation))
                is QrUiEnvelope.Unsupported,
        )

        val future = message.copy(protocolVersion = 2)
        assertTrue(QrActionInterpreter.interpret(future) is QrUiEnvelope.Unsupported)

        val unknown = message.copy(actions = listOf(QrClientAction("x", "future_native", "Chạy")))
        assertTrue(QrActionInterpreter.interpret(unknown) is QrUiEnvelope.Unsupported)
    }

    @Test
    fun knownActionSurvivesUnknownCompanionButDuplicateIdsAreRejected() {
        val mixed = QrActionEnvelope(
            protocolVersion = 1,
            presentation = QrPresentation("Mở", "Trang an toàn"),
            actions = listOf(
                QrClientAction("open", QrActionTypes.OpenHttpsUrl, "Mở", url = "https://example.com/help"),
                QrClientAction("future", "future_type", "Khác"),
            ),
        )
        val dialog = QrActionInterpreter.interpret(mixed) as QrUiEnvelope.Dialog
        assertEquals(listOf("open"), dialog.actions.map { it.id })

        val duplicate = mixed.copy(actions = listOf(
            QrClientAction("same", QrActionTypes.Dismiss, "Đóng"),
            QrClientAction("same", QrActionTypes.Dismiss, "Hủy"),
        ))
        assertTrue(QrActionInterpreter.interpret(duplicate) is QrUiEnvelope.Unsupported)
    }

    @Test
    fun externalUrlPolicyOnlyAllowsAbsoluteHttpsWithoutUserInfo() {
        assertEquals("https://example.com/path?q=1#x", QrExternalUrlPolicy.normalize("https://example.com/path?q=1#x"))
        assertTrue(QrExternalUrlPolicy.normalize("HTTPS://EXAMPLE.COM:443/help") != null)

        val blocked = listOf(
            "http://example.com",
            "//example.com/path",
            "https://user@example.com/path",
            "https://example.com:8443/path",
            "https://localhost/help",
            "https://api.localhost/help",
            "https://127.0.0.1/help",
            "https://printer/help",
            "https://2130706433/help",
            "https://[::1]/help",
            "https://0x7f000001/help",
            "file:///tmp/x",
            "content://example/x",
            "data:text/plain,x",
            "javascript:alert(1)",
            "intent://example.com",
            "https://example.com\\@evil.test",
            "https://example.com/\nnext",
        )
        blocked.forEach { assertNull("Expected blocked: $it", QrExternalUrlPolicy.normalize(it)) }
        assertFalse(blocked.isEmpty())
    }

    @Test
    fun unicodeLinksAreNormalisedInsteadOfBeingRejectedAsUnsafe() {
        // Trước đây URI() ném lỗi với ký tự ngoài ASCII nên link hợp lệ bị báo nhầm là không an toàn.
        val normalised = QrExternalUrlPolicy.normalize("https://thử.example.com/help")
        assertEquals("https://xn--th-yct.example.com/help", normalised)
        // Không tin vào chuỗi punycode gõ tay: giải ngược phải ra đúng tên miền ban đầu.
        assertEquals("thử.example.com", java.net.IDN.toUnicode(java.net.URI(normalised!!).host))
        // Dấu trong đường dẫn/tham số phải thành phần trăm-mã hoá, không được rơi mất.
        assertEquals(
            "https://example.com/h%C6%B0%E1%BB%9Bng-d%E1%BA%ABn",
            QrExternalUrlPolicy.normalize("https://example.com/hướng-dẫn"),
        )
        // Dấu cách cũng là ký tự URI() không nhận — phải thành %20 chứ không được chặn oan.
        assertEquals(
            "https://example.com/t%C3%ACm?q=k%E1%BA%BF%20to%C3%A1n",
            QrExternalUrlPolicy.normalize("https://example.com/tìm?q=kế toán"),
        )
        assertEquals("https://example.com/a%20b", QrExternalUrlPolicy.normalize("https://example.com/a b"))
        // Link đã mã hoá sẵn phải đi qua nguyên vẹn, không bị mã hoá hai lần thành %2520.
        assertEquals("https://example.com/a%20b", QrExternalUrlPolicy.normalize("https://example.com/a%20b"))

        // Nới lỏng cho Unicode KHÔNG được mở đường vòng qua các luật cũ.
        assertNull(QrExternalUrlPolicy.normalize("http://thử.example.com"))
        assertNull(QrExternalUrlPolicy.normalize("https://ngườidùng@thử.example.com/x"))
        assertNull(QrExternalUrlPolicy.normalize("https://thử.example.com:8443/x"))
        assertNull(QrExternalUrlPolicy.normalize("https://thử.localhost/x"))
    }
}
