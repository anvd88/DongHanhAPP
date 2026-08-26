package com.ketoanapk.hr.ui

import android.app.Activity
import android.content.Context
import android.content.ContextWrapper
import android.view.WindowManager
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Payments
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Schedule
import androidx.compose.material.icons.filled.Shield
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.ketoanapk.hr.data.PayslipItem
import com.ketoanapk.hr.ui.theme.Danger
import com.ketoanapk.hr.ui.theme.Success
import com.ketoanapk.hr.ui.theme.Warning
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter

/**
 * Màn xác nhận phiếu lương độc lập. Đây không phải biến thể của kho phiếu/[MyPayslipsScreen]:
 * nó có vòng đời, xác thực PIN, nội dung đối soát và nút ghi nhận riêng. Khi [required] là true,
 * màn phủ toàn bộ ứng dụng và không thể đóng trước khi máy chủ ghi nhận xác nhận.
 */
@Composable
fun PayslipConfirmationScreen(
    reviewKey: String,
    period: String,
    dueAt: String,
    required: Boolean,
    remainingOverdueCount: Int,
    payslip: PayslipItem?,
    loading: Boolean,
    loadError: String?,
    statusMessage: String?,
    submitting: Boolean,
    awaitingSync: Boolean,
    username: String,
    onRetry: () -> Unit,
    onConfirm: (PayslipItem) -> Unit,
    onInquiry: (String, String, String) -> Unit,
    onDownload: (PayslipItem) -> Unit,
    onClose: () -> Unit,
    onVerifyAccountPassword: (String, (Boolean, String?) -> Unit) -> Unit,
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    // Không save qua Activity/process recreation: quay lại dữ liệu lương luôn phải xác thực PIN lại.
    var unlocked by remember(reviewKey) { mutableStateOf(false) }
    var showPin by remember(reviewKey) { mutableStateOf(true) }
    var checkedFigures by remember(reviewKey) { mutableStateOf(false) }
    var inquiryOpen by remember(reviewKey) { mutableStateOf(false) }
    var inquiryText by remember(reviewKey) { mutableStateOf("") }

    BackHandler(enabled = true) {
        if (!required && !submitting) onClose()
    }

    // Phiếu lương là dữ liệu nhạy cảm: chặn screenshot/recent-app preview trong toàn bộ màn riêng này.
    DisposableEffect(context) {
        val activity = context.findPayslipConfirmationActivity()
        val window = activity?.window
        val wasSecure = ((window?.attributes?.flags ?: 0) and WindowManager.LayoutParams.FLAG_SECURE) != 0
        if (!wasSecure) window?.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        onDispose { if (!wasSecure) window?.clearFlags(WindowManager.LayoutParams.FLAG_SECURE) }
    }

    // Rời app/PDF rồi quay lại phải xác thực lại; không để một phiên unlock sống qua background.
    DisposableEffect(lifecycleOwner, reviewKey) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_STOP) {
                unlocked = false
                showPin = true
                checkedFigures = false
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
    }

    LaunchedEffect(reviewKey, payslip) {
        if (payslip == null) onRetry()
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(
                Brush.verticalGradient(
                    listOf(
                        MaterialTheme.colorScheme.primaryContainer,
                        MaterialTheme.colorScheme.background,
                        MaterialTheme.colorScheme.background,
                    ),
                ),
            )
            .statusBarsPadding()
            .navigationBarsPadding(),
    ) {
        PayslipConfirmationHeader(
            period = payslip?.period ?: period,
            dueAt = payslip?.acknowledgementDueAt?.ifBlank { dueAt } ?: dueAt,
            required = required,
            canClose = !required && !submitting,
            onClose = onClose,
        )

        when {
            !unlocked -> PayslipConfirmationLockedIntro(
                required = required,
                onUnlock = { showPin = true },
            )

            payslip != null -> PayslipConfirmationReview(
                payslip = payslip,
                required = required,
                remainingOverdueCount = remainingOverdueCount,
                checkedFigures = checkedFigures,
                onCheckedFigures = { checkedFigures = it },
                loadError = loadError,
                statusMessage = statusMessage,
                submitting = submitting,
                awaitingSync = awaitingSync,
                onConfirm = { onConfirm(payslip) },
                onRetry = onRetry,
                onInquiry = { inquiryOpen = true },
                onDownload = { onDownload(payslip) },
            )

            loading -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    CircularProgressIndicator()
                    Text("Đang tải phiếu cần xác nhận…", color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }

            else -> PayslipConfirmationLoadError(loadError, onRetry)
        }
    }

    AppPinGate(
        visible = showPin && !unlocked,
        username = username,
        purpose = "Xác thực để mở màn hình xác nhận phiếu lương.",
        onDismiss = { showPin = false },
        onUnlocked = {
            unlocked = true
            showPin = false
        },
        onVerifyAccountPassword = onVerifyAccountPassword,
    )

    if (inquiryOpen && payslip != null) {
        AlertDialog(
            onDismissRequest = { inquiryOpen = false },
            title = { Text("Gửi thắc mắc về phiếu lương") },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    Text(
                        "Xác nhận phiếu lương không làm mất quyền khiếu nại. Bạn vẫn có thể gửi nội dung cần bộ phận lương kiểm tra.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    OutlinedTextField(
                        value = inquiryText,
                        onValueChange = { inquiryText = it.take(1000) },
                        minLines = 4,
                        label = { Text("Nội dung cần kiểm tra") },
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            },
            confirmButton = {
                Button(
                    enabled = inquiryText.isNotBlank(),
                    onClick = {
                        onInquiry(payslip.id, "Xác nhận phiếu lương", inquiryText.trim())
                        inquiryText = ""
                        inquiryOpen = false
                    },
                ) { Text("Gửi thắc mắc") }
            },
            dismissButton = { TextButton(onClick = { inquiryOpen = false }) { Text("Đóng") } },
        )
    }
}

