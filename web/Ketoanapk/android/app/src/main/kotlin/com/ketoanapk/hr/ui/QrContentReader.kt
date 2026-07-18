package com.ketoanapk.hr.ui

import java.net.URI
import java.net.URLDecoder
import java.util.Locale

/**
 * Nội dung một mã QR sau khi được đọc NGAY TRÊN MÁY, dùng khi máy chủ không có nghiệp vụ nào cho mã đó
 * (hoặc đang mất mạng). Xem [QrContentReader].
 */
internal data class QrLocalRead(
    val title: String,
    val body: String,
    val copyText: String,
    val copyLabel: String,
    /** Chỉ khác null khi liên kết vượt qua [QrExternalUrlPolicy]; null thì không hiện nút mở. */
    val openUrl: String?,
    /** Nội dung sao chép là bí mật (mật khẩu Wi-Fi) → không cho bảng nháy hiện xem trước. */
    val sensitive: Boolean = false,
)

/**
 * Bóc tách các định dạng mã QR phổ thông thành nội dung đọc được. Chỉ ĐỌC và hiển thị: không tự mở
 * liên kết, không tự nối Wi-Fi, không tự lưu danh bạ — mọi hành động đều phải do người dùng bấm.
 *
 * Cố ý tách khỏi [QrActionInterpreter]: phần kia diễn giải lệnh của MÁY CHỦ (tin cậy có điều kiện),
 * phần này diễn giải nội dung do NGƯỜI LẠ in ra giấy nên mọi thứ đọc được đều là dữ liệu, không phải lệnh.
 */
internal object QrContentReader {
    /** Bằng giới hạn message của máy chủ để dialog đọc tại chỗ không dài hơn dialog do server trả về. */
    private const val MaxBodyLength = 1_500

    /**
     * Các chuẩn QR phổ thông được đọc hoàn toàn trên máy. Ngoài việc phản hồi nhanh như một máy quét
     * thông thường, điều này tránh gửi mật khẩu Wi-Fi, danh thiếp, vị trí hoặc URL của người dùng tới
     * máy chủ chỉ để nhận câu trả lời "không có nghiệp vụ". Chuỗi riêng/không rõ định dạng vẫn đi qua
     * bộ phân giải server-driven để các nghiệp vụ Ketoan cấu hình động tiếp tục hoạt động.
     */
    fun isStandardFormat(raw: String): Boolean {
        val value = raw.trim()
        return value.startsWith("WIFI:", true) ||
            value.startsWith("MECARD:", true) ||
            value.startsWith("BEGIN:VCARD", true) ||
            value.startsWith("http://", true) ||
            value.startsWith("https://", true) ||
            value.startsWith("tel:", true) ||
            value.startsWith("mailto:", true) ||
            value.startsWith("SMSTO:", true) ||
            value.startsWith("sms:", true) ||
            value.startsWith("geo:", true)
    }

    fun read(raw: String): QrLocalRead {
        val value = raw.trim()
        return when {
            value.isEmpty() -> text(value)
            value.startsWith("WIFI:", true) -> wifi(value)
            value.startsWith("MECARD:", true) -> mecard(value)
            value.startsWith("BEGIN:VCARD", true) -> vcard(value)
            value.startsWith("http://", true) || value.startsWith("https://", true) -> link(value)
            value.startsWith("tel:", true) -> phone(value)
            value.startsWith("mailto:", true) -> mail(value)
            value.startsWith("SMSTO:", true) || value.startsWith("sms:", true) -> sms(value)
            value.startsWith("geo:", true) -> geo(value)
            else -> text(value)
        }
    }

