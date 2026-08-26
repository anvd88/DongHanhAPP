package com.ketoanapk.hr.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Call
import androidx.compose.material.icons.filled.CallMade
import androidx.compose.material.icons.filled.CallReceived
import androidx.compose.material.icons.filled.History
import androidx.compose.material.icons.filled.Videocam
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.ketoanapk.hr.data.CallHistoryItem
import com.ketoanapk.hr.data.CallManager

data class CallHistoryUiState(
    val loading: Boolean = false,
    val items: List<CallHistoryItem> = emptyList(),
    val error: String? = null,
)

@Composable
fun CallHistoryScreen(vm: HrViewModel) {
    val state = vm.callHistoryState
    LazyColumn(Modifier.fillMaxSize(), contentPadding = screenPadding(), verticalArrangement = Arrangement.spacedBy(9.dp)) {
        when {
            state.loading && state.items.isEmpty() -> item { LoadingBlock() }
            state.items.isEmpty() -> item {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    EmptyState(if (state.error != null) "Không tải được lịch sử" else "Chưa có cuộc gọi", state.error ?: "Các cuộc gọi của bạn sẽ hiển thị tại đây.")
                    Button(onClick = vm::refreshCallHistory, modifier = Modifier.fillMaxWidth()) { Text("Thử lại") }
                }
            }
            else -> items(state.items, key = { it.id }) { item -> CallHistoryCard(item, vm) }
        }
    }
}

@Composable
private fun CallHistoryCard(item: CallHistoryItem, vm: HrViewModel) {
    val missed = item.outcome in setOf("missed", "no_answer", "declined", "canceled") && item.durationSeconds == 0
    Surface(modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(17.dp), border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline)) {
        Row(Modifier.padding(13.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Icon(if (item.direction == "incoming") Icons.Filled.CallReceived else Icons.Filled.CallMade, null, tint = if (missed) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary)
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(item.peerName.ifBlank { item.peerUsername }, fontWeight = FontWeight.Bold)
                Text(
                    "${if (item.media == "video") "Video" else "Thoại"} · ${callOutcomeLabel(item.outcome)} · ${formatIsoDateTime(item.endedAt)}",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                if (item.durationSeconds > 0) Text("Thời lượng ${formatCallSeconds(item.durationSeconds)}", style = MaterialTheme.typography.labelSmall)
            }
            IconButton(onClick = {
                val media = if (item.media == "video") CallManager.Media.Video else CallManager.Media.Audio
                if (vm.ensureCallAllowed(media == CallManager.Media.Video)) CallManager.startCall(item.peerUsername, item.peerName, initials(item.peerName), media)
            }) { Icon(if (item.media == "video") Icons.Filled.Videocam else Icons.Filled.Call, "Gọi lại") }
        }
    }
}

internal fun callOutcomeLabel(value: String): String = when (value) {
    "missed" -> "Gọi nhỡ"
    "no_answer" -> "Không trả lời"
    "declined" -> "Từ chối"
    "busy" -> "Máy bận"
    "disconnected" -> "Mất kết nối"
    else -> "Đã kết thúc"
}

internal fun formatCallSeconds(value: Int): String = "%02d:%02d".format(value / 60, value % 60)
