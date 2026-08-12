package com.ketoanapk.hr.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.CloudDownload
import androidx.compose.material.icons.filled.ErrorOutline
import androidx.compose.material.icons.filled.SignalCellularAlt
import androidx.compose.material.icons.filled.SystemUpdate
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.SheetValue
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import com.ketoanapk.hr.data.AppUpdater
import com.ketoanapk.hr.data.ReleaseInfo

/**
 * Thanh nhắc nhỏ, cố định trong khung ứng dụng khi có bản mới.
 *
 * Thanh này không phụ thuộc quyền thông báo Android và không có nút đóng: người dùng có thể hoãn
 * bảng chi tiết, nhưng vẫn luôn nhìn thấy đường quay lại cập nhật trên mọi màn hình của ứng dụng.
 */
@Composable
fun UpdateReminderBar(
    info: ReleaseInfo,
    onOpen: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Surface(
        modifier = modifier
            .fillMaxWidth()
            .clickable(onClick = onOpen),
        color = MaterialTheme.colorScheme.primaryContainer,
        contentColor = MaterialTheme.colorScheme.onPrimaryContainer,
        tonalElevation = 2.dp,
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .heightIn(min = 40.dp)
                .padding(horizontal = 14.dp, vertical = 7.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(9.dp),
        ) {
            Icon(
                Icons.Filled.SystemUpdate,
                contentDescription = null,
                modifier = Modifier.size(19.dp),
            )
            Text(
                "Có bản cập nhật ${info.version}",
                modifier = Modifier.weight(1f),
                style = MaterialTheme.typography.bodySmall,
                fontWeight = FontWeight.SemiBold,
                maxLines = 1,
            )
            Text(
                "Xem ngay",
                style = MaterialTheme.typography.labelMedium,
                fontWeight = FontWeight.Bold,
            )
        }
    }
}

