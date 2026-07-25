package com.ketoanapk.hr.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.DoneAll
import androidx.compose.material.icons.filled.Face
import androidx.compose.material.icons.filled.Chat
import androidx.compose.material.icons.filled.Gavel
import androidx.compose.material.icons.filled.Inbox
import androidx.compose.material.icons.filled.NotificationsNone
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.compositeOver
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.ketoanapk.hr.data.AppNotification
import com.ketoanapk.hr.data.NotificationKind
import com.ketoanapk.hr.ui.theme.Danger
import com.ketoanapk.hr.ui.theme.Success
import com.ketoanapk.hr.ui.theme.Warning

@Composable
fun NotificationsScreen(
    notifications: List<AppNotification>,
    onOpen: (AppNotification) -> Unit,
    onMarkAllRead: () -> Unit,
    onClear: () -> Unit,
) {
    val unread = notifications.count { !it.read }
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        item {
            PageHeader(
                Icons.Filled.NotificationsNone,
                "Thông báo",
                if (unread > 0) "$unread thông báo chưa đọc" else "Bạn đã đọc hết thông báo",
                Tone.Neutral,
            )
        }

        if (notifications.isNotEmpty()) {
            item {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    TextButton(onClick = onMarkAllRead, enabled = unread > 0) {
                        Icon(Icons.Filled.DoneAll, contentDescription = null, modifier = Modifier.size(18.dp))
                        Spacer(Modifier.width(6.dp))
                        Text("Đọc tất cả")
                    }
                    TextButton(onClick = onClear) {
                        Text("Xoá hết", color = MaterialTheme.colorScheme.error)
                    }
                }
            }
        }

        if (notifications.isEmpty()) {
            item { EmptyState("Chưa có thông báo", "Các đơn từ, phê duyệt và quyết định phạt sẽ hiện ở đây.") }
        } else {
            items(notifications, key = { it.id }) { n ->
                NotificationRow(n, onClick = { onOpen(n) })
            }
        }
    }
}

@Composable
private fun NotificationRow(n: AppNotification, onClick: () -> Unit) {
    val accent = notificationColor(n.kind)
    // Nền dòng CHƯA ĐỌC phải là màu ĐẶC (đổ accent lên nền surface). Nếu để màu trong suốt (alpha thấp),
    // bóng đổ từ shadowElevation hắt XUYÊN QUA nền, tạo "khung xám lồng hộp" trông như vỡ giao diện.
    val surface = MaterialTheme.colorScheme.surface
    val rowColor = if (n.read) surface else accent.copy(alpha = 0.08f).compositeOver(surface)
    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(22.dp))
            .clickable(onClick = onClick),
        shape = RoundedCornerShape(22.dp),
        color = rowColor,
        border = BorderStroke(1.dp, if (n.read) MaterialTheme.colorScheme.outline else accent.copy(alpha = 0.35f)),
        shadowElevation = 1.dp,
    ) {
        Row(
            modifier = Modifier.padding(14.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Box(
                modifier = Modifier
                    .size(44.dp)
                    .clip(CircleShape)
                    .background(accent.copy(alpha = 0.14f)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(notificationIcon(n.kind), contentDescription = null, tint = accent, modifier = Modifier.size(22.dp))
            }
            Spacer(Modifier.width(14.dp))
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(
                    n.title,
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = if (n.read) FontWeight.SemiBold else FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onSurface,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    n.body,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    relativeTime(n.createdAt),
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            if (!n.read) {
                Spacer(Modifier.width(8.dp))
                Box(
                    modifier = Modifier
                        .size(10.dp)
                        .clip(CircleShape)
                        .background(accent),
                )
            }
        }
    }
}

private fun notificationIcon(kind: NotificationKind): ImageVector = when (kind) {
    NotificationKind.Request -> Icons.Filled.Description
    NotificationKind.Approval -> Icons.Filled.Inbox
    NotificationKind.Penalty -> Icons.Filled.Gavel
    NotificationKind.Attendance -> Icons.Filled.Face
    NotificationKind.Chat -> Icons.Filled.Chat
    NotificationKind.System -> Icons.Filled.NotificationsNone
}

@Composable
private fun notificationColor(kind: NotificationKind): Color = when (kind) {
    NotificationKind.Request -> Success
    NotificationKind.Approval -> MaterialTheme.colorScheme.primary
    NotificationKind.Penalty -> Danger
    NotificationKind.Attendance -> Warning
    NotificationKind.Chat -> MaterialTheme.colorScheme.primary
    NotificationKind.System -> MaterialTheme.colorScheme.primary
}
