package com.ketoanapk.hr.ui

import com.ketoanapk.hr.data.QrActionEnvelope
import com.ketoanapk.hr.data.QrClientAction
import java.net.URI

internal sealed interface QrUiEnvelope {
    data class Message(val title: String, val message: String, val severity: String) : QrUiEnvelope
    data class Dialog(
        val title: String,
        val message: String,
        val severity: String,
        val actions: List<QrClientAction>,
        val decisionToken: String?,
        val dismissActionId: String?,
        /**
         * Nội dung của nút "Sao chép". CHỈ do [QrContentReader] đặt vào khi app tự đọc mã; envelope của
         * máy chủ không bao giờ đi qua đây nên server không thể nhét nội dung vào bảng nháy.
         */
        val copyText: String? = null,
        val copySensitive: Boolean = false,
    ) : QrUiEnvelope
    data class Unsupported(val message: String) : QrUiEnvelope
}

/** Chỉ diễn giải các primitive UI ổn định; không chứa bất kỳ prefix hay nghiệp vụ QR cụ thể nào. */
internal object QrActionInterpreter {
    private const val ProtocolVersion = 1
    private const val MaxActions = 3

    fun interpret(envelope: QrActionEnvelope): QrUiEnvelope {
        if (envelope.protocolVersion != ProtocolVersion) {
            return QrUiEnvelope.Unsupported("Máy chủ yêu cầu phiên bản ứng dụng mới hơn để xử lý mã QR này.")
        }
        if (envelope.actions.size > MaxActions ||
            envelope.actions.map { it.id }.any { it.isBlank() } ||
            envelope.actions.groupingBy { it.id }.eachCount().any { it.value > 1 }) {
            return QrUiEnvelope.Unsupported("Phản hồi QR từ máy chủ không hợp lệ.")
        }

        val title = envelope.presentation.title.trim().take(160)
        val message = envelope.presentation.message.trim().take(1_500)
        if (envelope.actions.isEmpty()) {
            return QrUiEnvelope.Message(title, message, envelope.presentation.severity)
        }

        val validActions = envelope.actions.mapNotNull { action ->
            if (action.label.isBlank() || action.label.length > 48) return@mapNotNull null
            when (action.type) {
                QrActionTypes.ServerDecision ->
                    action.takeIf { !envelope.decisionToken.isNullOrBlank() }
                QrActionTypes.OpenHttpsUrl ->
                    QrExternalUrlPolicy.normalize(action.url)?.let { action.copy(url = it) }
                QrActionTypes.Dismiss -> action
                else -> null // type mới không làm APK cũ crash; server vẫn có thể trả type đã hỗ trợ kèm theo.
            }
        }
        if (validActions.isEmpty()) {
            return QrUiEnvelope.Unsupported(
                message.ifBlank { "Ứng dụng chưa hỗ trợ hành động QR mà máy chủ yêu cầu." },
            )
        }

        // Back/chạm ngoài chỉ được đóng local hoặc gửi một quyết định từ chối tường minh. Không bao
        // giờ biến thao tác dismiss thành nút primary hay tự mở URL do server cấu hình nhầm.
        val dismissId = envelope.dismissActionId?.takeIf { id ->
            validActions.any { action ->
                action.id == id && (
                    action.type == QrActionTypes.Dismiss ||
                        (action.type == QrActionTypes.ServerDecision &&
                            action.style == "danger" && action.closeOnSelect)
                    )
            }
        }
        return QrUiEnvelope.Dialog(
            title = title,
            message = message,
            severity = envelope.presentation.severity,
            actions = validActions,
            decisionToken = envelope.decisionToken,
            dismissActionId = dismissId,
        )
    }
}

/** Phòng thủ lớp hai ở APK; allowlist hostname vẫn do server quản lý để đổi mà không build lại. */
internal object QrExternalUrlPolicy {
    private const val HEX = "0123456789ABCDEF"

    fun normalize(value: String?): String? {
        val raw = value?.trim().orEmpty()
        if (raw.length !in 1..2_048 || raw.any(Char::isISOControl) || '\\' in raw) return null
        val uri = runCatching { URI(toAscii(raw) ?: return null) }.getOrNull() ?: return null
        if (!uri.isAbsolute || uri.isOpaque || !uri.scheme.equals("https", ignoreCase = true) ||
            uri.host.isNullOrBlank() || uri.rawUserInfo != null || (uri.port != -1 && uri.port != 443))
            return null
        val host = uri.host.trimEnd('.').lowercase()
        if (host == "localhost" || host.endsWith(".localhost") || ':' in host || '[' in host ||
            host.all { it.isDigit() || it == '.' } || isHexIpLiteral(host)) return null
        return uri.toASCIIString()
    }

