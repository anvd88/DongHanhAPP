package com.ketoanapk.hr.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
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
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.InsertDriveFile
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.ChatBubble
import androidx.compose.material.icons.filled.DeleteOutline
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.Email
import androidx.compose.material.icons.filled.Folder
import androidx.compose.material.icons.filled.Image
import androidx.compose.material.icons.filled.ImageNotSupported
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material.icons.filled.MoreHoriz
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.NotificationsOff
import androidx.compose.material.icons.filled.Person
import androidx.compose.material.icons.filled.PersonAdd
import androidx.compose.material.icons.filled.Phone
import androidx.compose.material.icons.filled.PushPin
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.SentimentSatisfied
import androidx.compose.material.icons.filled.Videocam
import androidx.compose.material.icons.filled.Work
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

enum class ChatFilter(val label: String) {
    All("Tất cả"),
    Personal("Cá nhân"),
    Group("Nhóm"),
    Unread("Chưa đọc"),
}

data class ChatConversationUi(
    val id: String,
    val name: String,
    val preview: String,
    val time: String,
    val initials: String,
    val online: Boolean = false,
    val unread: Int = 0,
    val pinned: Boolean = false,
    val muted: Boolean = false,
    val group: Boolean = false,
)

data class ChatInboxUiState(
    val query: String = "",
    val selectedFilter: ChatFilter = ChatFilter.All,
    val conversations: List<ChatConversationUi> = sampleChatConversations(),
)

data class ChatContactUi(
    val id: String = "am",
    val name: String = "Nguyễn Anh Minh",
    val initials: String = "AM",
    val status: String = "Đang hoạt động",
    val badge: String = "Nhân viên",
    val department: String = "Kế toán tổng hợp",
    val phone: String = "090 123 4567",
    val email: String = "minh@congty.vn",
    val role: String = "Nhân viên",
)

sealed interface ChatMessageUi {
    val id: String

    data class DateChip(
        override val id: String,
        val label: String,
    ) : ChatMessageUi

    data class TextBubble(
        override val id: String,
        val text: String,
        val time: String,
        val mine: Boolean,
        val delivered: Boolean = true,
        val reaction: String? = null,
    ) : ChatMessageUi

    data class ExpiredFile(
        override val id: String,
        val title: String,
        val meta: String,
        val status: String,
        val time: String,
        val mine: Boolean,
    ) : ChatMessageUi

    data class ExpiredImage(
        override val id: String,
        val label: String,
        val time: String,
        val mine: Boolean,
    ) : ChatMessageUi
}

data class ChatThreadUiState(
    val contact: ChatContactUi = ChatContactUi(),
    val input: String = "",
    val messages: List<ChatMessageUi> = sampleChatMessages(),
)

data class ChatSharedItemUi(
    val id: String,
    val title: String,
    val subtitle: String,
    val icon: ImageVector,
)

data class ChatProfileUiState(
    val contact: ChatContactUi = ChatContactUi(),
    val sharedItems: List<ChatSharedItemUi> = sampleSharedItems(),
    val muted: Boolean = false,
    val pinned: Boolean = false,
)

@Composable
fun ChatInboxScreen(
    state: ChatInboxUiState,
    onQueryChange: (String) -> Unit,
    onFilterSelected: (ChatFilter) -> Unit,
    onConversationClick: (ChatConversationUi) -> Unit,
    onNewConversation: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier
            .fillMaxSize()
            .background(ChatBackground),
    ) {
        ChatInboxHeader(onNewConversation = onNewConversation)
        ChatSearchField(
            value = state.query,
            placeholder = "Tìm kiếm",
            onValueChange = onQueryChange,
            modifier = Modifier.padding(horizontal = 16.dp),
        )
        ChatFilterTabs(
            selected = state.selectedFilter,
            onSelect = onFilterSelected,
            modifier = Modifier.padding(top = 12.dp),
        )
        LazyColumn(
            modifier = Modifier.weight(1f),
            contentPadding = PaddingValues(top = 10.dp, bottom = 8.dp),
        ) {
            items(
                items = state.conversations,
                key = { it.id },
            ) { conversation ->
                ChatConversationRow(
                    conversation = conversation,
                    onClick = { onConversationClick(conversation) },
                )
            }
        }
        ChatBottomNavigation()
    }
}

