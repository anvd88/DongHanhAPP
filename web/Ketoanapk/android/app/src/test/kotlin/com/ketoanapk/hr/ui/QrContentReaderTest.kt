package com.ketoanapk.hr.ui

import java.util.Locale
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class QrContentReaderTest {
    @Test
    fun unicodeBidiAndFormatCharactersAreHiddenButCopyKeepsOriginalText() {
        val raw = "abc\u202Edef\u202C\u2066ghi\u2069\u200F\u061C\u200B"

        val read = QrContentReader.read(raw)

        assertEquals("abcdefghi", read.body)
        assertEquals(raw, read.copyText)
        listOf('\u202E', '\u202C', '\u2066', '\u2069', '\u200F', '\u061C', '\u200B').forEach { format ->
            assertFalse("FORMAT character U+${format.code.toString(16)} leaked into display body", read.body.contains(format))
        }
    }

    @Test
    fun wifiAndMecardParsingDoNotDependOnTurkishDefaultLocale() {
        val originalLocale = Locale.getDefault()
        try {
            Locale.setDefault(Locale.forLanguageTag("tr-TR"))

            val wifi = QrContentReader.read("wifi:t:wpa;s:Ofis;p:sifre;;")
            assertEquals("sifre", wifi.copyText)
            assertTrue(wifi.sensitive)

            // Turkish uppercasing turns the ASCII 'i' in "email" into U+0130 unless ROOT is used.
            val mecard = QrContentReader.read("mecard:n:Yilmaz,Ilker;email:ilker@example.com;;")
            assertEquals("ilker@example.com", mecard.copyText)
            assertTrue(mecard.body.contains("ilker@example.com"))
        } finally {
            Locale.setDefault(originalLocale)
        }
    }

    @Test
    fun wifiKeepsEscapedPasswordAndMarksItSensitive() {
        val read = QrContentReader.read("WIFI:T:WPA;S:Ke Toan CP;P:bi\\;mat;H:true;;")

        assertEquals("Mạng Wi-Fi", read.title)
        assertTrue(read.body.contains("Tên mạng (SSID): Ke Toan CP"))
        assertTrue(read.body.contains("Bảo mật: WPA/WPA2"))
        assertTrue(read.body.contains("Mật khẩu: bi;mat"))
        assertTrue(read.body.contains("Mạng ẩn: có"))
        // Sao chép mật khẩu chứ không phải cả khối, và không được để bảng nháy hiện xem trước.
        assertEquals("bi;mat", read.copyText)
        assertTrue(read.sensitive)
        // Đọc xong vẫn không được mọc ra nút mở/nối mạng nào.
        assertNull(read.openUrl)
    }

    @Test
    fun wifiWithoutPasswordCopiesNetworkName() {
        val read = QrContentReader.read("WIFI:T:nopass;S:Khach;;")

        assertTrue(read.body.contains("Bảo mật: Không mật khẩu"))
        assertEquals("Khach", read.copyText)
        assertEquals("Sao chép tên mạng", read.copyLabel)
        assertFalse(read.sensitive)
    }

    @Test
    fun httpsLinkShowsHostAndIsOpenable_butOtherSchemesAreNot() {
        val https = QrContentReader.read("https://ketoancp.click/tin-tuc?id=7")
        assertEquals("Liên kết", https.title)
        // Tên miền phải đứng riêng: liên kết dài dễ giấu tên miền thật giữa đống tham số.
        assertTrue(https.body.contains("Tên miền: ketoancp.click"))
        assertEquals("https://ketoancp.click/tin-tuc?id=7", https.openUrl)
        assertEquals("https://ketoancp.click/tin-tuc?id=7", https.copyText)

        // Cùng một chính sách với liên kết do máy chủ trả về: chỉ HTTPS tới tên miền thường mới mở được.
        assertNull(QrContentReader.read("http://example.com/a").openUrl)
        assertNull(QrContentReader.read("https://192.168.1.9/a").openUrl)
        assertTrue(QrContentReader.read("http://example.com/a").body.contains("không mở trực tiếp"))
    }

    @Test
    fun contactCardsReadNameInSpeakingOrder() {
        val vcard = QrContentReader.read(
            """
            BEGIN:VCARD
            VERSION:3.0
            N:Nguyen;An;;;
            TEL;TYPE=CELL:0901234567
            EMAIL:an@ketoancp.click
            ORG:Ke Toan CP;Ky Thuat
            END:VCARD
            """.trimIndent(),
        )
        assertEquals("Danh thiếp", vcard.title)
        assertTrue(vcard.body.contains("Tên: An Nguyen"))
        assertTrue(vcard.body.contains("Tổ chức: Ke Toan CP, Ky Thuat"))
        assertTrue(vcard.body.contains("Điện thoại: 0901234567"))
        assertEquals("0901234567", vcard.copyText)

        val mecard = QrContentReader.read("MECARD:N:Nguyen,An;TEL:0901234567;;")
        assertTrue(mecard.body.contains("Tên: An Nguyen"))
        assertEquals("0901234567", mecard.copyText)
    }

    @Test
    fun otherCommonSchemesAreLabelled() {
        assertEquals("Số điện thoại", QrContentReader.read("tel:+84901234567").title)
        assertEquals("+84901234567", QrContentReader.read("tel:+84901234567").copyText)

        val mail = QrContentReader.read("mailto:hr@ketoancp.click?subject=Xin%20nghi%20phep")
        assertEquals("hr@ketoancp.click", mail.copyText)
        assertTrue(mail.body.contains("Tiêu đề: Xin nghi phep"))

        val sms = QrContentReader.read("SMSTO:0901234567:Toi den muon")
        assertTrue(sms.body.contains("Gửi tới: 0901234567"))
        assertTrue(sms.body.contains("Nội dung: Toi den muon"))

        assertTrue(QrContentReader.read("geo:21.028511,105.804817").body.contains("Vĩ độ: 21.028511"))
    }

    @Test
    fun plainTextIsShownAsIs_withControlCharsStrippedAndLongContentCut() {
        val plain = QrContentReader.read("  Ma kho: KT-2026-07  ")
        assertEquals("Văn bản", plain.title)
        assertEquals("Ma kho: KT-2026-07", plain.body)
        assertEquals("Ma kho: KT-2026-07", plain.copyText)
        assertNull(plain.openUrl)

        // Ký tự điều khiển không được làm vỡ bố cục dialog; xuống dòng thật thì vẫn giữ.
        assertEquals("xinchao", QrContentReader.read("xin" + Char(7) + "chao").body)
        assertEquals("xin\nchao", QrContentReader.read("xin\nchao").body)

        val long = QrContentReader.read("x".repeat(4_000))
        assertEquals(1_501, long.body.length)
        assertTrue(long.body.endsWith("…"))
        // Cắt chỉ để hiển thị — sao chép vẫn phải ra đủ nội dung gốc.
        assertEquals(4_000, long.copyText.length)
    }

    @Test
    fun malformedPayloadsFallBackToPlainTextInsteadOfThrowing() {
        assertEquals("Văn bản", QrContentReader.read("WIFI:").title)
        assertEquals("Văn bản", QrContentReader.read("MECARD:;;").title)
        assertEquals("Văn bản", QrContentReader.read("tel:").title)
        assertEquals("Văn bản", QrContentReader.read("geo:abc").title)
        assertEquals("Văn bản", QrContentReader.read("BEGIN:VCARD\nEND:VCARD").title)
        assertTrue(QrContentReader.read("").body.contains("không chứa nội dung đọc được"))
    }
}