/**
 * Bảng cập nhật ứng dụng — thay cho hộp thoại cũ.
 *
 * Điểm khác quan trọng: bảng **ở lại suốt quá trình tải** và vẽ tiến độ thật (MB đã tải / tổng, %).
 * Hộp thoại cũ đóng ngay khi bấm "Cập nhật ngay" rồi mới tải ngầm gói ~90 MB, nên người dùng nhìn thấy
 * "bấm xong không có gì xảy ra" trong vài phút và tưởng ứng dụng bị lỗi.
 *
 * Bản BẮT BUỘC ([ReleaseInfo.isMandatory]) không cho vuốt xuống hay bấm ra ngoài để đóng.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun UpdateSheet(
    info: ReleaseInfo,
    stage: UpdateStage,
    needsMeteredConsent: Boolean,
    onDownload: () -> Unit,
    onAcceptMetered: () -> Unit,
    onRetry: () -> Unit,
    onDismiss: () -> Unit,
) {
    val context = LocalContext.current
    val busy = stage is UpdateStage.Preparing || stage is UpdateStage.Downloading
    // Đang tải hoặc bản bắt buộc thì không cho vuốt đóng — tránh mất dấu tiến trình đang chạy.
    // Phải chặn ngay trong [confirmValueChange]: onDismissRequest chỉ bắt được lượt chạm nền, còn cử chỉ
    // vuốt xuống do chính ModalBottomSheet xử lý nên nếu không chặn ở đây thì bản BẮT BUỘC vẫn vuốt đi được.
    val closable = !info.isMandatory && !busy
    val sheetState = rememberModalBottomSheetState(
        skipPartiallyExpanded = true,
        confirmValueChange = { target -> target != SheetValue.Hidden || closable },
    )
    val installedVersion = remember { AppUpdater.installedVersionName(context) }

    ModalBottomSheet(
        onDismissRequest = { if (closable) onDismiss() },
        sheetState = sheetState,
        dragHandle = null,
        containerColor = MaterialTheme.colorScheme.surface,
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .navigationBarsPadding()
                .padding(horizontal = 20.dp)
                .padding(top = 24.dp, bottom = 20.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            UpdateHeader(info = info, installedVersion = installedVersion, sizeText = AppUpdater.formatSize(context, info.apkSize))

            if (info.releaseNotes.isNotBlank()) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(max = 180.dp)
                        .verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(4.dp),
                ) {
                    Text(
                        "Có gì mới",
                        style = MaterialTheme.typography.labelLarge,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    Text(
                        info.releaseNotes.trim(),
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurface,
                    )
                }
            }

            when (stage) {
                is UpdateStage.Preparing -> BusyRow("Đang kiểm tra gói cập nhật…")
                is UpdateStage.Downloading -> DownloadProgress(
                    downloaded = stage.downloaded,
                    total = stage.total,
                    sizeText = { AppUpdater.formatSize(context, it) },
                )
                is UpdateStage.Installing -> NoticeCard(
                    icon = Icons.Filled.CheckCircle,
                    title = "Đã tải xong",
                    body = "Làm theo màn hình xác nhận cài đặt của hệ thống. Nếu màn đó đã tắt, bấm \"Mở lại trình cài đặt\".",
                    container = MaterialTheme.colorScheme.secondaryContainer,
                    content = MaterialTheme.colorScheme.onSecondaryContainer,
                )
                is UpdateStage.Failed -> NoticeCard(
                    icon = Icons.Filled.ErrorOutline,
                    title = "Chưa cập nhật được",
                    body = stage.message,
                    container = MaterialTheme.colorScheme.errorContainer,
                    content = MaterialTheme.colorScheme.onErrorContainer,
                )
                UpdateStage.Idle -> if (needsMeteredConsent) {
                    NoticeCard(
                        icon = Icons.Filled.SignalCellularAlt,
                        title = "Bạn đang dùng dữ liệu di động",
                        body = "Gói này khoảng ${AppUpdater.formatSize(context, info.apkSize)}, tải bằng 3G/4G có thể tốn cước. " +
                            "Nên chờ Wi-Fi nếu không gấp.",
                        container = MaterialTheme.colorScheme.tertiaryContainer,
                        content = MaterialTheme.colorScheme.onTertiaryContainer,
                    )
                }
            }

            UpdateActions(
                stage = stage,
                mandatory = info.isMandatory,
                needsMeteredConsent = needsMeteredConsent,
                onDownload = onDownload,
                onAcceptMetered = onAcceptMetered,
                onRetry = onRetry,
                onDismiss = onDismiss,
            )
        }
    }
}

/** Đầu bảng: biểu tượng, tên bản, và bước nhảy phiên bản "đang dùng → bản mới" + dung lượng gói. */
@Composable
private fun UpdateHeader(info: ReleaseInfo, installedVersion: String, sizeText: String) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
        Box(
            modifier = Modifier
                .size(52.dp)
                .background(MaterialTheme.colorScheme.primaryContainer, RoundedCornerShape(16.dp)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(
                Icons.Filled.SystemUpdate,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.onPrimaryContainer,
                modifier = Modifier.size(28.dp),
            )
        }
        Column(verticalArrangement = Arrangement.spacedBy(3.dp)) {
            Text(
                if (info.isMandatory) "Bản cập nhật bắt buộc" else "Đã có bản cập nhật mới",
                style = MaterialTheme.typography.titleLarge,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.onSurface,
            )
            Text(
                "Phiên bản $installedVersion  →  ${info.version}  ·  $sizeText",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            if (info.isMandatory) {
                Text(
                    "Cần cập nhật để tiếp tục sử dụng ứng dụng.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.error,
                )
            }
        }
    }
}