@Composable
fun ChatThreadScreen(
    state: ChatThreadUiState,
    onInputChange: (String) -> Unit,
    onBack: () -> Unit,
    onAddFriend: () -> Unit,
    onOpenProfile: () -> Unit,
    onCall: () -> Unit,
    onVideoCall: () -> Unit,
    onMore: () -> Unit,
    onSend: () -> Unit,
    onAttachFile: () -> Unit,
    onOpenGallery: () -> Unit,
    onVoiceInput: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier
            .fillMaxSize()
            .background(ChatThreadBackground),
    ) {
        ChatThreadHeader(
            contact = state.contact,
            onBack = onBack,
            onOpenProfile = onOpenProfile,
            onCall = onCall,
            onVideoCall = onVideoCall,
            onMore = onMore,
        )
        AddFriendStrip(onAddFriend = onAddFriend)
        LazyColumn(
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth(),
            contentPadding = PaddingValues(horizontal = 16.dp, vertical = 22.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            items(
                items = state.messages,
                key = { it.id },
            ) { message ->
                when (message) {
                    is ChatMessageUi.DateChip -> DateChip(label = message.label)
                    is ChatMessageUi.TextBubble -> MessageBubble(message)
                    is ChatMessageUi.ExpiredFile -> ExpiredFileBubble(message)
                    is ChatMessageUi.ExpiredImage -> ExpiredImageBubble(message)
                }
            }
        }
        ChatComposer(
            value = state.input,
            onValueChange = onInputChange,
            onAttachFile = onAttachFile,
            onOpenGallery = onOpenGallery,
            onVoiceInput = onVoiceInput,
            onSend = onSend,
        )
    }
}

@Composable
fun ChatContactProfileScreen(
    state: ChatProfileUiState,
    onBack: () -> Unit,
    onMore: () -> Unit,
    onMessage: () -> Unit,
    onCall: () -> Unit,
    onVideoCall: () -> Unit,
    onToggleMute: () -> Unit,
    onSearchMessages: () -> Unit,
    onTogglePin: () -> Unit,
    onClearHistory: () -> Unit,
    modifier: Modifier = Modifier,
) {
    LazyColumn(
        modifier = modifier
            .fillMaxSize()
            .background(ChatBackground),
        contentPadding = PaddingValues(bottom = 22.dp),
    ) {
        item {
            ProfileTopBar(onBack = onBack, onMore = onMore)
            ProfileHero(contact = state.contact)
        }
        item {
            ProfileActions(
                muted = state.muted,
                onMessage = onMessage,
                onCall = onCall,
                onVideoCall = onVideoCall,
                onToggleMute = onToggleMute,
            )
        }
        item {
            ProfileInfoCard(contact = state.contact)
        }
        item {
            SharedItemsCard(items = state.sharedItems)
        }
        item {
            ProfileSettingsCard(
                pinned = state.pinned,
                onSearchMessages = onSearchMessages,
                onTogglePin = onTogglePin,
                onClearHistory = onClearHistory,
            )
        }
    }
}

@Composable
private fun ChatInboxHeader(onNewConversation: () -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .statusBarsPadding()
            .padding(start = 16.dp, end = 6.dp, top = 12.dp, bottom = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            text = "Trò chuyện",
            style = MaterialTheme.typography.headlineSmall,
            color = ChatTextPrimary,
            modifier = Modifier.weight(1f),
        )
        IconButton(onClick = { }) {
            Icon(Icons.Filled.Search, contentDescription = "Tìm kiếm", tint = ChatTextPrimary)
        }
        IconButton(onClick = onNewConversation) {
            Icon(Icons.Filled.Add, contentDescription = "Tạo hội thoại", tint = ChatTextPrimary)
        }
    }
}