    private fun isHexIpLiteral(host: String): Boolean {
        if (!host.startsWith("0x") || host.length <= 2) return false
        return host.drop(2).all { it.isDigit() || it.lowercaseChar() in 'a'..'f' }
    }

    /**
     * `URI()` của Java chỉ nhận ASCII, nên liên kết có dấu tiếng Việt (tên miền IDN hoặc đường dẫn) bị
     * ném lỗi và trước đây app báo nhầm là "liên kết không an toàn". Đưa tên miền về punycode và phần
     * trăm-mã hoá phần còn lại TRƯỚC khi kiểm tra, để mọi luật bên dưới vẫn chạy trên chuỗi ASCII chuẩn.
     */
    private fun toAscii(raw: String): String? {
        val schemeEnd = raw.indexOf("://")
        // Không có "://" thì không thể là https tuyệt đối; để nguyên cho các luật bên dưới từ chối.
        if (schemeEnd <= 0) return raw.takeIf { it.all { c -> c.code <= 0x7F } }
        val authorityStart = schemeEnd + 3
        val authorityEnd = raw.indexOfFirst(authorityStart) { it == '/' || it == '?' || it == '#' }
        val authority = raw.substring(authorityStart, authorityEnd)
        if (authority.isEmpty()) return null

        // Tên miền thuần ASCII giữ NGUYÊN XI: cho IDN.toASCII đụng vào sẽ làm hỏng các trường hợp mà
        // luật bên dưới đang xử lý đúng (dấu chấm cuối, "[::1]", nhãn lạ) vì nó ném lỗi thay vì trả về.
        val asciiAuthority =
            if (authority.all { it.code <= 0x7F }) authority else punycodeAuthority(authority) ?: return null
        return raw.substring(0, authorityStart) + asciiAuthority + percentEncode(raw.substring(authorityEnd))
    }

    private fun punycodeAuthority(authority: String): String? {
        // Tách cổng ra trước: IDN.toASCII coi ":443" là một phần nhãn tên miền và sẽ báo lỗi.
        val portAt = authority.lastIndexOf(':')
        val hasPort = portAt > 0 && authority.substring(portAt + 1).let { it.isNotEmpty() && it.all(Char::isDigit) }
        val host = if (hasPort) authority.substring(0, portAt) else authority
        val port = if (hasPort) authority.substring(portAt) else ""

        val punycode = runCatching { java.net.IDN.toASCII(host, java.net.IDN.ALLOW_UNASSIGNED) }.getOrNull()
        if (punycode.isNullOrEmpty() || punycode.any { it.code > 0x7F }) return null
        return punycode + port
    }

    /**
     * Mã hoá phần sau tên miền. Không chỉ ký tự ngoài ASCII: dấu cách và vài ký tự ASCII khác cũng
     * không hợp lệ trong URI, để nguyên là `URI()` ném lỗi và link bị chặn oan y như cũ.
     * `%` được giữ nguyên để link đã mã hoá sẵn đi qua được; `%` lẻ sẽ khiến URI() từ chối — chặn an toàn.
     */
    private fun percentEncode(rest: String): String = buildString(rest.length) {
        for (byte in rest.toByteArray(Charsets.UTF_8)) {
            val code = byte.toInt() and 0xFF
            val char = code.toChar()
            val allowed = code in 0x21..0x7E &&
                (char.isLetterOrDigit() || char in "-._~:/?#[]@!$&'()*+,;=%")
            if (allowed) append(char) else append('%').append(HEX[code shr 4]).append(HEX[code and 0x0F])
        }
    }

    private inline fun String.indexOfFirst(from: Int, predicate: (Char) -> Boolean): Int {
        for (i in from until length) if (predicate(this[i])) return i
        return length
    }
}

internal object QrActionTypes {
    const val ServerDecision = "server_decision"
    const val OpenHttpsUrl = "open_https_url"
    const val Dismiss = "dismiss"

    /**
     * Chỉ dùng nội bộ cho dialog đọc mã tại chỗ, KHÔNG nằm trong `capabilities` gửi lên máy chủ.
     * [QrActionInterpreter] loại mọi type lạ nên server có trả về type này cũng không bấm được.
     */
    const val LocalCopy = "local_copy"
}