/** Thanh tiến độ tải kèm số liệu thật — thứ mà luồng cũ hoàn toàn không có. */
@Composable
private fun DownloadProgress(downloaded: Long, total: Long, sizeText: (Long) -> String) {
    val fraction = if (total > 0) (downloaded.toFloat() / total).coerceIn(0f, 1f) else 0f
    val animated by animateFloatAsState(targetValue = fraction, animationSpec = tween(220), label = "updateProgress")
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(
                "Đang tải bản cập nhật…",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurface,
            )
            Text(
                if (total > 0) "${(fraction * 100).toInt()}%" else sizeText(downloaded),
                style = MaterialTheme.typography.bodyMedium,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.primary,
            )
        }
        // Tổng chưa biết (máy chủ không trả Content-Length) → chạy vô định thay vì đứng ở 0%.
        if (total > 0) {
            LinearProgressIndicator(
                progress = { animated },
                modifier = Modifier
                    .fillMaxWidth()
                    .height(8.dp),
                strokeCap = androidx.compose.ui.graphics.StrokeCap.Round,
            )
            Text(
                "${sizeText(downloaded)} / ${sizeText(total)}",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        } else {
            LinearProgressIndicator(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(8.dp),
                strokeCap = androidx.compose.ui.graphics.StrokeCap.Round,
            )
        }
        Text(
            "Giữ ứng dụng mở cho tới khi tải xong.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

@Composable
private fun BusyRow(text: String) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        CircularProgressIndicator(Modifier.size(20.dp), MaterialTheme.colorScheme.primary, 2.dp)
        Text(text, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurface)
    }
}

/** Khối thông tin có màu (thành công / lỗi / cảnh báo cước) dùng chung cho các bước. */
@Composable
private fun NoticeCard(
    icon: ImageVector,
    title: String,
    body: String,
    container: Color,
    content: Color,
) {
    Surface(color = container, shape = RoundedCornerShape(16.dp), modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier.padding(14.dp),
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Icon(icon, contentDescription = null, tint = content, modifier = Modifier.size(22.dp))
            Column(verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Text(title, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold, color = content)
                Text(body, style = MaterialTheme.typography.bodySmall, color = content)
            }
        }
    }
}

/** Hàng nút dưới cùng — nhãn đổi theo bước hiện tại để luôn nói đúng việc sắp xảy ra. */
@Composable
private fun UpdateActions(
    stage: UpdateStage,
    mandatory: Boolean,
    needsMeteredConsent: Boolean,
    onDownload: () -> Unit,
    onAcceptMetered: () -> Unit,
    onRetry: () -> Unit,
    onDismiss: () -> Unit,
) {
    val busy = stage is UpdateStage.Preparing || stage is UpdateStage.Downloading
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        val (label, action) = when {
            busy -> "Đang tải…" to null
            stage is UpdateStage.Installing -> "Mở lại trình cài đặt" to onRetry
            stage is UpdateStage.Failed -> "Thử lại" to onRetry
            needsMeteredConsent -> "Tải bằng dữ liệu di động" to onAcceptMetered
            else -> "Cập nhật ngay" to onDownload
        }
        Button(
            onClick = { action?.invoke() },
            enabled = action != null,
            shape = RoundedCornerShape(14.dp),
            modifier = Modifier
                .fillMaxWidth()
                .height(52.dp),
        ) {
            if (busy) {
                CircularProgressIndicator(Modifier.size(20.dp), MaterialTheme.colorScheme.onPrimary, 2.dp)
                Spacer(Modifier.width(10.dp))
            } else {
                Icon(Icons.Filled.CloudDownload, contentDescription = null, modifier = Modifier.size(20.dp))
                Spacer(Modifier.width(8.dp))
            }
            Text(label, fontWeight = FontWeight.Bold)
        }

        // Bản bắt buộc không có đường thoát; đang tải cũng không cho đóng để khỏi mất dấu tiến độ.
        if (!mandatory && !busy) {
            if (stage is UpdateStage.Installing) {
                OutlinedButton(
                    onClick = onDismiss,
                    shape = RoundedCornerShape(14.dp),
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(48.dp),
                ) { Text("Đóng") }
            } else {
                TextButton(onClick = onDismiss, modifier = Modifier.fillMaxWidth()) {
                    Text(if (needsMeteredConsent) "Để sau (chờ Wi-Fi)" else "Để sau")
                }
            }
        }
    }
}
