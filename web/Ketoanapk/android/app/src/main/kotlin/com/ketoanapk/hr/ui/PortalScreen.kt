package com.ketoanapk.hr.ui

import android.graphics.BitmapFactory
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.Image
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
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.Article
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.Campaign
import androidx.compose.material.icons.filled.Email
import androidx.compose.material.icons.filled.Event
import androidx.compose.material.icons.filled.Language
import androidx.compose.material.icons.filled.Phone
import androidx.compose.material.icons.filled.Place
import androidx.compose.material.icons.filled.PushPin
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.ketoanapk.hr.data.PortalAbout
import com.ketoanapk.hr.data.PortalPost
import com.ketoanapk.hr.ui.theme.BrandRed
import com.ketoanapk.hr.ui.theme.InfoBlue
import com.ketoanapk.hr.ui.theme.Warning
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.temporal.ChronoUnit
import java.util.Locale

/** Màn hình Cổng thông tin công ty: giới thiệu + sự kiện sắp tới + tin tức/thông báo nội bộ. */
@Composable
fun PortalScreen(
    state: PortalUiState,
    detail: PortalPost?,
    onOpen: (PortalPost) -> Unit,
    onBack: () -> Unit,
) {
    // Đang mở chi tiết một bài → hiển thị toàn bộ nội dung ở màn riêng.
    if (detail != null) {
        BackHandler { onBack() } // Back lùi về danh sách trước, chưa rời tab
        PortalDetailScreen(detail, onBack)
        return
    }

    val feed = state.feed
    if (state.loading && feed == null) {
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            CircularProgressIndicator(color = BrandRed)
        }
        return
    }
    if (feed == null) {
        Box(Modifier.fillMaxSize().padding(24.dp), contentAlignment = Alignment.Center) {
            Text(
                text = state.error ?: "Chưa tải được cổng thông tin.",
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center,
            )
        }
        return
    }

    val isEmpty = !feed.about.hasContent && feed.news.isEmpty() && feed.events.isEmpty()
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        // Sự kiện sắp tới lên ĐẦU (nhân viên hay hóng): bài gần nhất làm thẻ nổi bật đếm ngược.
        if (feed.events.isNotEmpty()) {
            val featured = feed.events.first()
            item(key = "featured${featured.id}") { FeaturedEventCard(featured) { onOpen(featured) } }
            val rest = feed.events.drop(1)
            if (rest.isNotEmpty()) {
                item { SectionHeader(Icons.Filled.Event, "Sự kiện khác sắp tới") }
                items(rest, key = { "e${it.id}" }) { EventCard(it) { onOpen(it) } }
            }
        }

        if (feed.about.hasContent) item { AboutCard(feed.about) }

        if (feed.news.isNotEmpty()) {
            item { SectionHeader(Icons.Filled.Article, "Tin tức & thông báo") }
            items(feed.news, key = { "n${it.id}" }) { NewsCard(it) { onOpen(it) } }
        }

        if (isEmpty) item { EmptyPortal() }
    }
}

/** Thẻ lối vào cổng thông tin đặt ở Trang chủ. */
@Composable
fun PortalEntryCard(onClick: () -> Unit) {
    Surface(
        shape = RoundedCornerShape(20.dp),
        color = Color.Transparent,
        modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(20.dp)).clickable(onClick = onClick),
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .background(Brush.horizontalGradient(listOf(Color(0xFF1B2A41), Color(0xFF2563EB))))
                .padding(16.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Box(
                modifier = Modifier
                    .size(46.dp)
                    .clip(CircleShape)
                    .background(Color.White.copy(alpha = 0.18f)),
                contentAlignment = Alignment.Center,
            ) {
                Icon(Icons.Filled.Campaign, contentDescription = null, tint = Color.White, modifier = Modifier.size(26.dp))
            }
            Spacer(Modifier.width(14.dp))
            Column(Modifier.weight(1f)) {
                Text("Cổng thông tin công ty", color = Color.White, fontWeight = FontWeight.Bold)
                Text(
                    "Tin tức, sự kiện & giới thiệu",
                    color = Color.White.copy(alpha = 0.85f),
                    style = MaterialTheme.typography.bodySmall,
                )
            }
            Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = null, tint = Color.White)
        }
    }
}

@Composable
private fun SectionHeader(icon: androidx.compose.ui.graphics.vector.ImageVector, title: String) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Icon(icon, contentDescription = null, tint = BrandRed, modifier = Modifier.size(20.dp))
        Spacer(Modifier.width(8.dp))
        Text(title, fontWeight = FontWeight.Bold, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
    }
}

