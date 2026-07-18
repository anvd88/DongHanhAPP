package com.ketoanapk.hr.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Payments
import androidx.compose.material.icons.filled.QrCode2
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.google.zxing.BarcodeFormat
import com.google.zxing.EncodeHintType
import com.google.zxing.qrcode.QRCodeWriter
import com.ketoanapk.hr.data.CreatePayoutBody
import com.ketoanapk.hr.data.PayoutVoucher

/**
 * Phiếu chi tiền mặt trên app.
 * - Nhân viên thường: xem phiếu chi của chính mình. Việc KÝ NHẬN vẫn làm bằng nút quét QR có sẵn ở
 *   header (server điều khiển hộp thoại xác nhận), nên màn này cố tình không có nút quét riêng.
 * - Kế toán (role Accounting + phòng kế toán): lập phiếu, hiện QR ngay trên điện thoại cho người nhận
 *   quét, rồi bấm "Duyệt chi". Nút duyệt chi chỉ bật khi phiếu đã được ký nhận — server cũng chặn.
 */
@Composable
fun PayoutScreen(vm: HrViewModel) {
    val state = vm.payoutState
    val cashier = vm.isCashier
    var creating by remember { mutableStateOf(false) }
    var confirmApprove by remember { mutableStateOf<PayoutVoucher?>(null) }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        state.message?.let { msg ->
            item { NoticeCard(msg, Tone.Success) { vm.clearPayoutMessage() } }
        }
        state.error?.let { err ->
            item { NoticeCard(err, Tone.Danger) { vm.clearPayoutMessage() } }
        }

        if (cashier) {
            item {
                Button(
                    onClick = { creating = true },
                    modifier = Modifier.fillMaxWidth(),
                    shape = RoundedCornerShape(14.dp),
                    enabled = !state.busy,
                ) {
                    Icon(Icons.Filled.Add, contentDescription = null, modifier = Modifier.size(18.dp))
                    Spacer(Modifier.width(8.dp))
                    Text("Lập phiếu chi", fontWeight = FontWeight.Bold)
                }
            }
        }

        if (state.loading && state.items.isEmpty()) item { LoadingBlock() }

        if (state.items.isEmpty() && !state.loading) {
            item {
                EmptyState(
                    if (cashier) "Chưa có phiếu chi nào" else "Bạn chưa có phiếu chi nào",
                    if (cashier) "Bấm “Lập phiếu chi” để tạo phiếu mới."
                    else "Khi kế toán lập phiếu chi cho bạn, phiếu sẽ hiện ở đây.",
                )
            }
        } else {
            items(state.items, key = { it.id }) { v ->
                PayoutCard(
                    v = v,
                    cashier = cashier,
                    busy = state.busy,
                    onQr = { vm.openPayoutQr(v) },
                    onApprove = { confirmApprove = v },
                    onCancel = { vm.cancelPayout(v) },
                )
            }
        }
    }

    state.qrVoucher?.let { voucher ->
        PayoutQrDialog(
            voucher = voucher,
            busy = state.busy,
            onRefresh = { vm.refreshPayoutQr(voucher) },
            onClose = { vm.closePayoutQr() },
        )
    }

    if (creating) {
        CreatePayoutDialog(vm = vm, onClose = { creating = false })
    }

    confirmApprove?.let { v ->
        AlertDialog(
            onDismissRequest = { confirmApprove = null },
            title = { Text("Duyệt chi phiếu này?") },
            text = { Text("${v.voucherNo} · ${formatMoney(v.amount)} cho ${v.employeeName}. Người nhận đã ký nhận.") },
            confirmButton = {
                TextButton(onClick = { vm.approvePayout(v); confirmApprove = null }) { Text("Duyệt chi") }
            },
            dismissButton = { TextButton(onClick = { confirmApprove = null }) { Text("Hủy") } },
        )
    }
}

