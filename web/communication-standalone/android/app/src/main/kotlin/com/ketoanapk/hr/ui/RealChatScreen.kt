package com.ketoanapk.hr.ui

import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.positionChange
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
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
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.AddComment
import androidx.compose.material.icons.filled.AttachFile
import androidx.compose.material.icons.filled.Call
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Call
import androidx.compose.material.icons.filled.Chat
import androidx.compose.material.icons.filled.Contacts
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.PushPin
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Videocam
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material3.Button
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import android.media.MediaPlayer
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.rememberCoroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.window.Popup
import androidx.compose.ui.window.PopupProperties
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.platform.LocalContext
import androidx.core.content.ContextCompat
import androidx.core.content.FileProvider
import com.ketoanapk.hr.data.CallContact
import com.ketoanapk.hr.data.CallManager
import com.ketoanapk.hr.data.ChatConversation
import com.ketoanapk.hr.data.ChatMessage
import com.ketoanapk.hr.data.isVoiceMessage
import com.ketoanapk.hr.data.ChatAudioRecorder

data class RealChatUiState(
    val loading: Boolean = false,
    val conversations: List<ChatConversation> = emptyList(),
    val selected: ChatConversation? = null,
    val messages: List<ChatMessage> = emptyList(),
    val contacts: List<CallContact> = emptyList(),
    val choosingContact: Boolean = false,
    val query: String = "",
    val messageQuery: String = "",
    val input: String = "",
    val sending: Boolean = false,
    val loadingMore: Boolean = false,
    val hasMore: Boolean = true,
    val error: String? = null,
    val offline: Boolean = false,
)

@Composable
fun RealChatScreen(vm: HrViewModel) {
    val state = vm.realChatState
    BackHandler(enabled = state.selected != null || state.choosingContact) { vm.closeChatLayer() }
    when {
        state.choosingContact -> ChatContactPicker(state, vm)
        state.selected != null -> RealChatThread(state, vm)
        else -> ChatMiniApp(vm, state)
    }
}

/**
 * Mini app Chat: chiếm trọn màn (HrApp đã ẩn header + thanh dưới của HR khi ở đây) và tự dựng header
 * riêng + thanh tab riêng. Danh bạ và Cuộc gọi là TAB của mini app chứ không phải màn rời rạc — đúng
 * cấu trúc Telegram (Chats / Contacts / Calls).
 */
@Composable
private fun ChatMiniApp(vm: HrViewModel, state: RealChatUiState) {
    Column(Modifier.fillMaxSize()) {
        Surface(color = MaterialTheme.colorScheme.surface, shadowElevation = 2.dp) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 4.dp, vertical = 6.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                // Thoát mini app = lùi ngăn xếp, tức về đúng màn vừa rời (giống nút Back của máy).
                IconButton(onClick = vm::goBack) {
                    Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Thoát Chat")
                }
                Text(
                    vm.chatTab.label,
                    style = MaterialTheme.typography.titleLarge,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onSurface,
                    modifier = Modifier.weight(1f),
                )
                IconButton(onClick = vm::openNewChat) {
                    Icon(Icons.Filled.AddComment, contentDescription = "Tạo hội thoại")
                }
            }
        }

        Box(Modifier.weight(1f)) {
            when (vm.chatTab) {
                ChatTab.Conversations -> RealChatInbox(state, vm)
                ChatTab.Directory -> DirectoryScreen(vm)
                ChatTab.Calls -> CallHistoryScreen(vm)
            }
        }

        ChatTabBar(current = vm.chatTab, unread = vm.chatUnreadCount, onSelect = vm::openChatTab)
    }
}

@Composable
private fun ChatTabBar(current: ChatTab, unread: Int, onSelect: (ChatTab) -> Unit) {
    NavigationBar(
        containerColor = Color.Transparent,
        tonalElevation = 0.dp,
    ) {
        ChatTab.entries.forEach { tab ->
            NavigationBarItem(
                selected = current == tab,
                onClick = { onSelect(tab) },
                colors = NavigationBarItemDefaults.colors(
                    selectedIconColor = MaterialTheme.colorScheme.primary,
                    selectedTextColor = MaterialTheme.colorScheme.primary,
                    indicatorColor = MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.78f),
                    unselectedIconColor = MaterialTheme.colorScheme.onSurfaceVariant,
                    unselectedTextColor = MaterialTheme.colorScheme.onSurfaceVariant,
                ),
                icon = {
                    val icon = when (tab) {
                        ChatTab.Conversations -> Icons.Filled.Chat
                        ChatTab.Directory -> Icons.Filled.Contacts
                        ChatTab.Calls -> Icons.Filled.Call
                    }
                    if (tab == ChatTab.Conversations && unread > 0) {
                        BadgedBox(badge = { Badge { Text(if (unread > 99) "99+" else "$unread") } }) {
                            Icon(icon, contentDescription = null)
                        }
                    } else {
                        Icon(icon, contentDescription = null)
                    }
                },
                label = { Text(tab.label) },
            )
        }
    }
}

