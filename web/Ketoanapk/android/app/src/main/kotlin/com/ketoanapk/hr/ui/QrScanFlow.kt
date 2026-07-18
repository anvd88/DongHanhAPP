package com.ketoanapk.hr.ui

import android.content.ActivityNotFoundException
import android.content.ClipData
import android.content.ClipDescription
import android.content.ClipboardManager
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.PersistableBundle
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.ketoanapk.hr.data.QrActionEnvelope
import com.ketoanapk.hr.data.QrClientAction
import com.ketoanapk.hr.data.QrResolveOutcome
import com.ketoanapk.hr.network.ApiClient
import kotlinx.serialization.decodeFromString

/**
 * APK không phân loại QR. Mọi chuỗi đều gửi tới /api/qr/resolve trước; server trả nội dung và các
 * primitive UI ổn định nên có thể thêm nhiều nghiệp vụ QR mà không phát hành lại APK.
 *
 * Chỉ khi máy chủ nói rõ là không có nghiệp vụ nào cho mã đó (hoặc đang mất mạng) thì app mới tự đọc
 * nội dung mã bằng [QrContentReader] để người dùng ít nhất cũng xem/sao chép được. Thứ tự này giữ cho
 * nghiệp vụ luôn thuộc về máy chủ: một mã QR nội bộ mới sẽ chạy đúng nghiệp vụ chứ không bị app đọc thô.
 */
internal data class QrScanController(
    val busy: Boolean,
    val dialog: QrUiEnvelope.Dialog?,
    val startScan: () -> Unit,
    val resolveValue: (String) -> Unit,
    val selectAction: (QrClientAction) -> Unit,
    val dismiss: () -> Unit,
)