@Composable
private fun ChatSearchField(
    value: String,
    placeholder: String,
    onValueChange: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    Surface(
        modifier = modifier
            .fillMaxWidth()
            .height(46.dp),
        shape = RoundedCornerShape(16.dp),
        color = ChatSurfaceAlt,
    ) {
        Row(
            modifier = Modifier.padding(horizontal = 14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Icon(Icons.Filled.Search, contentDescription = null, tint = ChatTextSecondary, modifier = Modifier.size(20.dp))
            BasicTextField(
                value = value,
                onValueChange = onValueChange,
                singleLine = true,
                textStyle = TextStyle(color = ChatTextPrimary, fontSize = 16.sp),
                cursorBrush = SolidColor(ChatAccent),
                modifier = Modifier.weight(1f),
                decorationBox = { innerTextField ->
                    Box(contentAlignment = Alignment.CenterStart) {
                        if (value.isBlank()) {
                            Text(placeholder, color = ChatTextMuted, fontSize = 16.sp)
                        }
                        innerTextField()
                    }
                },
            )
        }
    }
}

@Composable
private fun ChatFilterTabs(
    selected: ChatFilter,
    onSelect: (ChatFilter) -> Unit,
    modifier: Modifier = Modifier,
) {
    LazyRow(
        modifier = modifier.fillMaxWidth(),
        contentPadding = PaddingValues(horizontal = 16.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        items(ChatFilter.entries.toList()) { filter ->
            val active = selected == filter
            Surface(
                shape = RoundedCornerShape(999.dp),
                color = if (active) ChatAccent else ChatSurface,
                border = BorderStroke(1.dp, if (active) ChatAccent else ChatOutline),
                onClick = { onSelect(filter) },
            ) {
                Text(
                    text = filter.label,
                    color = if (active) Color.White else ChatTextSecondary,
                    style = MaterialTheme.typography.labelLarge,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.padding(horizontal = 14.dp, vertical = 8.dp),
                )
            }
        }
    }
}

@Composable
private fun ChatConversationRow(
    conversation: ChatConversationUi,
    onClick: () -> Unit,
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .background(ChatSurface)
            .padding(start = 16.dp, end = 14.dp, top = 11.dp, bottom = 11.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            ChatAvatar(
                initials = conversation.initials,
                size = 52,
                online = conversation.online,
            )
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        text = conversation.name,
                        color = ChatTextPrimary,
                        style = MaterialTheme.typography.titleMedium,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f),
                    )
                    if (conversation.pinned) {
                        Icon(Icons.Filled.PushPin, contentDescription = null, tint = ChatTextMuted, modifier = Modifier.size(15.dp))
                        Spacer(Modifier.width(4.dp))
                    }
                    if (conversation.muted) {
                        Icon(Icons.Filled.NotificationsOff, contentDescription = null, tint = ChatTextMuted, modifier = Modifier.size(16.dp))
                        Spacer(Modifier.width(4.dp))
                    }
                    Text(
                        text = conversation.time,
                        color = ChatTextMuted,
                        style = MaterialTheme.typography.labelMedium,
                    )
                }
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        text = conversation.preview,
                        color = if (conversation.unread > 0) ChatTextPrimary else ChatTextSecondary,
                        style = MaterialTheme.typography.bodyMedium,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f),
                    )
                    if (conversation.unread > 0) {
                        Spacer(Modifier.width(8.dp))
                        UnreadBadge(conversation.unread)
                    }
                }
            }
        }
    }
    HorizontalDivider(modifier = Modifier.padding(start = 80.dp), color = ChatOutline.copy(alpha = 0.65f))
}

@Composable
private fun UnreadBadge(count: Int) {
    Box(
        modifier = Modifier
            .size(if (count > 9) 24.dp else 20.dp)
            .clip(CircleShape)
            .background(ChatAccent),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = if (count > 99) "99+" else count.toString(),
            color = Color.White,
            style = MaterialTheme.typography.labelSmall,
            fontWeight = FontWeight.Bold,
            textAlign = TextAlign.Center,
        )
    }
}

@Composable
private fun ChatBottomNavigation() {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .navigationBarsPadding()
            .background(ChatSurface)
            .padding(horizontal = 18.dp, vertical = 8.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        BottomNavItem(Icons.Filled.ChatBubble, "Trò chuyện", active = true)
        BottomNavItem(Icons.Filled.Person, "Danh bạ", active = false)
        BottomNavItem(Icons.Filled.Description, "Công việc", active = false)
        BottomNavItem(Icons.Filled.MoreHoriz, "Thêm", active = false)
    }
}