@Composable
private fun RealChatInbox(state: RealChatUiState, vm: HrViewModel) {
    val query = state.query.trim()
    val rows = state.conversations.filter {
        query.isBlank() || it.title.contains(query, true) || it.preview.contains(query, true)
    }
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(14.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        if (state.offline) item { EmptyState("Đang xem ngoại tuyến", "Dữ liệu gần nhất đã được giải mã từ cache trên thiết bị. Bấm Thử lại khi có mạng.") }
        item {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                OutlinedTextField(
                    value = state.query,
                    onValueChange = vm::setChatQuery,
                    modifier = Modifier.weight(1f),
                    singleLine = true,
                    leadingIcon = { Icon(Icons.Filled.Search, contentDescription = null) },
                    label = { Text("Tìm hội thoại") },
                )
            }
        }
        when {
            state.loading && rows.isEmpty() -> item { LoadingBlock() }
            rows.isEmpty() -> item {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    EmptyState(if (state.error != null) "Không tải được chat" else "Chưa có hội thoại", state.error ?: "Tạo hội thoại mới từ danh bạ.")
                    Button(onClick = vm::refreshChat, modifier = Modifier.fillMaxWidth()) { Text("Thử lại") }
                }
            }
            else -> items(rows, key = { it.id }) { conversation ->
                ChatConversationCard(conversation) { vm.openChatConversation(conversation) }
            }
        }
    }
}