    /** WIFI:T:WPA;S:tên mạng;P:mật khẩu;H:true;; */
    private fun wifi(value: String): QrLocalRead {
        val fields = fieldMap(value.drop("WIFI:".length))
        val ssid = unescape(fields["S"].orEmpty())
        val password = unescape(fields["P"].orEmpty())
        if (ssid.isEmpty() && password.isEmpty()) return text(value)

        val rawType = unescape(fields["T"].orEmpty())
        val security = when (rawType.uppercase(Locale.ROOT)) {
            "", "NOPASS" -> "Không mật khẩu"
            "WEP" -> "WEP"
            "WPA", "WPA2", "WPA3", "WPA2-EAP" -> "WPA/WPA2"
            else -> rawType
        }
        val lines = buildList {
            add("Tên mạng (SSID): ${ssid.ifEmpty { "(không ghi)" }}")
            add("Bảo mật: $security")
            if (password.isNotEmpty()) add("Mật khẩu: $password")
            if (unescape(fields["H"].orEmpty()).equals("true", true)) add("Mạng ẩn: có")
        }
        return QrLocalRead(
            title = "Mạng Wi-Fi",
            body = display(lines.joinToString("\n")),
            copyText = password.ifEmpty { ssid },
            copyLabel = if (password.isNotEmpty()) "Sao chép mật khẩu" else "Sao chép tên mạng",
            openUrl = null,
            sensitive = password.isNotEmpty(),
        )
    }

    /** MECARD:N:Họ,Tên;TEL:...;EMAIL:...;; */
    private fun mecard(value: String): QrLocalRead {
        val fields = fieldMap(value.drop("MECARD:".length))
        val name = fields["N"]?.let { formatName(splitUnescaped(it, ',')) }.orEmpty()
        val phone = unescape(fields["TEL"].orEmpty())
        val email = unescape(fields["EMAIL"].orEmpty())
        val lines = buildList {
            if (name.isNotEmpty()) add("Tên: $name")
            if (phone.isNotEmpty()) add("Điện thoại: $phone")
            if (email.isNotEmpty()) add("Email: $email")
            unescape(fields["ORG"].orEmpty()).takeIf { it.isNotEmpty() }?.let { add("Tổ chức: $it") }
            fields["ADR"]?.let { joinParts(splitUnescaped(it, ',')) }?.takeIf { it.isNotEmpty() }
                ?.let { add("Địa chỉ: $it") }
            unescape(fields["NOTE"].orEmpty()).takeIf { it.isNotEmpty() }?.let { add("Ghi chú: $it") }
        }
        if (lines.isEmpty()) return text(value)
        val (copyText, copyLabel) = contactCopy(phone, email, name, value)
        return QrLocalRead("Danh thiếp", display(lines.joinToString("\n")), copyText, copyLabel, null)
    }

    /** BEGIN:VCARD ... END:VCARD */
    private fun vcard(value: String): QrLocalRead {
        val lines = unfold(value)
        val name = prop(lines, "FN").ifEmpty { formatName(splitUnescaped(prop(lines, "N"), ';')) }
        val phones = props(lines, "TEL")
        val emails = props(lines, "EMAIL")
        val body = buildList {
            if (name.isNotEmpty()) add("Tên: $name")
            joinParts(prop(lines, "ORG").split(';')).takeIf { it.isNotEmpty() }?.let { add("Tổ chức: $it") }
            prop(lines, "TITLE").takeIf { it.isNotEmpty() }?.let { add("Chức danh: $it") }
            phones.forEach { add("Điện thoại: $it") }
            emails.forEach { add("Email: $it") }
            joinParts(prop(lines, "ADR").split(';')).takeIf { it.isNotEmpty() }?.let { add("Địa chỉ: $it") }
            prop(lines, "URL").takeIf { it.isNotEmpty() }?.let { add("Website: $it") }
            prop(lines, "NOTE").takeIf { it.isNotEmpty() }?.let { add("Ghi chú: $it") }
        }
        if (body.isEmpty()) return text(value)
        val (copyText, copyLabel) = contactCopy(phones.firstOrNull().orEmpty(), emails.firstOrNull().orEmpty(), name, value)
        return QrLocalRead("Danh thiếp", display(body.joinToString("\n")), copyText, copyLabel, null)
    }