@Composable
private fun NoticeCard(text: String, tone: Tone, onDismiss: () -> Unit) {
    HrCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                text,
                style = MaterialTheme.typography.bodyMedium,
                color = toneColor(tone),
                modifier = Modifier.weight(1f),
            )
            TextButton(onClick = onDismiss) { Text("Đóng") }
        }
    }
}

private fun statusTone(status: String): Tone = when (status) {
    "AwaitingScan" -> Tone.Warning
    "Confirmed" -> Tone.Info
    "Paid" -> Tone.Success
    else -> Tone.Muted
}

private fun statusLabel(status: String): String = when (status) {
    "AwaitingScan" -> "Chờ quét QR"
    "Confirmed" -> "Đã ký nhận"
    "Paid" -> "Đã chi"
    "Cancelled" -> "Đã hủy"
    else -> status
}

@Composable
private fun PayoutCard(
    v: PayoutVoucher,
    cashier: Boolean,
    busy: Boolean,
    onQr: () -> Unit,
    onApprove: () -> Unit,
    onCancel: () -> Unit,
) {
    HrCard {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    if (cashier) v.employeeName else v.categoryName,
                    style = MaterialTheme.typography.titleMedium,
                    color = MaterialTheme.colorScheme.onSurface,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    "${v.voucherNo} · ${formatIsoDate(v.createdAt)}" + if (cashier) " · ${v.categoryName}" else "",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            StatusChip(statusLabel(v.status), statusTone(v.status))
        }

        Text(
            v.reason.ifBlank { "Không có nội dung" },
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurface,
            maxLines = 2,
            overflow = TextOverflow.Ellipsis,
        )
        StatusChip(formatMoney(v.amount), Tone.Warning)

        if (v.status == "AwaitingScan" && !cashier) {
            Text(
                "Tới phòng kế toán nhận tiền, rồi bấm nút quét QR ở đầu màn hình để xác nhận đã nhận.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        v.confirmedAt?.takeIf { v.status == "Confirmed" }?.let {
            Text(
                "Đã ký nhận lúc ${formatIsoDateTime(it)}",
                style = MaterialTheme.typography.bodySmall,
                color = toneColor(Tone.Success),
            )
        }
        v.paidAt?.takeIf { v.status == "Paid" }?.let {
            Text(
                "Đã chi lúc ${formatIsoDateTime(it)}",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }

        if (cashier && v.status != "Paid" && v.status != "Cancelled") {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                if (v.status == "AwaitingScan") {
                    OutlinedButton(
                        onClick = onQr,
                        modifier = Modifier.weight(1f),
                        shape = RoundedCornerShape(12.dp),
                        enabled = !busy,
                    ) {
                        Icon(Icons.Filled.QrCode2, contentDescription = null, modifier = Modifier.size(18.dp))
                        Spacer(Modifier.width(6.dp))
                        Text("Mã QR")
                    }
                }
                Button(
                    onClick = onApprove,
                    modifier = Modifier.weight(1f),
                    shape = RoundedCornerShape(12.dp),
                    // Chốt chống gian lận: chưa ký nhận thì không duyệt chi được (server cũng chặn).
                    enabled = !busy && v.status == "Confirmed",
                ) {
                    Icon(Icons.Filled.Payments, contentDescription = null, modifier = Modifier.size(18.dp))
                    Spacer(Modifier.width(6.dp))
                    Text("Duyệt chi")
                }
            }
            TextButton(onClick = onCancel, enabled = !busy) { Text("Hủy phiếu") }
        }
    }
}

/**
 * Vẽ mã QR bằng `zxing:core` sẵn có thay vì `BarcodeEncoder` của zxing-android-embedded — nhờ vậy bỏ
 * được hẳn thư viện embedded (kèm cụm Camera1 + resource của nó) khỏi APK.
 */