@Composable
private fun PayslipConfirmationHeader(
    period: String,
    dueAt: String,
    required: Boolean,
    canClose: Boolean,
    onClose: () -> Unit,
) {
    Surface(color = Color.Transparent) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            if (canClose) {
                IconButton(onClick = onClose) {
                    Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Đóng màn xác nhận")
                }
            } else {
                Surface(shape = CircleShape, color = MaterialTheme.colorScheme.primary.copy(alpha = 0.14f)) {
                    Icon(
                        Icons.Filled.Lock,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.padding(10.dp).size(22.dp),
                    )
                }
            }
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(
                    if (required) "Xác nhận để mở khóa ứng dụng" else "Xác nhận phiếu lương",
                    style = MaterialTheme.typography.titleLarge,
                    color = MaterialTheme.colorScheme.onSurface,
                    fontWeight = FontWeight.ExtraBold,
                )
                Text(
                    buildString {
                        append(if (period.isBlank()) "Phiếu lương cần xác nhận" else formatTimesheetPeriod(period))
                        if (dueAt.isNotBlank()) append(" · Hạn ${formatConfirmationDeadline(dueAt)}")
                    },
                    style = MaterialTheme.typography.bodySmall,
                    color = if (required) Danger else MaterialTheme.colorScheme.onSurfaceVariant,
                    fontWeight = if (required) FontWeight.SemiBold else FontWeight.Normal,
                )
            }
        }
    }
}