@Composable
private fun BottomNavItem(icon: ImageVector, label: String, active: Boolean) {
    val color = if (active) ChatAccent else ChatTextMuted
    Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(2.dp)) {
        Icon(icon, contentDescription = label, tint = color, modifier = Modifier.size(22.dp))
        Text(label, color = color, style = MaterialTheme.typography.labelSmall, maxLines = 1)
    }
}

@Composable
private fun ChatThreadHeader(
    contact: ChatContactUi,
    onBack: () -> Unit,
    onOpenProfile: () -> Unit,
    onCall: () -> Unit,
    onVideoCall: () -> Unit,
    onMore: () -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .statusBarsPadding()
            .background(ChatSurface)
            .padding(start = 4.dp, end = 4.dp, top = 8.dp, bottom = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        IconButton(onClick = onBack) {
            Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Quay lại", tint = ChatTextPrimary)
        }
        Column(
            modifier = Modifier
                .weight(1f)
                .clickable(onClick = onOpenProfile),
            verticalArrangement = Arrangement.spacedBy(3.dp),
        ) {
            Text(
                text = contact.name,
                color = ChatTextPrimary,
                style = MaterialTheme.typography.titleLarge,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Surface(shape = RoundedCornerShape(999.dp), color = ChatSurfaceAlt) {
                Text(
                    text = contact.badge,
                    color = ChatTextSecondary,
                    style = MaterialTheme.typography.labelLarge,
                    modifier = Modifier.padding(horizontal = 10.dp, vertical = 3.dp),
                )
            }
        }
        IconButton(onClick = onCall) {
            Icon(Icons.Filled.Phone, contentDescription = "Gọi", tint = ChatTextPrimary)
        }
        IconButton(onClick = onVideoCall) {
            Icon(Icons.Filled.Videocam, contentDescription = "Video", tint = ChatTextPrimary)
        }
        IconButton(onClick = onMore) {
            Icon(Icons.Filled.MoreVert, contentDescription = "Tùy chọn", tint = ChatTextPrimary)
        }
    }
}

@Composable
private fun AddFriendStrip(onAddFriend: () -> Unit) {
    Surface(
        modifier = Modifier.fillMaxWidth(),
        color = ChatSurfaceAlt,
        onClick = onAddFriend,
    ) {
        Row(
            modifier = Modifier.padding(vertical = 14.dp),
            horizontalArrangement = Arrangement.Center,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Icon(Icons.Filled.PersonAdd, contentDescription = null, tint = ChatTextSecondary, modifier = Modifier.size(27.dp))
            Spacer(Modifier.width(10.dp))
            Text("Kết bạn", color = ChatTextPrimary, style = MaterialTheme.typography.titleMedium)
        }
    }
}

@Composable
private fun DateChip(label: String) {
    Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
        Surface(shape = RoundedCornerShape(999.dp), color = Color(0xFFE5E7EB)) {
            Text(
                text = label,
                color = ChatTextSecondary,
                style = MaterialTheme.typography.labelLarge,
                modifier = Modifier.padding(horizontal = 12.dp, vertical = 5.dp),
            )
        }
    }
}