    private fun link(value: String): QrLocalRead {
        val safeUrl = QrExternalUrlPolicy.normalize(value)
        // URL Unicode được normalize sang ASCII/punycode trước khi bóc host; URI(value) trực tiếp thường
        // trả host=null và vô tình làm mất dòng tên miền quan trọng nhất trong cảnh báo liên kết.
        val hostSource = safeUrl ?: value
        val host = runCatching { URI(hostSource).host }.getOrNull()?.trimEnd('.')?.lowercase().orEmpty()
        val lines = buildList {
            // Tên miền đứng riêng một dòng: liên kết dài dễ giấu tên miền thật ở giữa đống tham số.
            if (host.isNotEmpty()) add("Tên miền: $host")
            add("Liên kết: $value")
            if (safeUrl == null) {
                add("Ứng dụng không mở trực tiếp liên kết này vì nó không phải HTTPS tới một tên miền thường.")
            }
        }
        return QrLocalRead(
            title = "Liên kết",
            body = display(lines.joinToString("\n")),
            copyText = value,
            copyLabel = "Sao chép liên kết",
            openUrl = safeUrl,
        )
    }

    private fun phone(value: String): QrLocalRead {
        val number = value.drop("tel:".length).substringBefore('?').trim()
        if (number.isEmpty()) return text(value)
        return QrLocalRead("Số điện thoại", display("Số máy: $number"), number, "Sao chép số điện thoại", null)
    }

    private fun mail(value: String): QrLocalRead {
        val rest = value.drop("mailto:".length)
        val address = rest.substringBefore('?').trim()
        if (address.isEmpty()) return text(value)
        val lines = buildList {
            add("Người nhận: $address")
            queryParam(rest, "subject").takeIf { it.isNotEmpty() }?.let { add("Tiêu đề: $it") }
            queryParam(rest, "body").takeIf { it.isNotEmpty() }?.let { add("Nội dung: $it") }
        }
        return QrLocalRead("Địa chỉ email", display(lines.joinToString("\n")), address, "Sao chép email", null)
    }

    /** SMSTO:số:nội dung (chuẩn QR) và sms:số?body=nội dung. */
    private fun sms(value: String): QrLocalRead {
        val rest = value.substringAfter(':', "")
        val number = rest.substringBefore(':').substringBefore('?').trim()
        if (number.isEmpty()) return text(value)
        val message = if (rest.contains('?')) queryParam(rest, "body") else rest.substringAfter(':', "").trim()
        val lines = buildList {
            add("Gửi tới: $number")
            if (message.isNotEmpty()) add("Nội dung: $message")
        }
        return QrLocalRead("Tin nhắn", display(lines.joinToString("\n")), number, "Sao chép số điện thoại", null)
    }

    private fun geo(value: String): QrLocalRead {
        val parts = value.drop("geo:".length).substringBefore('?').split(',')
        val lat = parts.getOrNull(0)?.trim().orEmpty()
        val lng = parts.getOrNull(1)?.trim().orEmpty()
        if (lat.isEmpty() || lng.isEmpty()) return text(value)
        val lines = buildList {
            add("Vĩ độ: $lat")
            add("Kinh độ: $lng")
            queryParam(value, "q").takeIf { it.isNotEmpty() }?.let { add("Địa điểm: $it") }
        }
        return QrLocalRead("Vị trí", display(lines.joinToString("\n")), "$lat,$lng", "Sao chép toạ độ", null)
    }

    private fun text(value: String): QrLocalRead = QrLocalRead(
        title = "Văn bản",
        body = display(value).ifBlank { "Mã QR này không chứa nội dung đọc được." },
        copyText = value,
        copyLabel = "Sao chép",
        openUrl = null,
    )

    private fun contactCopy(phone: String, email: String, name: String, fallback: String): Pair<String, String> =
        when {
            phone.isNotEmpty() -> phone to "Sao chép số điện thoại"
            email.isNotEmpty() -> email to "Sao chép email"
            name.isNotEmpty() -> name to "Sao chép tên"
            else -> fallback to "Sao chép nội dung"
        }

    /** Ký tự điều khiển trong mã QR không được phép làm vỡ bố cục dialog. */
    private fun display(value: String): String {
        val cleaned = buildString(value.length) {
            for (c in value) {
                val allowedControl = c == '\n' || c == '\t'
                val safeCharacter = !c.isISOControl() &&
                    Character.getType(c) != Character.FORMAT.toInt()
                if (allowedControl || safeCharacter) append(c)
            }
        }
        return if (cleaned.length <= MaxBodyLength) cleaned else cleaned.take(MaxBodyLength) + "…"
    }