@Composable
private fun PayslipConfirmationLockedIntro(required: Boolean, onUnlock: () -> Unit) {
    Box(modifier = Modifier.fillMaxSize().padding(20.dp), contentAlignment = Alignment.Center) {
        Surface(
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(28.dp),
            color = MaterialTheme.colorScheme.surface,
            border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
            shadowElevation = 8.dp,
        ) {
            Column(
                modifier = Modifier.padding(24.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(14.dp),
            ) {
                Box(
                    modifier = Modifier.size(76.dp).background(MaterialTheme.colorScheme.primaryContainer, CircleShape),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(Icons.Filled.Shield, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(38.dp))
                }
                Text(
                    "Màn xác nhận bảo mật",
                    style = MaterialTheme.typography.headlineSmall,
                    color = MaterialTheme.colorScheme.onSurface,
                    fontWeight = FontWeight.ExtraBold,
                    textAlign = TextAlign.Center,
                )
                Text(
                    if (required)
                        "Ứng dụng đang tạm khóa vì phiếu đã quá hạn xác nhận. Xác thực mã bảo mật để kiểm tra và xác nhận phiếu lương."
                    else
                        "Xác thực mã bảo mật trước khi xem số tiền và xác nhận phiếu lương.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    textAlign = TextAlign.Center,
                )
                Button(onClick = onUnlock, modifier = Modifier.fillMaxWidth().height(52.dp), shape = RoundedCornerShape(16.dp)) {
                    Icon(Icons.Filled.Lock, contentDescription = null, modifier = Modifier.size(19.dp))
                    Spacer(Modifier.size(8.dp))
                    Text("Xác thực và tiếp tục", fontWeight = FontWeight.Bold)
                }
            }
        }
    }
}

@Composable
private fun PayslipConfirmationReview(
    payslip: PayslipItem,
    required: Boolean,
    remainingOverdueCount: Int,
    checkedFigures: Boolean,
    onCheckedFigures: (Boolean) -> Unit,
    loadError: String?,
    statusMessage: String?,
    submitting: Boolean,
    awaitingSync: Boolean,
    onConfirm: () -> Unit,
    onRetry: () -> Unit,
    onInquiry: () -> Unit,
    onDownload: () -> Unit,
) {
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 6.dp, bottom = 24.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        item { PayslipConfirmationSteps() }
        if (required && remainingOverdueCount > 1) {
            item {
                Surface(
                    shape = RoundedCornerShape(16.dp),
                    color = Warning.copy(alpha = 0.11f),
                    border = BorderStroke(1.dp, Warning.copy(alpha = 0.35f)),
                ) {
                    Text(
                        "Bạn còn $remainingOverdueCount phiếu quá hạn. Xác nhận xong phiếu này, ứng dụng sẽ chuyển sang phiếu tiếp theo.",
                        modifier = Modifier.padding(14.dp),
                        color = MaterialTheme.colorScheme.onSurface,
                        style = MaterialTheme.typography.bodyMedium,
                        fontWeight = FontWeight.SemiBold,
                    )
                }
            }
        }
        statusMessage?.takeIf { it.isNotBlank() }?.let { message ->
            item {
                Surface(shape = RoundedCornerShape(16.dp), color = Success.copy(alpha = 0.11f)) {
                    Text(message, modifier = Modifier.padding(14.dp), color = Success, fontWeight = FontWeight.SemiBold)
                }
            }
        }
        loadError?.takeIf { it.isNotBlank() }?.let { message ->
            item {
                Surface(shape = RoundedCornerShape(16.dp), color = MaterialTheme.colorScheme.errorContainer) {
                    Column(modifier = Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(message, color = MaterialTheme.colorScheme.onErrorContainer, style = MaterialTheme.typography.bodyMedium)
                        TextButton(onClick = onRetry, enabled = !submitting) { Text("Thử đồng bộ lại") }
                    }
                }
            }
        }
        item { ConfirmationNetPayCard(payslip) }
        item { ConfirmationEquationCard(payslip) }
        item { ConfirmationWorkCard(payslip) }
        if (payslip.note.isNotBlank()) {
            item {
                ConfirmationSectionCard("Ghi chú từ bộ phận lương") {
                    Text(payslip.note, color = MaterialTheme.colorScheme.onSurface, style = MaterialTheme.typography.bodyMedium)
                }
            }
        }
        item {
            ConfirmationSectionCard("Ghi nhận của nhân viên") {
                ConfirmationCheckRow(
                    checked = checkedFigures,
                    onChecked = onCheckedFigures,
                    text = "Tôi đã kiểm tra số ngày công, tăng ca, tổng thu nhập, khấu trừ và thực nhận trên màn hình này và xác nhận phiếu lương.",
                )
            }
        }
        item {
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedButton(onClick = onDownload, modifier = Modifier.weight(1f)) {
                    Icon(Icons.Filled.Description, contentDescription = null, modifier = Modifier.size(18.dp))
                    Spacer(Modifier.size(6.dp))
                    Text("Tải PDF")
                }
                OutlinedButton(onClick = onInquiry, modifier = Modifier.weight(1f)) {
                    Icon(Icons.Filled.ErrorOutline, contentDescription = null, modifier = Modifier.size(18.dp))
                    Spacer(Modifier.size(6.dp))
                    Text("Thắc mắc")
                }
            }
        }
        item {
            Button(
                onClick = onConfirm,
                enabled = checkedFigures && !submitting && !awaitingSync,
                modifier = Modifier.fillMaxWidth().height(56.dp),
                shape = RoundedCornerShape(17.dp),
            ) {
                if (submitting || awaitingSync) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(20.dp),
                        strokeWidth = 2.dp,
                        color = MaterialTheme.colorScheme.onPrimary,
                    )
                } else {
                    Icon(Icons.Filled.CheckCircle, contentDescription = null, modifier = Modifier.size(21.dp))
                }
                Spacer(Modifier.size(8.dp))
                Text(
                    if (submitting) "Đang ghi nhận…"
                    else if (awaitingSync) "Đã ghi nhận · đang chờ đồng bộ"
                    else if (required && remainingOverdueCount > 1) "Xác nhận phiếu này và tiếp tục"
                    else if (required) "Xác nhận phiếu lương và mở khóa"
                    else "Xác nhận phiếu lương",
                    fontWeight = FontWeight.ExtraBold,
                )
            }
        }
    }
}