@Composable
private fun MessageBubble(message: ChatMessageUi.TextBubble) {
    Column(
        modifier = Modifier.fillMaxWidth(),
        horizontalAlignment = if (message.mine) Alignment.End else Alignment.Start,
    ) {
        Surface(
            shape = RoundedCornerShape(
                topStart = 18.dp,
                topEnd = 18.dp,
                bottomStart = if (message.mine) 18.dp else 5.dp,
                bottomEnd = if (message.mine) 5.dp else 18.dp,
            ),
            color = if (message.mine) ChatAccent else ChatSurface,
            shadowElevation = 1.dp,
        ) {
            Text(
                text = message.text,
                color = if (message.mine) Color.White else ChatTextPrimary,
                style = MaterialTheme.typography.bodyLarge,
                modifier = Modifier.padding(horizontal = 14.dp, vertical = 10.dp),
            )
        }
        Row(
            modifier = Modifier.padding(top = 4.dp, start = 8.dp, end = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            message.reaction?.let {
                Surface(shape = RoundedCornerShape(999.dp), color = ChatSurface) {
                    Text(it, modifier = Modifier.padding(horizontal = 8.dp, vertical = 2.dp), fontSize = 12.sp)
                }
            }
            Text(
                text = buildString {
                    append(message.time)
                    if (message.mine && message.delivered) append("  ✓✓")
                },
                color = ChatTextMuted,
                style = MaterialTheme.typography.labelSmall,
            )
        }
    }
}

@Composable
private fun ExpiredFileBubble(message: ChatMessageUi.ExpiredFile) {
    Column(
        modifier = Modifier.fillMaxWidth(),
        horizontalAlignment = if (message.mine) Alignment.End else Alignment.Start,
    ) {
        Surface(
            shape = RoundedCornerShape(18.dp),
            color = ChatFileBubble,
            shadowElevation = 1.dp,
        ) {
            Row(
                modifier = Modifier
                    .fillMaxWidth(0.78f)
                    .padding(14.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Box(
                    modifier = Modifier
                        .size(54.dp)
                        .clip(CircleShape)
                        .background(ChatTextPrimary.copy(alpha = 0.12f)),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(Icons.AutoMirrored.Filled.InsertDriveFile, contentDescription = null, tint = ChatTextSecondary, modifier = Modifier.size(28.dp))
                }
                Spacer(Modifier.width(12.dp))
                Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                    Text(
                        text = message.title,
                        color = ChatTextPrimary,
                        style = MaterialTheme.typography.titleMedium,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Text(message.meta, color = ChatTextSecondary, style = MaterialTheme.typography.bodyMedium)
                    Text(message.status, color = ChatTextMuted, style = MaterialTheme.typography.bodyMedium)
                }
            }
        }
        TimePill(message.time, Modifier.padding(top = 6.dp))
    }
}

@Composable
private fun ExpiredImageBubble(message: ChatMessageUi.ExpiredImage) {
    Column(
        modifier = Modifier.fillMaxWidth(),
        horizontalAlignment = if (message.mine) Alignment.End else Alignment.Start,
    ) {
        Row(verticalAlignment = Alignment.Top) {
            if (!message.mine) {
                Box(
                    modifier = Modifier
                        .padding(top = 2.dp)
                        .size(38.dp)
                        .clip(CircleShape)
                        .background(ChatSurface)
                        .then(Modifier),
                )
                Spacer(Modifier.width(10.dp))
            }
            Surface(
                modifier = Modifier
                    .width(238.dp)
                    .height(330.dp),
                shape = RoundedCornerShape(16.dp),
                color = Color(0xFFF1F2F4),
                border = BorderStroke(1.dp, Color(0xFFD1D5DB)),
            ) {
                Column(
                    modifier = Modifier.fillMaxSize(),
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.Center,
                ) {
                    Icon(Icons.Filled.ImageNotSupported, contentDescription = null, tint = ChatTextMuted, modifier = Modifier.size(34.dp))
                    Spacer(Modifier.height(10.dp))
                    Text(message.label, color = ChatTextMuted, style = MaterialTheme.typography.titleMedium)
                }
            }
        }
        TimePill(message.time, Modifier.padding(top = 6.dp, start = if (message.mine) 0.dp else 48.dp))
    }
}

@Composable
private fun TimePill(text: String, modifier: Modifier = Modifier) {
    Surface(modifier = modifier, shape = RoundedCornerShape(999.dp), color = Color(0xFFE5E7EB)) {
        Text(
            text = text,
            color = ChatTextSecondary,
            style = MaterialTheme.typography.labelLarge,
            modifier = Modifier.padding(horizontal = 9.dp, vertical = 3.dp),
        )
    }
}

@Composable
private fun ChatComposer(
    value: String,
    onValueChange: (String) -> Unit,
    onAttachFile: () -> Unit,
    onOpenGallery: () -> Unit,
    onVoiceInput: () -> Unit,
    onSend: () -> Unit,
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .background(ChatSurface)
            .navigationBarsPadding(),
    ) {
        HorizontalDivider(color = ChatOutline)
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 8.dp, end = 8.dp, top = 8.dp, bottom = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(onClick = onAttachFile) {
                Icon(Icons.Filled.SentimentSatisfied, contentDescription = "Biểu cảm", tint = ChatTextSecondary)
            }
            BasicTextField(
                value = value,
                onValueChange = onValueChange,
                singleLine = true,
                textStyle = TextStyle(color = ChatTextPrimary, fontSize = 22.sp),
                cursorBrush = SolidColor(ChatAccent),
                modifier = Modifier.weight(1f),
                decorationBox = { innerTextField ->
                    Box(contentAlignment = Alignment.CenterStart) {
                        if (value.isBlank()) {
                            Text("Tin nhắn", color = ChatTextMuted, fontSize = 22.sp)
                        }
                        innerTextField()
                    }
                },
            )
            IconButton(onClick = onAttachFile) {
                Icon(Icons.Filled.MoreHoriz, contentDescription = "Thêm", tint = ChatTextSecondary)
            }
            IconButton(onClick = onVoiceInput) {
                Icon(Icons.Filled.Mic, contentDescription = "Ghi âm", tint = ChatTextSecondary)
            }
            if (value.isBlank()) {
                IconButton(onClick = onOpenGallery) {
                    Icon(Icons.Filled.Image, contentDescription = "Ảnh", tint = ChatTextSecondary)
                }
            } else {
                IconButton(onClick = onSend) {
                    Icon(Icons.AutoMirrored.Filled.Send, contentDescription = "Gửi", tint = ChatAccent)
                }
            }
        }
    }
}