    /** "Họ,Tên" (MECARD) và "Họ;Tên;;;" (vCard) đều xếp phần sau lên trước cho đúng cách gọi tên. */
    private fun formatName(parts: List<String>): String =
        parts.map { unescape(it).trim() }.filter { it.isNotEmpty() }.reversed().joinToString(" ")

    private fun joinParts(parts: List<String>): String =
        parts.map { it.trim() }.filter { it.isNotEmpty() }.joinToString(", ")

    private fun props(lines: List<String>, name: String): List<String> = lines.mapNotNull { line ->
        val at = line.indexOf(':')
        if (at <= 0) return@mapNotNull null
        // "TEL;TYPE=CELL:0900..." → tên thuộc tính nằm trước dấu ';' đầu tiên.
        if (!line.substring(0, at).substringBefore(';').trim().equals(name, true)) return@mapNotNull null
        unescape(line.substring(at + 1).trim(), newlines = true).ifBlank { null }
    }

    private fun prop(lines: List<String>, name: String): String = props(lines, name).firstOrNull().orEmpty()

    /** vCard cho phép ngắt một thuộc tính dài thành nhiều dòng; dòng nối bắt đầu bằng dấu cách/tab. */
    private fun unfold(value: String): List<String> {
        val out = mutableListOf<String>()
        for (raw in value.split('\n')) {
            val line = raw.trimEnd('\r')
            if (line.isEmpty()) continue
            if ((line.startsWith(" ") || line.startsWith("\t")) && out.isNotEmpty()) {
                out[out.lastIndex] = out.last() + line.substring(1)
            } else {
                out.add(line)
            }
        }
        return out
    }

    private fun fieldMap(body: String): Map<String, String> {
        val map = LinkedHashMap<String, String>()
        for (token in splitUnescaped(body, ';')) {
            val at = indexOfUnescaped(token, ':')
            if (at <= 0) continue
            val key = token.substring(0, at).trim().uppercase(Locale.ROOT)
            // Giữ giá trị còn nguyên dấu thoát: nơi dùng có thể cần tách tiếp (vd "Họ,Tên") trước khi bỏ dấu.
            if (key.isNotEmpty() && !map.containsKey(key)) map[key] = token.substring(at + 1)
        }
        return map
    }

    /**
     * @param newlines vCard dùng "\n" để xuống dòng thật; WIFI/MECARD thì không nên "\n" ở đó chỉ là chữ n.
     */
    private fun unescape(value: String, newlines: Boolean = false): String {
        val sb = StringBuilder(value.length)
        var i = 0
        while (i < value.length) {
            val c = value[i]
            if (c == '\\' && i + 1 < value.length) {
                val next = value[i + 1]
                sb.append(if (newlines && (next == 'n' || next == 'N')) '\n' else next)
                i += 2
            } else {
                sb.append(c)
                i++
            }
        }
        return sb.toString()
    }

    private fun splitUnescaped(value: String, separator: Char): List<String> {
        val parts = mutableListOf<String>()
        val current = StringBuilder()
        var i = 0
        while (i < value.length) {
            val c = value[i]
            when {
                c == '\\' && i + 1 < value.length -> { current.append(c).append(value[i + 1]); i += 2 }
                c == separator -> { parts.add(current.toString()); current.setLength(0); i++ }
                else -> { current.append(c); i++ }
            }
        }
        parts.add(current.toString())
        return parts
    }

    private fun indexOfUnescaped(value: String, target: Char): Int {
        var i = 0
        while (i < value.length) {
            when {
                value[i] == '\\' -> i += 2
                value[i] == target -> return i
                else -> i++
            }
        }
        return -1
    }

    private fun queryParam(value: String, name: String): String {
        val query = value.substringAfter('?', "")
        if (query.isEmpty()) return ""
        return query.split('&').firstNotNullOfOrNull { pair ->
            if (!pair.substringBefore('=').equals(name, true)) null
            else runCatching { URLDecoder.decode(pair.substringAfter('=', ""), "UTF-8") }.getOrNull()
        }.orEmpty()
    }
}