private fun encodeQrBitmap(content: String, size: Int): android.graphics.Bitmap? = runCatching {
    val matrix = QRCodeWriter().encode(
        content,
        BarcodeFormat.QR_CODE,
        size,
        size,
        mapOf(EncodeHintType.MARGIN to 1, EncodeHintType.CHARACTER_SET to "UTF-8"),
    )
    val pixels = IntArray(matrix.width * matrix.height)
    for (y in 0 until matrix.height) {
        val row = y * matrix.width
        for (x in 0 until matrix.width) {
            pixels[row + x] = if (matrix.get(x, y)) android.graphics.Color.BLACK else android.graphics.Color.WHITE
        }
    }
    android.graphics.Bitmap.createBitmap(matrix.width, matrix.height, android.graphics.Bitmap.Config.ARGB_8888)
        .apply { setPixels(pixels, 0, matrix.width, 0, 0, matrix.width, matrix.height) }
}.getOrNull()

/** Mã QR to, rõ, nền trắng để người nhận quét bằng máy của họ. */
@Composable
private fun PayoutQrDialog(voucher: PayoutVoucher, busy: Boolean, onRefresh: () -> Unit, onClose: () -> Unit) {
    val qr = voucher.qrValue
    val bitmap = remember(qr) {
        qr?.takeIf { it.isNotBlank() }?.let { encodeQrBitmap(it, 720) }
    }

    AlertDialog(
        onDismissRequest = onClose,
        title = { Text("Phiếu chi ${voucher.voucherNo}") },
        text = {
            Column(
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(10.dp),
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text(voucher.employeeName, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                Text(formatMoney(voucher.amount), style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Black)
                if (voucher.reason.isNotBlank()) {
                    Text(
                        voucher.reason,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                if (bitmap != null) {
                    Box(
                        modifier = Modifier
                            .background(Color.White, RoundedCornerShape(12.dp))
                            .padding(10.dp),
                    ) {
                        Image(
                            bitmap = bitmap.asImageBitmap(),
                            contentDescription = "Mã QR phiếu chi ${voucher.voucherNo}",
                            modifier = Modifier.size(240.dp),
                        )
                    }
                    Text(
                        "Đưa mã này cho ${voucher.employeeName} quét để ký nhận đã cầm tiền. Ký nhận xong bạn mới duyệt chi được.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                } else {
                    Text(
                        "Mã QR đã hết hạn hoặc chưa tạo được.",
                        style = MaterialTheme.typography.bodyMedium,
                        color = toneColor(Tone.Warning),
                    )
                }
                if (busy) CircularProgressIndicator(modifier = Modifier.size(22.dp))
            }
        },
        confirmButton = { TextButton(onClick = onClose) { Text("Đóng") } },
        dismissButton = {
            TextButton(onClick = onRefresh, enabled = !busy) {
                Icon(Icons.Filled.Refresh, contentDescription = null, modifier = Modifier.size(16.dp))
                Spacer(Modifier.width(6.dp))
                Text("Tạo lại mã")
            }
        },
    )
}

/** Lập phiếu: chọn khoản hoàn đang chờ (tự ra số tiền) hoặc nhập tay khoản chi khác. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun CreatePayoutDialog(vm: HrViewModel, onClose: () -> Unit) {
    val state = vm.payoutState
    var fromRefund by remember { mutableStateOf(true) }
    var refundId by remember { mutableStateOf("") }
    var categoryId by remember { mutableStateOf("") }
    var employeeId by remember { mutableStateOf("") }
    var amount by remember { mutableStateOf("") }
    var reason by remember { mutableStateOf("") }

    // Không có khoản hoàn nào chờ thì mở thẳng chế độ nhập tay cho đỡ phải bấm.
    LaunchedEffect(state.refundSources) {
        if (state.refundSources.isEmpty()) fromRefund = false
    }
    LaunchedEffect(state.categories) {
        if (categoryId.isBlank()) {
            categoryId = (state.categories.firstOrNull { !it.isSystem } ?: state.categories.firstOrNull())?.id.orEmpty()
        }
    }

    val picked = state.refundSources.firstOrNull { it.id == refundId }
    val canSubmit = if (fromRefund) refundId.isNotBlank()
    else employeeId.isNotBlank() && (amount.toDoubleOrNull() ?: 0.0) > 0 && reason.isNotBlank() && categoryId.isNotBlank()

    AlertDialog(
        onDismissRequest = onClose,
        title = { Text("Lập phiếu chi") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.fillMaxWidth()) {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    FilterChip(
                        selected = fromRefund,
                        onClick = { fromRefund = true },
                        label = { Text("Khoản hoàn (${state.refundSources.size})") },
                    )
                    FilterChip(
                        selected = !fromRefund,
                        onClick = { fromRefund = false },
                        label = { Text("Khoản chi khác") },
                    )
                }

                if (fromRefund) {
                    if (state.refundSources.isEmpty()) {
                        Text(
                            "Không có khoản hoàn nào đang chờ chi.",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    } else {
                        PickerField(
                            label = "Đơn hoàn tiền phạt",
                            selectedText = picked?.let { "${it.employeeName} · ${formatMoney(it.amount)}" }.orEmpty(),
                            options = state.refundSources.map { it.id to "${it.employeeName} · ${it.refundNo} · ${formatMoney(it.amount)}" },
                            onPick = { refundId = it },
                        )
                        picked?.let {
                            Text(
                                "Số tiền phải chi: ${formatMoney(it.amount)}",
                                style = MaterialTheme.typography.titleMedium,
                                fontWeight = FontWeight.Bold,
                            )
                            Text(
                                "${it.employeeName} (${it.employeeCode}) · phạt ${it.penaltyNo}",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                } else {
                    PickerField(
                        label = "Loại chi",
                        selectedText = state.categories.firstOrNull { it.id == categoryId }?.name.orEmpty(),
                        options = state.categories.map { it.id to it.name },
                        onPick = { categoryId = it },
                    )
                    PickerField(
                        label = "Người nhận tiền",
                        selectedText = state.recipients.firstOrNull { it.id == employeeId }?.fullName.orEmpty(),
                        options = state.recipients.map { it.id to (it.fullName + if (it.departmentName.isNotBlank()) " · ${it.departmentName}" else "") },
                        onPick = { employeeId = it },
                    )
                    OutlinedTextField(
                        value = amount,
                        onValueChange = { amount = it.filter { c -> c.isDigit() } },
                        label = { Text("Số tiền (₫)") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth(),
                    )
                }

                OutlinedTextField(
                    value = reason,
                    onValueChange = { reason = it },
                    label = { Text(if (fromRefund) "Nội dung (tùy chọn)" else "Nội dung chi") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        },
        confirmButton = {
            TextButton(
                enabled = canSubmit && !state.busy,
                onClick = {
                    val body = if (fromRefund) {
                        CreatePayoutBody(sourceKind = "refund", sourceId = refundId, reason = reason.trim())
                    } else {
                        CreatePayoutBody(
                            sourceKind = "manual",
                            categoryId = categoryId,
                            employeeId = employeeId,
                            amount = amount.toDoubleOrNull() ?: 0.0,
                            reason = reason.trim(),
                        )
                    }
                    vm.createPayout(body) { onClose() }
                },
            ) { Text("Lập phiếu") }
        },
        dismissButton = { TextButton(onClick = onClose) { Text("Đóng") } },
    )
}

/** Ô chọn dạng dropdown dùng chung cho các danh sách ngắn trong hộp lập phiếu. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun PickerField(
    label: String,
    selectedText: String,
    options: List<Pair<String, String>>,
    onPick: (String) -> Unit,
) {
    var expanded by remember { mutableStateOf(false) }
    ExposedDropdownMenuBox(expanded = expanded, onExpandedChange = { expanded = it }) {
        OutlinedTextField(
            value = selectedText,
            onValueChange = {},
            readOnly = true,
            label = { Text(label) },
            trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = expanded) },
            modifier = Modifier
                .fillMaxWidth()
                .menuAnchor(),
        )
        ExposedDropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            options.forEach { (id, text) ->
                DropdownMenuItem(
                    text = { Text(text) },
                    onClick = {
                        onPick(id)
                        expanded = false
                    },
                )
            }
        }
    }
}