@Composable
private fun ProfileTopBar(onBack: () -> Unit, onMore: () -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .statusBarsPadding()
            .background(ChatSurface)
            .padding(start = 4.dp, end = 4.dp, top = 8.dp, bottom = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        IconButton(onClick = onBack) {
            Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Quay lại", tint = ChatTextPrimary)
        }
        Text(
            text = "Hồ sơ",
            color = ChatTextPrimary,
            style = MaterialTheme.typography.titleLarge,
            textAlign = TextAlign.Center,
            modifier = Modifier.weight(1f),
        )
        IconButton(onClick = onMore) {
            Icon(Icons.Filled.MoreVert, contentDescription = "Tùy chọn", tint = ChatTextPrimary)
        }
    }
}

@Composable
private fun ProfileHero(contact: ChatContactUi) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .background(
                Brush.verticalGradient(
                    colors = listOf(Color(0xFFFDF2F2), ChatSurface),
                ),
            )
            .padding(top = 20.dp, bottom = 18.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        ChatAvatar(initials = contact.initials, size = 92, online = true)
        Text(contact.name, color = ChatTextPrimary, style = MaterialTheme.typography.headlineSmall, textAlign = TextAlign.Center)
        Text(contact.status, color = OnlineGreen, style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.SemiBold)
        Text(contact.department, color = ChatTextSecondary, style = MaterialTheme.typography.bodyMedium)
    }
}

@Composable
private fun ProfileActions(
    muted: Boolean,
    onMessage: () -> Unit,
    onCall: () -> Unit,
    onVideoCall: () -> Unit,
    onToggleMute: () -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(ChatSurface)
            .padding(horizontal = 18.dp, vertical = 14.dp),
        horizontalArrangement = Arrangement.SpaceAround,
    ) {
        ProfileAction(Icons.Filled.ChatBubble, "Nhắn tin", onMessage)
        ProfileAction(Icons.Filled.Phone, "Gọi", onCall)
        ProfileAction(Icons.Filled.Videocam, "Video", onVideoCall)
        ProfileAction(Icons.Filled.NotificationsOff, if (muted) "Đã tắt" else "Tắt chuông", onToggleMute)
    }
}