@Composable
internal fun rememberQrScanController(vm: HrViewModel): QrScanController {
    val context = LocalContext.current
    var dialog by remember { mutableStateOf<QrUiEnvelope.Dialog?>(null) }
    var busy by remember { mutableStateOf(false) }
    var scannerActive by remember { mutableStateOf(false) }

    fun showEnvelope(envelope: QrActionEnvelope) {
        when (val ui = QrActionInterpreter.interpret(envelope)) {
            is QrUiEnvelope.Dialog -> dialog = ui
            is QrUiEnvelope.Message -> {
                // Message có thể dài tới 1.500 ký tự; giữ trong dialog để người dùng chủ động đọc
                // và đóng, thay vì Snackbar biến mất trước khi đọc xong.
                dialog = QrUiEnvelope.Dialog(
                    title = ui.title,
                    message = ui.message,
                    severity = ui.severity,
                    actions = listOf(
                        QrClientAction("local-dismiss", QrActionTypes.Dismiss, "Đóng", "primary", closeOnSelect = true),
                    ),
                    decisionToken = null,
                    dismissActionId = "local-dismiss",
                )
            }
            is QrUiEnvelope.Unsupported -> {
                dialog = null
                vm.showActionMessage(ui.message)
            }
        }
    }

    /** Máy chủ không nhận nghiệp vụ nào cho mã này → đọc tại chỗ cho người dùng xem và sao chép. */
    fun showLocalRead(value: String, offline: Boolean) {
        val read = QrContentReader.read(value)
        val note = if (offline) {
            "\n\nĐang không kết nối được máy chủ nên ứng dụng mới chỉ đọc nội dung mã, chưa kiểm tra được đây có phải mã của công ty hay không."
        } else {
            ""
        }
        dialog = QrUiEnvelope.Dialog(
            title = read.title,
            message = read.body + note,
            severity = if (offline) "warning" else "info",
            actions = buildList {
                read.openUrl?.let { url ->
                    add(QrClientAction("local-open", QrActionTypes.OpenHttpsUrl, "Mở liên kết", "primary", url, closeOnSelect = true))
                }
                if (read.copyText.isNotEmpty()) {
                    // Không đóng dialog: người dùng thường sao chép xong vẫn muốn đọc tiếp nội dung.
                    add(QrClientAction("local-copy", QrActionTypes.LocalCopy, read.copyLabel,
                        if (read.openUrl == null) "primary" else "secondary"))
                }
                add(QrClientAction("local-dismiss", QrActionTypes.Dismiss, "Đóng", "secondary", closeOnSelect = true))
            },
            decisionToken = null,
            dismissActionId = "local-dismiss",
            copyText = read.copyText,
            copySensitive = read.sensitive,
        )
    }

    fun copyToClipboard(current: QrUiEnvelope.Dialog) {
        val value = current.copyText
        if (value.isNullOrEmpty()) return
        val clipboard = context.getSystemService(ClipboardManager::class.java)
        if (clipboard == null) {
            vm.showActionMessage("Thiết bị không cho phép sao chép nội dung.")
            return
        }
        val clip = ClipData.newPlainText("Mã QR", value)
        if (current.copySensitive && Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            // Chặn Android hiện xem trước nội dung vừa sao chép — mật khẩu Wi-Fi không nên nằm trên màn hình.
            clip.description.extras = PersistableBundle().apply {
                putBoolean(ClipDescription.EXTRA_IS_SENSITIVE, true)
            }
        }
        clipboard.setPrimaryClip(clip)
        // Android 13 trở lên đã tự hiện xác nhận sao chép của hệ thống; máy cũ hơn thì không có gì báo.
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) vm.showActionMessage("Đã sao chép.")
    }

    fun openHttps(url: String?) {
        val safeUrl = QrExternalUrlPolicy.normalize(url)
        if (safeUrl == null) {
            vm.showActionMessage("Máy chủ trả về liên kết không an toàn nên ứng dụng đã chặn mở.")
            return
        }
        try {
            context.startActivity(
                Intent(Intent.ACTION_VIEW, Uri.parse(safeUrl)).addCategory(Intent.CATEGORY_BROWSABLE),
            )
        } catch (_: ActivityNotFoundException) {
            vm.showActionMessage("Điện thoại chưa có ứng dụng phù hợp để mở liên kết HTTPS.")
        } catch (_: SecurityException) {
            vm.showActionMessage("Điện thoại đã chặn không cho mở liên kết này.")
        }
    }

    fun performAction(action: QrClientAction) {
        val current = dialog ?: return
        if (busy) return
        if (action.closeOnSelect) dialog = null

        when (action.type) {
            QrActionTypes.ServerDecision -> {
                val token = current.decisionToken
                if (token.isNullOrBlank()) {
                    dialog = null
                    vm.showActionMessage("Phản hồi QR từ máy chủ thiếu vé xác nhận.")
                    return
                }
                // Khóa lần quét/thao tác kế tiếp cho tới khi server trả lời, kể cả khi nút hiện tại
                // đóng dialog ngay (ví dụ Từ chối). Nhờ vậy một vé không bị gửi quyết định song song.
                busy = true
                vm.decideQr(token, action.id) { next ->
                    busy = false
                    if (next != null) {
                        dialog = null
                        showEnvelope(next)
                    }
                }
            }
            QrActionTypes.OpenHttpsUrl -> {
                openHttps(action.url)
            }
            QrActionTypes.LocalCopy -> copyToClipboard(current)
            QrActionTypes.Dismiss -> dialog = null
            else -> {
                dialog = null
                vm.showActionMessage("Ứng dụng chưa hỗ trợ hành động QR này.")
            }
        }
    }

    fun resolveValue(rawValue: String) {
        val value = rawValue.trim()
        if (value.isEmpty() || busy || scannerActive) return
        busy = true
        vm.resolveQr(value) { outcome ->
            busy = false
            when (outcome) {
                is QrResolveOutcome.Handled -> showEnvelope(outcome.envelope)
                QrResolveOutcome.Unhandled -> showLocalRead(value, offline = false)
                QrResolveOutcome.Offline -> showLocalRead(value, offline = true)
                // Máy chủ từ chối là kết quả dứt khoát; không hiển thị thô mã phiên đăng nhập.
                is QrResolveOutcome.Rejected -> vm.showActionMessage(outcome.message)
            }
        }
    }

    val launcher = rememberLauncherForActivityResult(QrCaptureContract()) { result ->
        scannerActive = false
        // Màn quét đã hỏi server ngay trên camera để QR đăng nhập cập nhật tức thì. Envelope được
        // chuyển về nguyên vẹn; nhánh dưới chỉ chạy khi vì lý do nào đó chỉ có nội dung mã.
        val resolvedEnvelope = result.resolvedEnvelopeJson
            ?.let { encoded -> runCatching { ApiClient.json.decodeFromString<QrActionEnvelope>(encoded) }.getOrNull() }
        val value = result.value?.trim().orEmpty()
        if (resolvedEnvelope != null) {
            showEnvelope(resolvedEnvelope)
        } else if (value.isNotEmpty()) {
            resolveValue(value)
        }
    }

    return QrScanController(
        busy = busy || scannerActive,
        dialog = dialog,
        startScan = {
            if (!busy && !scannerActive) {
                scannerActive = true
                runCatching { launcher.launch(QrCaptureRequest(QrCaptureMode.Resolve)) }.onFailure {
                    scannerActive = false
                    vm.showActionMessage("Không thể mở camera quét QR trên thiết bị này.")
                }
            }
        },
        resolveValue = ::resolveValue,
        selectAction = ::performAction,
        dismiss = {
            val current = dialog
            val dismissAction = current?.dismissActionId?.let { id -> current.actions.firstOrNull { it.id == id } }
            if (dismissAction != null) performAction(dismissAction) else dialog = null
        },
    )
}