@Composable
private fun AboutCard(about: PortalAbout) {
    Surface(
        shape = RoundedCornerShape(20.dp),
        color = MaterialTheme.colorScheme.surface,
        tonalElevation = 1.dp,
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column {
            PortalImage(about.coverImage, Modifier.fillMaxWidth().height(160.dp))
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                if (about.title.isNotBlank()) {
                    Text(about.title, fontWeight = FontWeight.Bold, style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface)
                }
                if (about.content.isNotBlank()) {
                    Text(about.content, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                if (about.address.isNotBlank()) InfoRow(Icons.Filled.Place, about.address)
                if (about.hotline.isNotBlank()) InfoRow(Icons.Filled.Phone, about.hotline)
                if (about.email.isNotBlank()) InfoRow(Icons.Filled.Email, about.email)
                if (about.website.isNotBlank()) InfoRow(Icons.Filled.Language, about.website)
            }
        }
    }
}

@Composable
private fun InfoRow(icon: androidx.compose.ui.graphics.vector.ImageVector, text: String) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Icon(icon, contentDescription = null, tint = InfoBlue, modifier = Modifier.size(18.dp))
        Spacer(Modifier.width(8.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurface)
    }
}

/**
 * Thẻ sự kiện NỔI BẬT ở đầu cổng thông tin: đồng hồ đếm ngược "Còn X ngày" trên nền ảnh/gradient —
 * để nhân viên dễ "hóng" lịch nghỉ/sự kiện gần nhất.
 */
@Composable
private fun FeaturedEventCard(post: PortalPost, onClick: () -> Unit) {
    val bitmap = rememberPortalBitmap(post.coverImage)
    val countdown = eventCountdown(post.eventAt)
    Surface(
        shape = RoundedCornerShape(22.dp),
        color = Color.Transparent,
        modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(22.dp)).clickable(onClick = onClick),
    ) {
        Box(Modifier.fillMaxWidth().heightIn(min = 196.dp)) {
            if (bitmap != null) {
                Image(bitmap = bitmap, contentDescription = null, modifier = Modifier.matchParentSize(), contentScale = ContentScale.Crop)
                Box(Modifier.matchParentSize().background(Brush.verticalGradient(listOf(Color(0x22000000), Color(0xE6000000)))))
            } else {
                Box(Modifier.matchParentSize().background(Brush.linearGradient(listOf(Color(0xFF1B2A41), BrandRed))))
            }
            Column(
                modifier = Modifier.align(Alignment.BottomStart).fillMaxWidth().padding(18.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                Box(
                    modifier = Modifier
                        .clip(RoundedCornerShape(50))
                        .background(Color.White.copy(alpha = 0.22f))
                        .padding(horizontal = 10.dp, vertical = 4.dp),
                ) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Filled.Event, contentDescription = null, tint = Color.White, modifier = Modifier.size(14.dp))
                        Spacer(Modifier.width(4.dp))
                        Text("SỰ KIỆN SẮP TỚI", color = Color.White, fontWeight = FontWeight.Bold, style = MaterialTheme.typography.labelSmall)
                    }
                }
                if (countdown != null) {
                    Text(countdown.label, color = Color.White, fontWeight = FontWeight.Bold, style = MaterialTheme.typography.headlineMedium)
                }
                Text(post.title, color = Color.White, fontWeight = FontWeight.Bold, style = MaterialTheme.typography.titleLarge, maxLines = 2, overflow = TextOverflow.Ellipsis)
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Filled.CalendarMonth, contentDescription = null, tint = Color.White, modifier = Modifier.size(16.dp))
                    Spacer(Modifier.width(6.dp))
                    Text(formatEventWhen(post.eventAt), color = Color.White, fontWeight = FontWeight.SemiBold, style = MaterialTheme.typography.bodyMedium)
                }
                if (post.location.isNotBlank()) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Filled.Place, contentDescription = null, tint = Color.White, modifier = Modifier.size(16.dp))
                        Spacer(Modifier.width(6.dp))
                        Text(post.location, color = Color.White.copy(alpha = 0.92f), style = MaterialTheme.typography.bodyMedium)
                    }
                }
            }
        }
    }
}