@Composable
private fun ProfileAction(icon: ImageVector, label: String, onClick: () -> Unit) {
    Column(
        modifier = Modifier.clickable(onClick = onClick),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(7.dp),
    ) {
        Box(
            modifier = Modifier
                .size(48.dp)
                .clip(CircleShape)
                .background(ChatAccent.copy(alpha = 0.1f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(icon, contentDescription = label, tint = ChatAccent, modifier = Modifier.size(23.dp))
        }
        Text(label, color = ChatTextSecondary, style = MaterialTheme.typography.labelMedium, maxLines = 1)
    }
}

@Composable
private fun ProfileInfoCard(contact: ChatContactUi) {
    ProfileCard(title = "Thông tin") {
        ProfileInfoRow(Icons.Filled.Phone, "Số điện thoại", contact.phone)
        ProfileInfoRow(Icons.Filled.Email, "Email", contact.email)
        ProfileInfoRow(Icons.Filled.Work, "Phòng ban", "Kế toán")
        ProfileInfoRow(Icons.Filled.Person, "Vai trò", contact.role)
    }
}

@Composable
private fun SharedItemsCard(items: List<ChatSharedItemUi>) {
    ProfileCard(title = "Ảnh, file đã chia sẻ") {
        items.forEach { item ->
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(14.dp))
                    .clickable { }
                    .padding(vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Box(
                    modifier = Modifier
                        .size(42.dp)
                        .clip(RoundedCornerShape(12.dp))
                        .background(ChatFileBubble),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(item.icon, contentDescription = null, tint = ChatTextSecondary, modifier = Modifier.size(22.dp))
                }
                Spacer(Modifier.width(12.dp))
                Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    Text(item.title, color = ChatTextPrimary, style = MaterialTheme.typography.bodyLarge, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(item.subtitle, color = ChatTextMuted, style = MaterialTheme.typography.bodySmall)
                }
                Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = null, tint = ChatTextMuted)
            }
        }
    }
}

@Composable
private fun ProfileSettingsCard(
    pinned: Boolean,
    onSearchMessages: () -> Unit,
    onTogglePin: () -> Unit,
    onClearHistory: () -> Unit,
) {
    ProfileCard(title = "Tùy chọn") {
        ProfileOptionRow(Icons.Filled.Search, "Tìm tin nhắn", onSearchMessages)
        ProfileOptionRow(Icons.Filled.PushPin, if (pinned) "Bỏ ghim hội thoại" else "Ghim hội thoại", onTogglePin)
        ProfileOptionRow(Icons.Filled.DeleteOutline, "Xóa lịch sử", onClearHistory, danger = true)
    }
}

@Composable
private fun ProfileCard(title: String, content: @Composable ColumnScope.() -> Unit) {
    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 14.dp, vertical = 8.dp),
        shape = RoundedCornerShape(18.dp),
        color = ChatSurface,
        border = BorderStroke(1.dp, ChatOutline),
    ) {
        Column(modifier = Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(title, color = ChatTextPrimary, style = MaterialTheme.typography.titleMedium)
            content()
        }
    }
}

@Composable
private fun ProfileInfoRow(icon: ImageVector, label: String, value: String) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 7.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Icon(icon, contentDescription = null, tint = ChatTextMuted, modifier = Modifier.size(20.dp))
        Spacer(Modifier.width(12.dp))
        Text(label, color = ChatTextSecondary, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
        Text(value, color = ChatTextPrimary, style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.SemiBold)
    }
}

@Composable
private fun ProfileOptionRow(icon: ImageVector, label: String, onClick: () -> Unit, danger: Boolean = false) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .clickable(onClick = onClick)
            .padding(vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        val color = if (danger) ChatAccent else ChatTextSecondary
        Icon(icon, contentDescription = null, tint = color, modifier = Modifier.size(21.dp))
        Spacer(Modifier.width(12.dp))
        Text(label, color = color, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.weight(1f))
        Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = null, tint = ChatTextMuted)
    }
}