@Composable
private fun PayslipConfirmationSteps() {
    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        ConfirmationStep("1", "Xác thực", true, Modifier.weight(1f))
        ConfirmationStep("2", "Kiểm tra", true, Modifier.weight(1f))
        ConfirmationStep("3", "Xác nhận", false, Modifier.weight(1f))
    }
}

@Composable
private fun ConfirmationStep(number: String, label: String, done: Boolean, modifier: Modifier = Modifier) {
    Surface(modifier = modifier, shape = RoundedCornerShape(14.dp), color = MaterialTheme.colorScheme.surface.copy(alpha = 0.82f)) {
        Row(modifier = Modifier.padding(horizontal = 9.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Surface(shape = CircleShape, color = if (done) Success.copy(alpha = 0.14f) else MaterialTheme.colorScheme.primaryContainer) {
                Text(number, modifier = Modifier.padding(horizontal = 7.dp, vertical = 3.dp), color = if (done) Success else MaterialTheme.colorScheme.primary, fontWeight = FontWeight.Bold)
            }
            Text(label, maxLines = 1, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Bold)
        }
    }
}

@Composable
private fun ConfirmationNetPayCard(payslip: PayslipItem) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(24.dp),
        color = MaterialTheme.colorScheme.primary,
        contentColor = MaterialTheme.colorScheme.onPrimary,
    ) {
        Row(modifier = Modifier.padding(20.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
            Surface(shape = CircleShape, color = MaterialTheme.colorScheme.onPrimary.copy(alpha = 0.16f)) {
                Icon(Icons.Filled.Payments, contentDescription = null, modifier = Modifier.padding(12.dp).size(25.dp))
            }
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Text("THỰC NHẬN", style = MaterialTheme.typography.labelMedium, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onPrimary.copy(alpha = 0.82f))
                Text(formatMoney(payslip.netPay), style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.Black)
                Text(formatTimesheetPeriod(payslip.period), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onPrimary.copy(alpha = 0.82f))
            }
        }
    }
}

@Composable
private fun ConfirmationEquationCard(payslip: PayslipItem) {
    ConfirmationSectionCard("Tóm tắt đối soát") {
        if (payslip.earnings.isNotEmpty()) {
            Text("CÁC KHOẢN CỘNG", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant, fontWeight = FontWeight.Bold)
            payslip.earnings.forEach { line ->
                ConfirmationAmountRow(line.label, line.amount, Success)
            }
            HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
        }
        ConfirmationAmountRow("Tổng thu nhập", payslip.totalEarnings, Success)
        if (payslip.deductions.isNotEmpty()) {
            HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
            Text("CÁC KHOẢN KHẤU TRỪ", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant, fontWeight = FontWeight.Bold)
            payslip.deductions.forEach { line ->
                ConfirmationAmountRow(line.label, line.amount, Danger, negative = true)
            }
        }
        ConfirmationAmountRow("Tổng khấu trừ", payslip.totalDeductions, Danger, negative = true)
        HorizontalDivider(color = MaterialTheme.colorScheme.outline)
        ConfirmationAmountRow("Thực nhận", payslip.netPay, MaterialTheme.colorScheme.primary, emphasized = true)
    }
}

@Composable
private fun ConfirmationAmountRow(label: String, amount: Double, color: Color, negative: Boolean = false, emphasized: Boolean = false) {
    Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Text(label, modifier = Modifier.weight(1f), color = MaterialTheme.colorScheme.onSurfaceVariant, style = if (emphasized) MaterialTheme.typography.titleSmall else MaterialTheme.typography.bodyMedium)
        Text(
            (if (negative) "− " else "") + formatMoney(amount),
            color = color,
            style = if (emphasized) MaterialTheme.typography.titleLarge else MaterialTheme.typography.titleSmall,
            fontWeight = FontWeight.ExtraBold,
        )
    }
}