@Composable
private fun ChatConversationCard(item: ChatConversation, onClick: () -> Unit) {
    Surface(onClick = onClick, modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(18.dp), border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline)) {
        Row(Modifier.padding(14.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            UserAvatar(item.title.ifBlank { item.username.orEmpty() }, 44, avatar = item.avatarUrl)
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(item.title, modifier = Modifier.weight(1f), fontWeight = if (item.unread > 0) FontWeight.ExtraBold else FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    if (item.pinned) Icon(Icons.Filled.PushPin, contentDescription = null, modifier = Modifier.size(16.dp))
                }
                Text(chatPreview(item.preview), maxLines = 1, overflow = TextOverflow.Ellipsis, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            if (item.unread > 0) StatusChip("${item.unread}", Tone.Danger)
        }
    }
}

@Composable
private fun RealChatThread(state: RealChatUiState, vm: HrViewModel) {
    val conversation = state.selected ?: return
    val peer = conversation.username
    val messages = state.messages.filter {
        state.messageQuery.isBlank() || it.body.contains(state.messageQuery, true) || it.fileName.orEmpty().contains(state.messageQuery, true)
    }
    var actionMessage by remember { mutableStateOf<ChatMessage?>(null) }
    var editingMessage by remember { mutableStateOf<ChatMessage?>(null) }
    var forwardingMessage by remember { mutableStateOf<ChatMessage?>(null) }
    val context = LocalContext.current
    val audioRecorder = remember { ChatAudioRecorder(context.applicationContext) }
    // Bảng ghi âm đang mở (có thể đang chờ, chưa ghi).
    var voicePanel by remember { mutableStateOf(false) }
    var recording by remember { mutableStateOf(false) }
    // Rảnh tay: bấm nhả luôn hoặc vuốt sang phải nên nhả tay vẫn ghi tiếp, chờ bấm gửi/huỷ.
    var locked by remember { mutableStateOf(false) }
    var dragX by remember { mutableFloatStateOf(0f) }
    var elapsedMs by remember { mutableLongStateOf(0L) }
    val amplitudes = remember { mutableStateListOf<Float>() }
    // Upload lỗi vẫn giữ tệp nguồn để có thể gửi lại; khi thành công repo đã seed cache theo messageId.
    var pendingVoiceRetry by remember { mutableStateOf<java.io.File?>(null) }
    val keyboard = LocalSoftwareKeyboardController.current
    val focusManager = LocalFocusManager.current

    /**
     * Bảng ghi âm THAY CHỖ bàn phím nên phải đuổi bàn phím đi trước; để cả hai cùng bật thì bảng bị đẩy
     * lên và vỡ bố cục. Bỏ tiêu điểm khỏi ô nhập, nếu không chạm lại vào ô là bàn phím bật lên đè tiếp.
     */
    fun openVoicePanel() {
        keyboard?.hide()
        focusManager.clearFocus()
        voicePanel = true
    }

    // Cấp quyền xong thì mở bảng luôn, nhưng KHÔNG tự ghi — người dùng vẫn phải tự bấm nút mic to.
    val microphoneLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) openVoicePanel()
    }
    DisposableEffect(Unit) { onDispose { audioRecorder.cancel() } }
    val retryFileForDisposal = pendingVoiceRetry
    DisposableEffect(retryFileForDisposal) { onDispose { retryFileForDisposal?.delete() } }

    fun sendVoice(file: java.io.File) {
        pendingVoiceRetry = file
        val uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
        vm.sendChatAttachment(context, uri, kind = "voice") { sent ->
            if (sent && pendingVoiceRetry == file) pendingVoiceRetry = null
        }
    }

    /** Dừng ghi rồi đóng bảng. [send] = false là bỏ hẳn bản ghi. */
    fun finishRecording(send: Boolean) {
        // Đọc TRƯỚC stop()/cancel() vì cả hai đều xoá mốc bắt đầu.
        val tooShort = audioRecorder.elapsedMs() < MIN_VOICE_MS
        when {
            !send -> audioRecorder.cancel()
            // Chạm nhầm vào nút mic cũng đủ tạo bản ghi 0 giây; gửi ra thì bên kia nhận một tin thoại câm.
            tooShort -> { audioRecorder.cancel(); vm.showActionMessage("Giữ lâu hơn một chút để ghi âm.") }
            else -> audioRecorder.stop()?.let(::sendVoice)
        }
        recording = false
        locked = false
        dragX = 0f
        elapsedMs = 0L
        amplitudes.clear()
        voicePanel = false
    }

    // Nút Back: đang ghi thì bỏ bản ghi, đang chờ thì chỉ đóng bảng. Đặt ở đây (sâu hơn BackHandler đóng
    // hội thoại của RealChatScreen) nên nó được ưu tiên khi bảng đang mở.
    BackHandler(enabled = voicePanel) { finishRecording(send = false) }

    // Bơm sóng âm + đồng hồ cho lớp phủ. recording = false là vòng lặp tự dừng theo LaunchedEffect.
    LaunchedEffect(recording) {
        if (!recording) return@LaunchedEffect
        while (true) {
            amplitudes.add(audioRecorder.amplitude())
            if (amplitudes.size > VOICE_WAVE_SAMPLES) amplitudes.removeAt(0)
            elapsedMs = audioRecorder.elapsedMs()
            delay(VOICE_WAVE_TICK_MS)
        }
    }
    val attachmentLauncher = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) vm.sendChatAttachment(vm.getApplication(), uri)
    }

    // Cử chỉ của nút mic TO giữa bảng: bấm = ghi rảnh tay · giữ rồi thả = gửi · giữ rồi vuốt trái = huỷ ·
    // vuốt phải = rảnh tay.
    val micGesture = Modifier.pointerInput(state.sending) {
        val engagePx = VOICE_ENGAGE_DRAG.toPx()
        // Nhả trước mốc nhận-giữ của hệ thống thì tính là BẤM. Mượn đúng mốc của Android để cảm giác
        // nhanh/chậm khớp với phần còn lại của máy.
        val tapWindowMs = viewConfiguration.longPressTimeoutMillis
        awaitEachGesture {
            val down = awaitFirstDown(requireUnconsumed = false)
            if (state.sending) return@awaitEachGesture
            if (!audioRecorder.start()) {
                vm.showActionMessage("Không mở được micro. Kiểm tra xem app khác có đang giữ micro không.")
                return@awaitEachGesture
            }
            val pressedAt = System.currentTimeMillis()
            recording = true
            dragX = 0f
            var outcome = VoiceOutcome.Send
            try {
                var offset = 0f
                while (true) {
                    val event = awaitPointerEvent()
                    val change = event.changes.firstOrNull { it.id == down.id } ?: break
                    if (!change.pressed) {
                        // CHỈ lúc thả tay mới chốt, và chốt theo chỗ nút mic đang đậu. Kéo vào icon rồi
                        // đổi ý kéo ra là thoát — trước đây chạm ngưỡng là ăn ngay, không rút lại được.
                        outcome = when {
                            offset <= -engagePx -> VoiceOutcome.Cancel
                            offset >= engagePx -> VoiceOutcome.Lock
                            // Bấm nhanh (không kéo) = ghi rảnh tay; giữ lâu rồi thả = gửi.
                            System.currentTimeMillis() - pressedAt < tapWindowMs -> VoiceOutcome.Lock
                            else -> VoiceOutcome.Send
                        }
                        break
                    }
                    offset += change.positionChange().x
                    change.consume()
                    dragX = offset
                }
            } catch (t: Throwable) {
                // Cử chỉ đứt giữa chừng (cuộc gọi đến chen lên, rời màn): coi như huỷ. KHÔNG gửi — người
                // dùng chưa hề thả tay để chốt.
                outcome = VoiceOutcome.Cancel
                throw t
            } finally {
                // PHẢI nằm trong finally: đây là chỗ duy nhất chắc chắn chạy ở MỌI nhánh thoát. Bỏ nó ra
                // ngoài là micro ghi VÔ THỜI HẠN mà người dùng không biết.
                when (outcome) {
                    // Khoá thì cố ý ghi tiếp; bảng vẫn hiện nên micro không chạy lén.
                    VoiceOutcome.Lock -> { locked = true; dragX = 0f }
                    VoiceOutcome.Cancel -> finishRecording(send = false)
                    VoiceOutcome.Send -> finishRecording(send = true)
                }
            }
        }
    }
    // Sửa tin vẫn là hộp thoại riêng: nó cần ô nhập nhiều dòng, không nhét vừa menu ngữ cảnh.
    editingMessage?.let { message ->
        EditMessageDialog(
            message = message,
            onDismiss = { editingMessage = null },
            onSave = { vm.editCurrentChatMessage(message.id, it); editingMessage = null },
        )
    }
    forwardingMessage?.let { message ->
        ForwardMessageDialog(
            conversations = state.conversations.filter { it.id != state.selected?.id },
            onDismiss = { forwardingMessage = null },
            onSelect = { vm.forwardChatMessage(message, it); forwardingMessage = null },
        )
    }
    Column(Modifier.fillMaxSize()) {
        if (state.offline) {
            Surface(color = MaterialTheme.colorScheme.errorContainer, modifier = Modifier.fillMaxWidth()) {
                Text("Ngoại tuyến · đang xem tin đã lưu", modifier = Modifier.padding(8.dp), color = MaterialTheme.colorScheme.onErrorContainer)
            }
        }
        Row(Modifier.fillMaxWidth().padding(horizontal = 6.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = vm::closeChatLayer) { Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Quay lại") }
            Column(Modifier.weight(1f)) {
                Text(conversation.title, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(if (conversation.isOnline) "Đang hoạt động" else "Ngoại tuyến", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            if (!peer.isNullOrBlank()) {
                IconButton(onClick = { if (vm.ensureCallAllowed(false)) CallManager.startCall(peer, conversation.title, initials(conversation.title), CallManager.Media.Audio) }) { Icon(Icons.Filled.Call, contentDescription = "Gọi thoại") }
                IconButton(onClick = { if (vm.ensureCallAllowed(true)) CallManager.startCall(peer, conversation.title, initials(conversation.title), CallManager.Media.Video) }) { Icon(Icons.Filled.Videocam, contentDescription = "Gọi video") }
            }
            IconButton(onClick = { vm.pinCurrentChat(!conversation.pinned) }) { Icon(Icons.Filled.PushPin, contentDescription = "Ghim") }
        }
        OutlinedTextField(
            value = state.messageQuery,
            onValueChange = vm::setMessageSearch,
            modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp),
            singleLine = true,
            leadingIcon = { Icon(Icons.Filled.Search, contentDescription = null) },
            label = { Text("Tìm tin nhắn") },
        )
        LazyColumn(Modifier.weight(1f).fillMaxWidth(), contentPadding = PaddingValues(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            if (state.hasMore && state.messages.isNotEmpty()) item {
                Button(onClick = vm::loadOlderChatMessages, enabled = !state.loadingMore, modifier = Modifier.fillMaxWidth()) {
                    Text(if (state.loadingMore) "Đang tải…" else "Tải tin cũ hơn")
                }
            }
            if (state.loading && messages.isEmpty()) item { LoadingBlock() }
            if (!state.loading && messages.isEmpty()) item { EmptyState("Chưa có tin nhắn", state.error ?: "Gửi lời chào để bắt đầu hội thoại.") }
            items(messages, key = { it.id }) { message ->
                RealMessageBubble(
                    message = message,
                    menuOpen = actionMessage?.id == message.id,
                    loadAudio = { vm.chatAudioFile(context, it) },
                    // Tin thoại KHÔNG mở bằng app ngoài: nó có nút phát riêng trong bong bóng. Nếu vẫn gọi
                    // downloadChatAttachment ở đây thì chạm vào bong bóng sẽ bắn ACTION_VIEW ra trình mở tệp.
                    onClick = {
                        if (message.kind == "file" && message.hasBlob && !message.isVoice) {
                            vm.downloadChatAttachment(context, message)
                        }
                    },
                    onLongPress = { actionMessage = message },
                    onDismissMenu = { actionMessage = null },
                    onEdit = { editingMessage = message; actionMessage = null },
                    onDelete = { vm.deleteCurrentChatMessage(message.id); actionMessage = null },
                    onReact = { vm.reactCurrentChatMessage(message.id, it); actionMessage = null },
                    onForward = { forwardingMessage = message; actionMessage = null },
                )
            }
            if (state.sending) item { Text("Đang gửi…", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.primary) }
            pendingVoiceRetry?.let { retryFile ->
                item {
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                        Button(onClick = { sendVoice(retryFile) }, enabled = !state.sending) { Text("Gửi lại tin thoại") }
                        TextButton(
                            onClick = { if (pendingVoiceRetry == retryFile) pendingVoiceRetry = null },
                            enabled = !state.sending,
                        ) { Text("Bỏ") }
                    }
                }
            }
            state.error?.let { error ->
                item {
                    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Text(error, color = MaterialTheme.colorScheme.error)
                        if (state.input.isNotBlank()) Button(onClick = vm::sendCurrentChatMessage) { Text("Thử gửi lại") }
                    }
                }
            }
        }
        Row(Modifier.fillMaxWidth().padding(10.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            IconButton(onClick = { attachmentLauncher.launch(arrayOf("image/*", "video/*", "audio/*", "application/pdf", "*/*")) }, enabled = !state.sending) {
                Icon(Icons.Filled.AttachFile, contentDescription = "Đính kèm")
            }
            // Công tắc bật/tắt bảng ghi âm — đúng kiểu nút chuyển bàn phím: bấm lần nữa là đóng. Việc ghi
            // nằm ở nút mic to giữa bảng, ở đó ngón tay mới đủ chỗ vuốt sang cả hai bên.
            IconButton(
                onClick = {
                    if (voicePanel) {
                        // Đang ghi mà đóng bảng thì bỏ bản ghi — cùng nước đi với nút Back.
                        finishRecording(send = false)
                    } else {
                        val granted = ContextCompat.checkSelfPermission(
                            context, Manifest.permission.RECORD_AUDIO,
                        ) == PackageManager.PERMISSION_GRANTED
                        if (granted) openVoicePanel() else microphoneLauncher.launch(Manifest.permission.RECORD_AUDIO)
                    }
                },
                enabled = !state.sending,
            ) {
                Icon(
                    Icons.Filled.Mic,
                    contentDescription = if (voicePanel) "Đóng bảng ghi âm" else "Ghi âm",
                    tint = if (voicePanel) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            OutlinedTextField(
                value = state.input,
                onValueChange = vm::setChatInput,
                modifier = Modifier.weight(1f),
                maxLines = 4,
                label = { Text("Tin nhắn") },
            )
            IconButton(
                onClick = vm::sendCurrentChatMessage,
                enabled = state.input.isNotBlank() && !state.sending,
            ) {
                Icon(Icons.AutoMirrored.Filled.Send, contentDescription = "Gửi")
            }
        }
        // Nằm DƯỚI thanh nhập, trong cùng Column: nó ăn đúng chỗ bàn phím vừa nhường ra, hội thoại phía
        // trên vẫn thấy — không phải lớp phủ kín màn.
        if (voicePanel) {
            VoiceRecorderPanel(
                recording = recording,
                locked = locked,
                elapsedMs = elapsedMs,
                amplitudes = amplitudes,
                dragX = dragX,
                engageAt = with(LocalDensity.current) { VOICE_ENGAGE_DRAG.toPx() },
                onCancel = { finishRecording(send = false) },
                onSend = { finishRecording(send = true) },
                micGesture = micGesture,
            )
        }
    }
}

/** Ngắn hơn mức này coi như chạm nhầm, không gửi. */
private const val MIN_VOICE_MS = 700L

/** Ngã ra của một lần giữ mic. */
private enum class VoiceOutcome { Send, Cancel, Lock }

@Composable
@OptIn(ExperimentalFoundationApi::class)
private fun RealMessageBubble(
    message: ChatMessage,
    menuOpen: Boolean,
    loadAudio: suspend (ChatMessage) -> java.io.File?,
    onClick: () -> Unit,
    onLongPress: () -> Unit,
    onDismissMenu: () -> Unit,
    onEdit: () -> Unit,
    onDelete: () -> Unit,
    onReact: (String) -> Unit,
    onForward: () -> Unit,
) {
  Box {
    // Đo chiều cao bong bóng để neo menu: viên cảm xúc nổi TRÊN đỉnh, thẻ tác vụ nằm DƯỚI đáy, tin nhắn
    // ở giữa và không bị che — bố cục của các app nhắn tin hiện nay.
    var bubbleHeight by remember { mutableIntStateOf(0) }
    MessageContextMenu(
        message = message,
        expanded = menuOpen,
        bubbleHeight = bubbleHeight,
        onDismiss = onDismissMenu,
        onEdit = onEdit,
        onDelete = onDelete,
        onReact = onReact,
        onForward = onForward,
    )
    Row(
        Modifier.fillMaxWidth().onSizeChanged { bubbleHeight = it.height },
        horizontalArrangement = if (message.mine) Arrangement.End else Arrangement.Start,
    ) {
        Surface(
            shape = RoundedCornerShape(16.dp),
            color = if (message.mine) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceVariant,
            modifier = Modifier.fillMaxWidth(0.82f).combinedClickable(onClick = onClick, onLongClick = onLongPress),
        ) {
            Column(Modifier.padding(11.dp), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                if (!message.mine) Text(message.senderName, style = MaterialTheme.typography.labelMedium, fontWeight = FontWeight.Bold)
                if (message.isVoice) {
                    VoicePlayer(message, loadAudio)
                } else {
                    val body = when {
                        message.removed -> "Tin nhắn đã được gỡ"
                        message.kind == "file" -> "📎 ${message.fileName ?: "Tệp"}"
                        else -> message.body
                    }
                    Text(body)
                }
                val meta = buildString {
                    append(formatIsoDateTime(message.createdAt))
                    if (message.editedAt != null) append(" · đã sửa")
                    if (message.forwarded) append(" · chuyển tiếp")
                    if (message.mine) append(if (message.read) " · đã đọc" else " · đã gửi")
                }
                Text(meta, style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                if (message.reactions.isNotEmpty()) Text(message.reactions.joinToString("  ") { "${it.emoji} ${it.count}" }, style = MaterialTheme.typography.bodySmall)
            }
        }
    }
  }
}

/**
 * Menu ngữ cảnh của một tin nhắn: hàng cảm xúc trên cùng rồi tới các tác vụ, nổi neo vào bong bóng.
 * Chỉ hiện tác vụ hợp lệ với tin đó (tin của mình mới sửa/gỡ được; tin đã gỡ thì không làm gì thêm).
 */
@Composable
private fun MessageContextMenu(
    message: ChatMessage,
    expanded: Boolean,
    bubbleHeight: Int,
    onDismiss: () -> Unit,
    onEdit: () -> Unit,
    onDelete: () -> Unit,
    onReact: (String) -> Unit,
    onForward: () -> Unit,
) {
    if (!expanded) return
    val canEdit = message.mine && !message.removed && message.kind == "text"
    val canForward = !message.removed && message.kind == "text"
    val canReact = !message.removed
    // Chỉ dựng phần nào THỰC SỰ có nội dung, nếu không sẽ ra thẻ trắng trống trơn. Hai ca dính lỗi này:
    // tin đã gỡ (không làm được gì) và tin tệp đính kèm (không sửa/chuyển tiếp được vì kind != "text").
    val hasActions = canEdit || canForward
    if (!canReact && !hasActions) return
    // Popup tự dựng thay vì DropdownMenu: cần viên cảm xúc bo tròn TÁCH RỜI nổi trên bong bóng còn thẻ tác
    // vụ nằm dưới — DropdownMenu gộp một khối duy nhất nên không làm được kiểu này.
    val align = if (message.mine) Alignment.TopEnd else Alignment.TopStart
    val gap = 6.dp
    // Chiều cao viên cảm xúc ĐẶT CỨNG: cỡ emoji cố định nên tính trước được, khỏi phải đo rồi nhảy vị trí
    // ở khung hình đầu tiên.
    val pillHeight = 54.dp
    val density = LocalDensity.current
    val pillOffsetY = with(density) { -(pillHeight + gap).roundToPx() }
    val cardOffsetY = bubbleHeight + with(density) { gap.roundToPx() }

    if (canReact) {
        // Khi CÓ thẻ tác vụ thì để thẻ đó lo việc đóng (focusable = false ở đây để chạm ra ngoài rơi
        // xuống nó). Khi KHÔNG có thẻ (tin tệp), viên cảm xúc phải tự lo, nếu không menu không đóng được.
        Popup(
            alignment = align,
            offset = IntOffset(0, pillOffsetY),
            onDismissRequest = if (hasActions) null else onDismiss,
            properties = PopupProperties(focusable = !hasActions),
        ) {
            Surface(
                shape = CircleShape,
                color = MaterialTheme.colorScheme.surface,
                shadowElevation = 6.dp,
                modifier = Modifier.height(pillHeight),
            ) {
                Row(
                    modifier = Modifier.padding(horizontal = 6.dp),
                    horizontalArrangement = Arrangement.spacedBy(2.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    listOf("👍", "❤️", "😂", "😮", "😢").forEach { emoji ->
                        Box(
                            modifier = Modifier
                                .clip(CircleShape)
                                .clickable { onReact(emoji) }
                                .padding(7.dp),
                        ) { Text(emoji, style = MaterialTheme.typography.titleLarge) }
                    }
                }
            }
        }
    }
    if (hasActions) {
        Popup(
            alignment = align,
            offset = IntOffset(0, cardOffsetY),
            onDismissRequest = onDismiss,
            properties = PopupProperties(focusable = true),
        ) {
            Surface(
                shape = RoundedCornerShape(18.dp),
                color = MaterialTheme.colorScheme.surface,
                shadowElevation = 6.dp,
            ) {
                Column(Modifier.width(IntrinsicSize.Max).padding(vertical = 6.dp)) {
                    if (canForward) MenuRow(Icons.AutoMirrored.Filled.Send, "Chuyển tiếp", onClick = onForward)
                    if (canEdit) {
                        MenuRow(Icons.Filled.Edit, "Sửa tin nhắn", onClick = onEdit)
                        MenuRow(
                            Icons.Filled.Delete,
                            "Gỡ tin nhắn",
                            tint = MaterialTheme.colorScheme.error,
                            onClick = onDelete,
                        )
                    }
                }
            }
        }
    }
}

/**
 * Tin nhắn thoại hay tệp thường? Tin thoại được PHÁT TRONG APP, không bao giờ ném ra trình mở tệp ngoài.
 *
 * Nhận diện theo mime bắt đầu bằng "audio" hoặc đuôi ".m4a" (khớp tên ghi-am-*.m4a mà ChatAudioRecorder
 * đặt).
 * KHÔNG dùng `hasBlob`: cờ đó chỉ bật SAU khi tải về (xem HrRepository), nên tin vừa lấy từ máy chủ luôn
 * false và trình phát sẽ không bao giờ hiện.
 */
/** Tên tệp ghi âm do ChatAudioRecorder đặt: ghi-am-<mốc thời gian>.ogg (Opus) hoặc .m4a (AAC, máy cũ). */
private val VOICE_FILE = Regex("""(📎\s*)?ghi-am-\S+\.(ogg|m4a)""", RegexOption.IGNORE_CASE)

/**
 * Dòng xem trước ở danh sách hội thoại. Máy chủ trả về chuỗi dựng sẵn ("Bạn: 📎 ghi-am-....ogg") nên tên
 * tệp thô lọt ra giao diện; đổi nó thành "Tin nhắn thoại" tại đây.
 *
 * Đây là giải pháp tạm bằng khớp chuỗi. Khi backend có kind="voice", preview nên do máy chủ trả đúng chữ
 * và hàm này bỏ đi được — cả web lẫn APK cũ hiện vẫn đang lộ tên tệp.
 */
private fun chatPreview(raw: String): String =
    if (raw.isBlank()) "Chưa có tin nhắn" else VOICE_FILE.replace(raw, "🎤 Tin nhắn thoại")

private val ChatMessage.isVoice: Boolean
    get() = isVoiceMessage()

/**
 * Trình phát tin nhắn thoại ngay trong bong bóng: nút phát/dừng + thanh tiến trình + thời lượng.
 * Tệp tải về lần đầu khi bấm phát (repo đã cache nên lần sau không tải lại); giải phóng MediaPlayer khi
 * bong bóng rời khỏi màn hình để không rò tài nguyên.
 */
@Composable
private fun VoicePlayer(message: ChatMessage, loadAudio: suspend (ChatMessage) -> java.io.File?) {
    val scope = rememberCoroutineScope()
    var player by remember(message.id) { mutableStateOf<MediaPlayer?>(null) }
    var playing by remember(message.id) { mutableStateOf(false) }
    var loading by remember(message.id) { mutableStateOf(false) }
    var durationMs by remember(message.id) { mutableIntStateOf(0) }
    var positionMs by remember(message.id) { mutableIntStateOf(0) }

    DisposableEffect(message.id) { onDispose { player?.release() } }

    // Nhích thanh tiến trình trong lúc phát. Dừng phát là vòng lặp tự thoát.
    LaunchedEffect(playing) {
        while (playing) {
            positionMs = player?.currentPosition ?: 0
            delay(200)
        }
    }

    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        modifier = Modifier.fillMaxWidth(),
    ) {
        IconButton(
            enabled = !loading,
            onClick = {
                val current = player
                when {
                    current == null -> scope.launch {
                        loading = true
                        val file = loadAudio(message)
                        loading = false
                        if (file != null) {
                            runCatching {
                                MediaPlayer().apply {
                                    setDataSource(file.absolutePath)
                                    prepare()
                                    setOnCompletionListener { playing = false; positionMs = 0; seekTo(0) }
                                }
                            }.onSuccess { mp ->
                                durationMs = mp.duration
                                player = mp
                                mp.start()
                                playing = true
                            }
                        }
                    }
                    playing -> { current.pause(); playing = false }
                    else -> { current.start(); playing = true }
                }
            },
        ) {
            when {
                loading -> CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                playing -> Icon(Icons.Filled.Pause, contentDescription = "Tạm dừng")
                else -> Icon(Icons.Filled.PlayArrow, contentDescription = "Phát")
            }
        }
        LinearProgressIndicator(
            progress = { if (durationMs > 0) positionMs.toFloat() / durationMs else 0f },
            modifier = Modifier.weight(1f),
        )
        Text(
            formatMillis(if (playing || positionMs > 0) positionMs else durationMs),
            style = MaterialTheme.typography.labelMedium,
        )
    }
}

private fun formatMillis(ms: Int): String {
    val total = ms / 1000
    return "%d:%02d".format(total / 60, total % 60)
}

@Composable
private fun MenuRow(
    icon: ImageVector,
    label: String,
    tint: Color = MaterialTheme.colorScheme.onSurface,
    onClick: () -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(horizontal = 18.dp, vertical = 13.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Icon(icon, contentDescription = null, tint = tint, modifier = Modifier.size(21.dp))
        Text(label, style = MaterialTheme.typography.bodyLarge, color = tint, maxLines = 1)
    }
}

/** Hộp thoại sửa tin nhắn — tách riêng khỏi menu vì cần ô nhập nhiều dòng. */
@Composable
private fun EditMessageDialog(message: ChatMessage, onDismiss: () -> Unit, onSave: (String) -> Unit) {
    var text by remember(message.id) { mutableStateOf(message.body) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Sửa tin nhắn") },
        text = {
            OutlinedTextField(
                value = text,
                onValueChange = { text = it.take(4000) },
                modifier = Modifier.fillMaxWidth(),
                minLines = 2,
            )
        },
        confirmButton = { Button(onClick = { onSave(text) }, enabled = text.isNotBlank()) { Text("Lưu") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Hủy") } },
    )
}

@Composable
private fun ForwardMessageDialog(
    conversations: List<ChatConversation>,
    onDismiss: () -> Unit,
    onSelect: (ChatConversation) -> Unit,
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Chuyển tiếp tới") },
        text = {
            LazyColumn(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                if (conversations.isEmpty()) item { Text("Không có hội thoại khác.") }
                items(conversations, key = { it.id }) { conversation ->
                    Surface(onClick = { onSelect(conversation) }, modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(12.dp), border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline)) {
                        Text(conversation.title, modifier = Modifier.padding(12.dp), fontWeight = FontWeight.Bold)
                    }
                }
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text("Đóng") } },
    )
}

@Composable
private fun ChatContactPicker(state: RealChatUiState, vm: HrViewModel) {
    LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(14.dp), verticalArrangement = Arrangement.spacedBy(9.dp)) {
        item {
            Row(verticalAlignment = Alignment.CenterVertically) {
                IconButton(onClick = vm::closeChatLayer) { Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Quay lại") }
                Text("Tạo hội thoại", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
                Spacer(Modifier.weight(1f))
                IconButton(onClick = vm::openNewChat) { Icon(Icons.Filled.Refresh, contentDescription = "Tải lại") }
            }
        }
        if (state.loading) item { LoadingBlock() }
        if (!state.loading && state.contacts.isEmpty()) item { EmptyState("Không có liên hệ", state.error ?: "Danh bạ hiện đang trống.") }
        items(state.contacts, key = { it.username }) { contact ->
            Surface(onClick = { vm.createDirectChat(contact) }, shape = RoundedCornerShape(16.dp), border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline), modifier = Modifier.fillMaxWidth()) {
                Row(Modifier.padding(13.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    UserAvatar(contact.displayName, 42, avatar = contact.avatarUrl)
                    Column(Modifier.weight(1f)) {
                        Text(contact.displayName, fontWeight = FontWeight.Bold)
                        Text(if (contact.isOnline) "Đang hoạt động" else contact.role, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
            }
        }
    }
}