@Composable
private fun ChatAvatar(
    initials: String,
    size: Int,
    online: Boolean,
    modifier: Modifier = Modifier,
) {
    Box(modifier = modifier.size(size.dp)) {
        Box(
            modifier = Modifier
                .matchParentSize()
                .clip(CircleShape)
                .background(Brush.linearGradient(listOf(Color(0xFFCBD5E1), Color(0xFFFEE2E2)))),
            contentAlignment = Alignment.Center,
        ) {
            Text(
                text = initials,
                color = Color(0xFF7F1D1D),
                style = if (size >= 70) MaterialTheme.typography.headlineSmall else MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.ExtraBold,
            )
        }
        if (online) {
            Box(
                modifier = Modifier
                    .align(Alignment.BottomEnd)
                    .size((size * 0.25f).dp)
                    .clip(CircleShape)
                    .background(ChatSurface)
                    .padding(2.dp),
            ) {
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .clip(CircleShape)
                        .background(OnlineGreen),
                )
            }
        }
    }
}

fun sampleChatConversations(): List<ChatConversationUi> = listOf(
    ChatConversationUi(
        id = "am",
        name = "Nguyễn Anh Minh",
        preview = "Mai nộp bảng công giúp em nhé",
        time = "16:32",
        initials = "AM",
        online = true,
        unread = 2,
        pinned = true,
    ),
    ChatConversationUi(
        id = "hr",
        name = "Phòng Nhân sự",
        preview = "Bảng công tháng này đã cập nhật",
        time = "15:08",
        initials = "HR",
        unread = 5,
        group = true,
    ),
    ChatConversationUi(
        id = "accounting",
        name = "Kế toán",
        preview = "Có file mới cần kiểm tra",
        time = "Hôm qua",
        initials = "KT",
        group = true,
        muted = true,
    ),
    ChatConversationUi(
        id = "attendance",
        name = "Tổ Chấm công",
        preview = "Nhắc xác nhận ca làm hôm nay",
        time = "Thứ 3",
        initials = "CC",
        unread = 1,
        group = true,
    ),
    ChatConversationUi(
        id = "news",
        name = "Thông báo công ty",
        preview = "Lịch nghỉ và sự kiện nội bộ",
        time = "12/07",
        initials = "TB",
        group = true,
    ),
)

fun sampleChatMessages(): List<ChatMessageUi> = listOf(
    ChatMessageUi.ExpiredFile(
        id = "file-1",
        title = "BANGCONG_T07.xlsx",
        meta = "XLSX · 248 KB",
        status = "File đã hết hạn",
        time = "09:25",
        mine = true,
    ),
    ChatMessageUi.DateChip(
        id = "date-1",
        label = "17:13 07/01/2026",
    ),
    ChatMessageUi.ExpiredImage(
        id = "image-1",
        label = "Ảnh đã hết hạn",
        time = "17:13",
        mine = false,
    ),
    ChatMessageUi.TextBubble(
        id = "text-1",
        text = "Anh gửi em file bảng công nhé.",
        time = "17:15",
        mine = false,
    ),
    ChatMessageUi.TextBubble(
        id = "text-2",
        text = "Em nhận được rồi ạ.",
        time = "17:16",
        mine = true,
        reaction = "👍 2",
    ),
)

fun sampleSharedItems(): List<ChatSharedItemUi> = listOf(
    ChatSharedItemUi("file-1", "BANGCONG_T07.xlsx", "XLSX · 248 KB", Icons.AutoMirrored.Filled.InsertDriveFile),
    ChatSharedItemUi("photo-1", "Ảnh chấm công", "3 ảnh", Icons.Filled.Image),
    ChatSharedItemUi("folder-1", "Tài liệu nhân sự", "5 file", Icons.Filled.Folder),
)

private val ChatAccent = Color(0xFFC62828)
private val ChatBackground = Color(0xFFF5F6F8)
private val ChatThreadBackground = Color(0xFFFAFAFA)
private val ChatSurface = Color.White
private val ChatSurfaceAlt = Color(0xFFF1F2F4)
private val ChatFileBubble = Color(0xFFE7EEF7)
private val ChatOutline = Color(0xFFE5E7EB)
private val ChatTextPrimary = Color(0xFF111827)
private val ChatTextSecondary = Color(0xFF4B5563)
private val ChatTextMuted = Color(0xFF9CA3AF)
private val OnlineGreen = Color(0xFF22C55E)