@Composable
private fun ConfirmationWorkCard(payslip: PayslipItem) {
    ConfirmationSectionCard("Dữ liệu công đã chốt") {
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            ConfirmationMetric("Ngày công", confirmationNumber(payslip.workedDays.toDouble()), Success, Modifier.weight(1f))
            ConfirmationMetric("Giờ làm", if (payslip.totalWorkedHours > 0) "${confirmationNumber(payslip.totalWorkedHours)}h" else "--", MaterialTheme.colorScheme.primary, Modifier.weight(1f))
        }
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            ConfirmationMetric("Tăng ca", "${confirmationNumber(payslip.overtimeHours)}h", Warning, Modifier.weight(1f))
            ConfirmationMetric("Đi muộn", "${payslip.lateDays} ngày", Danger, Modifier.weight(1f))
        }
        if (payslip.overtimePay != 0.0) {
            Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Filled.Schedule, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(19.dp))
                Text("  Tiền tăng ca", modifier = Modifier.weight(1f), color = MaterialTheme.colorScheme.onSurfaceVariant)
                Text(formatMoney(payslip.overtimePay), color = MaterialTheme.colorScheme.primary, fontWeight = FontWeight.ExtraBold)
            }
        }
    }
}

@Composable
private fun ConfirmationMetric(label: String, value: String, color: Color, modifier: Modifier = Modifier) {
    Surface(modifier = modifier, shape = RoundedCornerShape(15.dp), color = color.copy(alpha = 0.09f)) {
        Column(modifier = Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(value, color = color, fontWeight = FontWeight.ExtraBold, style = MaterialTheme.typography.titleMedium)
            Text(label, color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall)
        }
    }
}

@Composable
private fun ConfirmationSectionCard(title: String, content: @Composable ColumnScope.() -> Unit) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        shape = RoundedCornerShape(21.dp),
        color = MaterialTheme.colorScheme.surface,
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline),
    ) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(11.dp)) {
            Text(title, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.ExtraBold)
            content()
        }
    }
}

@Composable
private fun ConfirmationCheckRow(checked: Boolean, onChecked: (Boolean) -> Unit, text: String) {
    Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.Top) {
        Checkbox(checked = checked, onCheckedChange = onChecked)
        Text(text, modifier = Modifier.weight(1f).padding(top = 11.dp, end = 4.dp), color = MaterialTheme.colorScheme.onSurface, style = MaterialTheme.typography.bodyMedium)
    }
}

@Composable
private fun PayslipConfirmationLoadError(message: String?, onRetry: () -> Unit) {
    Box(modifier = Modifier.fillMaxSize().padding(20.dp), contentAlignment = Alignment.Center) {
        Surface(modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(22.dp), color = MaterialTheme.colorScheme.errorContainer) {
            Column(modifier = Modifier.padding(20.dp), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Icon(Icons.Filled.ErrorOutline, contentDescription = null, tint = MaterialTheme.colorScheme.error, modifier = Modifier.size(34.dp))
                Text("Chưa tải được phiếu cần xác nhận", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center)
                Text(message ?: "Kiểm tra kết nối mạng rồi thử lại. Ứng dụng chỉ mở khóa sau khi máy chủ ghi nhận xác nhận.", textAlign = TextAlign.Center, color = MaterialTheme.colorScheme.onErrorContainer)
                Button(onClick = onRetry) {
                    Icon(Icons.Filled.Refresh, contentDescription = null, modifier = Modifier.size(18.dp))
                    Spacer(Modifier.size(7.dp))
                    Text("Thử tải lại")
                }
            }
        }
    }
}

private fun Context.findPayslipConfirmationActivity(): Activity? =
    generateSequence(this) { (it as? ContextWrapper)?.baseContext }
        .filterIsInstance<Activity>()
        .firstOrNull()

private fun formatConfirmationDeadline(value: String): String = runCatching {
    OffsetDateTime.parse(value)
        .atZoneSameInstant(ZoneId.of("Asia/Ho_Chi_Minh"))
        .format(DateTimeFormatter.ofPattern("HH:mm dd/MM/yyyy"))
}.getOrElse { value }

private fun confirmationNumber(value: Double): String =
    if (value % 1.0 == 0.0) value.toLong().toString() else "%.1f".format(value)