/** Nhãn đếm ngược "Còn X ngày" / "Hôm nay" / "Ngày mai". Đỏ khi sắp tới gần (≤7 ngày). */
@Composable
private fun CountdownBadge(cd: EventCountdown) {
    val color = if (cd.urgent) BrandRed else InfoBlue
    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(50))
            .background(color.copy(alpha = 0.14f))
            .padding(horizontal = 10.dp, vertical = 4.dp),
    ) {
        Text(cd.label, color = color, fontWeight = FontWeight.Bold, style = MaterialTheme.typography.labelMedium)
    }
}

@Composable
private fun EventCard(post: PortalPost, onClick: () -> Unit) {
    Surface(
        shape = RoundedCornerShape(18.dp),
        color = MaterialTheme.colorScheme.surface,
        tonalElevation = 1.dp,
        modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(18.dp)).clickable(onClick = onClick),
    ) {
        Column {
            PortalImage(post.coverImage, Modifier.fillMaxWidth().height(150.dp))
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                eventCountdown(post.eventAt)?.let { CountdownBadge(it) }
                Text(post.title, fontWeight = FontWeight.Bold, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, maxLines = 2, overflow = TextOverflow.Ellipsis)
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Filled.CalendarMonth, contentDescription = null, tint = BrandRed, modifier = Modifier.size(16.dp))
                    Spacer(Modifier.width(6.dp))
                    Text(formatEventWhen(post.eventAt), color = BrandRed, fontWeight = FontWeight.SemiBold, style = MaterialTheme.typography.bodyMedium)
                }
                if (post.location.isNotBlank()) InfoRow(Icons.Filled.Place, post.location)
                val preview = previewText(post)
                if (preview.isNotBlank()) {
                    Text(preview, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 2, overflow = TextOverflow.Ellipsis)
                }
                ReadMoreRow()
            }
        }
    }
}

@Composable
private fun NewsCard(post: PortalPost, onClick: () -> Unit) {
    Surface(
        shape = RoundedCornerShape(18.dp),
        color = MaterialTheme.colorScheme.surface,
        tonalElevation = 1.dp,
        modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(18.dp)).clickable(onClick = onClick),
    ) {
        Column {
            PortalImage(post.coverImage, Modifier.fillMaxWidth().height(150.dp))
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    if (post.pinned) {
                        Icon(Icons.Filled.PushPin, contentDescription = "Ghim", tint = Warning, modifier = Modifier.size(16.dp))
                        Spacer(Modifier.width(6.dp))
                    }
                    Text(post.title, fontWeight = FontWeight.Bold, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurface, modifier = Modifier.weight(1f), maxLines = 2, overflow = TextOverflow.Ellipsis)
                }
                val preview = previewText(post)
                if (preview.isNotBlank()) {
                    Text(preview, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 2, overflow = TextOverflow.Ellipsis)
                }
                val meta = buildString {
                    append(formatIsoDate(post.createdAt))
                    if (post.authorName.isNotBlank()) append(" • ${post.authorName}")
                }
                Text(meta, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
                ReadMoreRow()
            }
        }
    }
}

/** Gợi ý "Xem chi tiết" ở cuối thẻ để người dùng biết bấm vào xem đầy đủ. */
@Composable
private fun ReadMoreRow() {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text("Xem chi tiết", color = BrandRed, fontWeight = FontWeight.SemiBold, style = MaterialTheme.typography.bodySmall)
        Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = null, tint = BrandRed, modifier = Modifier.size(18.dp))
    }
}