@Composable
internal fun QrScanDialog(controller: QrScanController) {
    if (controller.busy && controller.dialog == null) {
        AlertDialog(
            onDismissRequest = {},
            icon = {
                CircularProgressIndicator(
                    modifier = Modifier.size(28.dp),
                    strokeWidth = 3.dp,
                )
            },
            // Luồng này xử lý MỌI loại mã (đăng nhập web, phiếu chi, Wi-Fi, liên kết…) nên chữ ở đây
            // phải trung tính — trước đây nói riêng chuyện đăng nhập, quét mã Wi-Fi cũng hiện "yêu cầu
            // từ trình duyệt".
            title = { Text("Đang kiểm tra mã QR") },
            text = { Text("Ứng dụng đang hỏi máy chủ xem mã vừa quét dùng để làm gì.") },
            confirmButton = {},
        )
        return
    }
    val dialog = controller.dialog ?: return
    val primary = dialog.actions.firstOrNull { it.style == "primary" } ?: dialog.actions.first()
    val secondary = dialog.actions.filterNot { it.id == primary.id }

    AlertDialog(
        onDismissRequest = controller.dismiss,
        icon = { Icon(Icons.Filled.QrCodeScanner, contentDescription = null) },
        title = { Text(dialog.title.ifBlank { "Mã QR" }) },
        text = {
            // Nội dung đọc từ mã lạ có thể dài (danh thiếp, văn bản) nên phải cuộn được trong dialog
            // thay vì tràn ra ngoài đẩy mất hàng nút bấm.
            Column(
                verticalArrangement = Arrangement.spacedBy(8.dp),
                modifier = Modifier
                    .heightIn(max = 320.dp)
                    .verticalScroll(rememberScrollState()),
            ) {
                SelectionContainer {
                    Text(dialog.message.ifBlank { "Chọn thao tác bạn muốn thực hiện." })
                }
            }
        },
        confirmButton = {
            QrActionButton(primary, controller.busy, controller.selectAction)
        },
        dismissButton = {
            Column(horizontalAlignment = Alignment.End) {
                secondary.forEachIndexed { index, action ->
                    if (index > 0) Spacer(Modifier.size(2.dp))
                    QrActionButton(action, controller.busy, controller.selectAction)
                }
            }
        },
    )
}

@Composable
private fun QrActionButton(action: QrClientAction, busy: Boolean, onClick: (QrClientAction) -> Unit) {
    if (action.style == "primary") {
        Button(enabled = !busy, onClick = { onClick(action) }) {
            if (busy) {
                CircularProgressIndicator(
                    modifier = Modifier.size(18.dp),
                    strokeWidth = 2.dp,
                    color = MaterialTheme.colorScheme.onPrimary,
                )
                Spacer(Modifier.width(8.dp))
            }
            Text(action.label)
        }
    } else {
        TextButton(
            enabled = !busy,
            onClick = { onClick(action) },
            colors = if (action.style == "danger")
                ButtonDefaults.textButtonColors(contentColor = MaterialTheme.colorScheme.error)
            else ButtonDefaults.textButtonColors(),
        ) {
            Text(action.label)
        }
    }
}