/** Màn chi tiết một bài: hiển thị đầy đủ ảnh bìa, tiêu đề, thời gian/địa điểm và toàn bộ nội dung. */
@Composable
private fun PortalDetailScreen(post: PortalPost, onBack: () -> Unit) {
    Column(Modifier.fillMaxSize()) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 4.dp, vertical = 4.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(onClick = onBack) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Quay lại", tint = MaterialTheme.colorScheme.onSurface)
            }
            Text(
                if (post.kind == "event") "Chi tiết sự kiện" else "Chi tiết tin tức",
                fontWeight = FontWeight.Bold,
                style = MaterialTheme.typography.titleMedium,
                color = MaterialTheme.colorScheme.onSurface,
            )
        }

        Column(
            modifier = Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            val bitmap = rememberPortalBitmap(post.coverImage)
            if (bitmap != null) {
                Image(
                    bitmap = bitmap,
                    contentDescription = null,
                    modifier = Modifier.fillMaxWidth().height(200.dp).clip(RoundedCornerShape(18.dp)),
                    contentScale = ContentScale.Crop,
                )
            }

            Row(verticalAlignment = Alignment.CenterVertically) {
                if (post.pinned) {
                    Icon(Icons.Filled.PushPin, contentDescription = "Ghim", tint = Warning, modifier = Modifier.size(18.dp))
                    Spacer(Modifier.width(6.dp))
                }
                Text(post.title, fontWeight = FontWeight.Bold, style = MaterialTheme.typography.headlineSmall, color = MaterialTheme.colorScheme.onSurface)
            }

            if (post.kind == "event") {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Filled.CalendarMonth, contentDescription = null, tint = BrandRed, modifier = Modifier.size(18.dp))
                    Spacer(Modifier.width(6.dp))
                    Text(formatEventWhen(post.eventAt), color = BrandRed, fontWeight = FontWeight.SemiBold, style = MaterialTheme.typography.bodyLarge)
                }
                if (post.location.isNotBlank()) InfoRow(Icons.Filled.Place, post.location)
            }

            if (post.summary.isNotBlank()) {
                Text(post.summary, style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            if (post.body.isNotBlank()) {
                Text(post.body, style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.onSurface)
            }

            val meta = buildString {
                append(formatIsoDate(post.createdAt))
                if (post.authorName.isNotBlank()) append(" • ${post.authorName}")
            }
            Text(meta, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Spacer(Modifier.height(8.dp))
        }
    }
}

/** Đoạn mô tả ngắn cho thẻ danh sách: ưu tiên tóm tắt, không có thì lấy đầu nội dung. */
private fun previewText(post: PortalPost): String {
    if (post.summary.isNotBlank()) return post.summary
    val body = post.body.trim().replace(Regex("\\s+"), " ")
    return if (body.length > 140) body.take(140).trimEnd() + "…" else body
}

@Composable
private fun EmptyPortal() {
    Column(
        modifier = Modifier.fillMaxWidth().padding(top = 48.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Icon(Icons.Filled.Campaign, contentDescription = null, tint = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.size(46.dp))
        Text("Chưa có thông tin", fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface)
        Text(
            "Công ty chưa đăng tin tức hay sự kiện nào.",
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
        )
    }
}

/** Ảnh bìa base64 (data URL). Không có ảnh → không chiếm chỗ. */
@Composable
private fun PortalImage(data: String?, modifier: Modifier) {
    val bitmap = rememberPortalBitmap(data) ?: return
    Image(bitmap = bitmap, contentDescription = null, modifier = modifier, contentScale = ContentScale.Crop)
}

@Composable
private fun rememberPortalBitmap(data: String?): ImageBitmap? = remember(data) {
    if (data.isNullOrBlank()) return@remember null
    runCatching {
        val comma = data.indexOf(',')
        val raw = if (data.startsWith("data:") && comma >= 0) data.substring(comma + 1) else data
        val bytes = android.util.Base64.decode(raw, android.util.Base64.DEFAULT)
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
    }.getOrNull()
}

/** Thông tin đếm ngược tới một sự kiện. */
private data class EventCountdown(val label: String, val urgent: Boolean, val days: Long)

/** Tính số ngày còn lại tới sự kiện (theo ngày, múi giờ thiết bị) → nhãn thân thiện. */
private fun eventCountdown(iso: String?): EventCountdown? {
    if (iso.isNullOrBlank()) return null
    val date = runCatching {
        OffsetDateTime.parse(iso).atZoneSameInstant(ZoneId.systemDefault()).toLocalDate()
    }.getOrNull() ?: return null
    val days = ChronoUnit.DAYS.between(LocalDate.now(), date)
    return when {
        days < 0L -> EventCountdown("Đã diễn ra", urgent = false, days = days)
        days == 0L -> EventCountdown("Hôm nay", urgent = true, days = 0)
        days == 1L -> EventCountdown("Ngày mai", urgent = true, days = 1)
        else -> EventCountdown("Còn $days ngày", urgent = days <= 7, days = days)
    }
}

/** ISO UTC → "Thứ ..., dd/MM/yyyy • HH:mm" theo múi giờ thiết bị. */
private fun formatEventWhen(iso: String?): String {
    if (iso.isNullOrBlank()) return "--"
    return runCatching {
        OffsetDateTime.parse(iso)
            .atZoneSameInstant(ZoneId.systemDefault())
            .format(DateTimeFormatter.ofPattern("EEEE, dd/MM/yyyy • HH:mm", Locale("vi")))
            .replaceFirstChar { it.uppercase() }
    }.getOrElse { formatIsoDate(iso) }
}
