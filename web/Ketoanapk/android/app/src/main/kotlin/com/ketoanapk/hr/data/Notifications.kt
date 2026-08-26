package com.ketoanapk.hr.data

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import androidx.core.app.NotificationManagerCompat
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.security.MessageDigest
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.ZonedDateTime
import java.time.format.DateTimeFormatter
import java.time.format.DateTimeParseException
import java.util.Locale

/** Mốc thời gian ISO của máy chủ → epoch millis. Không đọc được thì trả null để bên gọi tự lùi. */
internal fun parseServerTime(raw: String?): Long? {
    val value = raw?.trim().orEmpty()
    if (value.isEmpty()) return null
    return try {
        java.time.OffsetDateTime.parse(value).toInstant().toEpochMilli()
    } catch (_: DateTimeParseException) {
        // Máy chủ có lúc trả dạng không kèm offset ("2026-08-26T09:30:00"); coi như giờ máy.
        runCatching { LocalDateTime.parse(value).atZone(java.time.ZoneId.systemDefault()).toInstant().toEpochMilli() }
            .getOrNull()
    }
}

const val APP_UPDATE_NOTIFICATION_TARGET = "AppUpdate"
const val MISSED_CHECKOUT_LOOKBACK_DAYS = 31
/** API cũ không có policy: bám đúng giờ kết ca; contract mới gửi checkoutGraceMinutes theo từng ca. */
internal const val MISSED_CHECKOUT_FALLBACK_GRACE_MINUTES = 0L
internal const val MISSED_CHECKOUT_NOTIFICATION_PREFIX = "attendance:missing-checkout:"
private const val MISSED_CHECKOUT_ENTITY_PREFIX = "forgot-checkout:"

/**
 * Nhóm thông báo, dùng để chọn icon/màu và điểm đến khi bấm vào.
 *
 * Năm giá trị cuối đến từ HỘP THƯ TRÊN MÁY CHỦ (web_notifications) — tiến trình giao hàng, thu tiền,
 * chứng từ, phiếu chi và việc được giao. Thêm giá trị vào cuối là an toàn: snapshot cũ trên máy chỉ
 * chứa các giá trị cũ nên vẫn đọc được.
 */
@Serializable
enum class NotificationKind {
    Request, Approval, Penalty, Attendance, Chat, System,
    Delivery, Collection, Document, Payout, Task,
}

/** category của máy chủ → nhóm hiển thị trong app. Lạ thì về System chứ không làm rơi thông báo. */
internal fun notificationKindFromCategory(category: String?): NotificationKind =
    when (category.orEmpty().trim().lowercase(Locale.ROOT)) {
        "delivery" -> NotificationKind.Delivery
        "collection" -> NotificationKind.Collection
        "document" -> NotificationKind.Document
        "payout" -> NotificationKind.Payout
        "task" -> NotificationKind.Task
        "request" -> NotificationKind.Request
        "penalty" -> NotificationKind.Penalty
        "attendance" -> NotificationKind.Attendance
        "chat" -> NotificationKind.Chat
        else -> NotificationKind.System
    }

/**
 * Một thông báo hiển thị trong chuông. Lưu bền vững trên máy (DataStore) kèm trạng thái đã đọc.
 * [target] là tên [com.ketoanapk.hr.ui.HrDestination] để điều hướng khi bấm vào.
 */
@Serializable
data class AppNotification(
    val id: String,
    val kind: NotificationKind,
    val title: String,
    val body: String,
    val createdAt: Long,
    val read: Boolean = false,
    val target: String? = null,
    val entityId: String? = null,
    /** Đã dựng thành công notification hệ thống; dùng để backfill đúng một lần sau khi cấp quyền. */
    val systemDelivered: Boolean = false,
    /**
     * Khoá dòng tương ứng trong hộp thư máy chủ, nếu thông báo này đến từ đó. Có nó thì "đánh dấu đã
     * đọc" trên app báo ngược lên máy chủ được, nhờ vậy chuông trên web cũng hết chấm đỏ.
     */
    val serverId: Long? = null,
)

@Serializable
private data class NotifSnapshot(
    val items: List<AppNotification> = emptyList(),
    val seen: List<String> = emptyList(),
)

internal fun retainedSeenSignatures(seen: Iterable<String>, limit: Int = 400): List<String> =
    if (limit <= 0) emptyList() else seen.toList().takeLast(limit)

private val Context.notifyStore: DataStore<Preferences> by preferencesDataStore(name = "ketoanapk_notifications")

/** Kho lưu thông báo + tập "chữ ký đã thấy" (để không báo trùng) trên thiết bị. */
internal fun notificationAccountScope(username: String): String {
    val normalized = username.trim().lowercase(Locale.ROOT).ifBlank { "signed-out" }
    return MessageDigest.getInstance("SHA-256")
        .digest(normalized.toByteArray(Charsets.UTF_8))
        .take(16)
        .joinToString("") { "%02x".format(it) }
}

/** Attendance fail-closed vì actionable theo cá nhân; payload chung loại khác được phép không có scope. */
internal fun notificationRecipientMatches(
    kind: NotificationKind,
    currentAccountScope: String,
    incomingRecipientScope: String?,
): Boolean {
    if (currentAccountScope.isBlank()) return false
    val incoming = incomingRecipientScope.orEmpty().trim().lowercase(Locale.ROOT)
    if (incoming.isBlank()) return kind != NotificationKind.Attendance
    return incoming == currentAccountScope.lowercase(Locale.ROOT)
}

class NotificationStore(context: Context, accountId: String) {
    private val appContext = context.applicationContext
    // Không dùng username thô trong Preferences và tuyệt đối không dùng chung snapshot giữa tài khoản.
    private val keyData = stringPreferencesKey("data_${notificationAccountScope(accountId)}")
    private val legacyKeyData = stringPreferencesKey("data")
    private val json = Json { ignoreUnknownKeys = true }

    suspend fun load(): Pair<List<AppNotification>, LinkedHashSet<String>> {
        val prefs = appContext.notifyStore.data.first()
        val raw = prefs[keyData]
        if (raw == null) {
            // Snapshot bản cũ không biết chủ tài khoản nên không được migrate. Dọn cả khay legacy để
            // tránh người B mở notification của người A sau nâng cấp/phiên hết hạn.
            prefs[legacyKeyData]?.let { legacyRaw ->
                val legacy = runCatching { json.decodeFromString<NotifSnapshot>(legacyRaw) }.getOrDefault(NotifSnapshot())
                legacy.items.forEach { NotificationManagerCompat.from(appContext).cancel(it.id.hashCode()) }
                appContext.notifyStore.edit { it.remove(legacyKeyData) }
            }
            return emptyList<AppNotification>() to linkedSetOf()
        }
        val snap = runCatching { json.decodeFromString<NotifSnapshot>(raw) }.getOrDefault(NotifSnapshot())
        // Snapshot là List có thứ tự; LinkedHashSet giữ đúng thứ tự thêm để retention luôn loại chữ ký cũ.
        return snap.items to snap.seen.toCollection(LinkedHashSet())
    }

    suspend fun save(items: List<AppNotification>, seen: Set<String>) {
        val trimmedItems = items.sortedByDescending { it.createdAt }.take(120)
        val trimmedSeen = retainedSeenSignatures(seen)
        val raw = json.encodeToString(NotifSnapshot(trimmedItems, trimmedSeen))
        appContext.notifyStore.edit { it[keyData] = raw }
    }

    suspend fun clear() {
        appContext.notifyStore.edit { it.remove(keyData) }
    }
}

/**
 * Bộ tạo thông báo: so sánh dữ liệu mới lấy từ máy chủ với những gì đã thấy trước đó để sinh
 * thông báo cho: đơn của mình được duyệt/từ chối, đơn mới chờ duyệt (quản trị), quyết định phạt mới.
 *
 * Lần đồng bộ đầu tiên (kho rỗng) chỉ "ghi nhớ" hiện trạng, KHÔNG bắn thông báo để tránh spam.
 */
class NotificationCenter(context: Context, val accountId: String) {
    private val appContext = context.applicationContext
    val accountScope: String = notificationAccountScope(accountId)
    private val store = NotificationStore(appContext, accountId)
    private var items: List<AppNotification> = emptyList()
    private var seen: LinkedHashSet<String> = linkedSetOf()
    private var loaded = false

    companion object {
        /** Mọi instance (UI, WorkManager, FCM) cùng tiến trình phải merge tuần tự cùng DataStore. */
        private val mutationMutex = Mutex()
    }

    /**
     * Đọc lại từ kho chung. QUAN TRỌNG cho chống trùng: [HrMessagingService] (tiến trình cùng app,
     * instance khác) có thể vừa ghi thêm "chữ ký" khi nhận FCM — reload để mọi thao tác thấy dữ liệu mới nhất.
     */
    private suspend fun reload() {
        val (savedItems, savedSeen) = store.load()
        items = savedItems
        seen = savedSeen
        loaded = true
    }

    suspend fun load(installedVersionCode: Int? = null): List<AppNotification> {
        return mutationMutex.withLock {
            reload()
            if (installedVersionCode != null) markObsoleteAppUpdatesReadLoaded(installedVersionCode)
            items
        }
    }

    val current: List<AppNotification> get() = items
    fun unreadCount(): Int = items.count { !it.read }

    suspend fun markRead(id: String): List<AppNotification> {
        return mutationMutex.withLock {
            reload()
            items.firstOrNull { it.id == id }?.let(::cancelSystem)
            items = items.map { if (it.id == id) it.copy(read = true) else it }
            store.save(items, seen)
            items
        }
    }

    /**
     * Một ngày có thể có ID gốc và nhiều thế hệ `:retry:<requestId>` sau khi đơn cũ bị từ chối/hủy.
     * Khi người dùng gửi đơn thay thế thành công, dọn toàn bộ thế hệ của ngày đó ngay, không chờ lần
     * đồng bộ bảng công tiếp theo mới gỡ notification khỏi khay.
     */
    suspend fun resolveMissedCheckout(workDate: String): List<AppNotification> {
        val canonicalDate = runCatching { LocalDate.parse(workDate).toString() }.getOrNull() ?: return current
        val baseId = "$MISSED_CHECKOUT_NOTIFICATION_PREFIX$canonicalDate"
        return mutationMutex.withLock {
            reload()
            val resolvedIds = items.asSequence()
                .map(AppNotification::id)
                .filter { it == baseId || it.startsWith("$baseId:") }
                .toHashSet()
            if (resolvedIds.isEmpty()) return@withLock items
            items.filter { it.id in resolvedIds }.forEach(::cancelSystem)
            items = items.map {
                if (it.id in resolvedIds) it.copy(read = true, systemDelivered = true) else it
            }
            store.save(items, seen)
            items
        }
    }

    suspend fun markAllRead(): List<AppNotification> {
        return mutationMutex.withLock {
            reload()
            items.filterNot { it.read }.forEach(::cancelSystem)
            items = items.map { if (it.read) it else it.copy(read = true) }
            store.save(items, seen)
            items
        }
    }

    /**
     * Cập nhật APK xong thì push `release:<versionCode>` cũ không còn là việc chưa đọc. Giữ dòng đã đọc
     * trong lịch sử, nhưng loại nó khỏi badge/thanh gõ chữ và gỡ thông báo khay hệ thống.
     * [noUpdateAvailable] dùng sau khi máy chủ xác nhận app đã là bản mới nhất; khi đó dọn cả push
     * di sản không có versionCode hợp lệ.
     */
    suspend fun markObsoleteAppUpdatesRead(
        installedVersionCode: Int,
        noUpdateAvailable: Boolean = false,
    ): List<AppNotification> {
        return mutationMutex.withLock {
            reload()
            markObsoleteAppUpdatesReadLoaded(installedVersionCode, noUpdateAvailable)
            items
        }
    }

    private suspend fun markObsoleteAppUpdatesReadLoaded(
        installedVersionCode: Int,
        noUpdateAvailable: Boolean = false,
    ) {
        val obsolete = items.filter {
            isObsoleteAppUpdateNotification(it, installedVersionCode, noUpdateAvailable)
        }
        if (obsolete.isEmpty()) return
        obsolete.forEach(::cancelSystem)
        if (obsolete.none { !it.read }) return
        val ids = obsolete.asSequence().map(AppNotification::id).toHashSet()
        items = items.map { if (!it.read && it.id in ids) it.copy(read = true) else it }
        store.save(items, seen)
    }

    suspend fun clearAll(): List<AppNotification> {
        return mutationMutex.withLock {
            reload()
            items.forEach(::cancelSystem)
            items = emptyList()
            store.save(items, seen)
            items
        }
    }

    /**
     * Nhận một thông báo đẩy FCM: nếu [notifId] đã thấy trước đó (đã hiển thị qua luồng khác) thì bỏ qua
     * (trả về null); ngược lại ghi nhận chữ ký + thêm vào danh sách và trả về để bắn lên khay hệ thống.
     */
    suspend fun ingestFromPush(
        notifId: String,
        kind: NotificationKind,
        title: String,
        body: String,
        target: String?,
    ): AppNotification? {
        return mutationMutex.withLock {
            reload()
            if (notifId.isNotBlank() && seen.contains(notifId)) return@withLock null
            val id = if (notifId.isBlank()) "fcm:${System.currentTimeMillis()}" else notifId
            val incoming = AppNotification(
                id = id,
                kind = kind,
                title = title,
                body = body,
                createdAt = System.currentTimeMillis(),
                target = target,
                entityId = entityIdFromNotificationId(notifId),
            )
            // FCM có thể giao muộn sau khi APK mới đã cài. Ghi nhận chữ ký để không nhận lặp, nhưng
            // không tạo một thông báo "cập nhật" sai thời điểm.
            if (isObsoleteAppUpdateNotification(incoming, AppUpdater.installedVersionCode(appContext))) {
                if (notifId.isNotBlank()) seen.add(notifId)
                store.save(items, seen)
                return@withLock null
            }
            if (notifId.isNotBlank()) seen.add(notifId)
            items = (listOf(incoming) + items).distinctBy { it.id }
            store.save(items, seen)
            incoming
        }
    }

    /**
     * Trộn HỘP THƯ TRÊN MÁY CHỦ vào chuông của app. Trả về các dòng LẦN ĐẦU thấy để bắn lên khay hệ thống.
     *
     * Khoá chống trùng là `notifId` — cùng chữ ký với gói FCM, nên một sự kiện đã rung máy qua push
     * thì lần đồng bộ này không dựng thêm thông báo thứ hai.
     *
     * [firstSync] (kho còn rỗng) chỉ GHI NHỚ chứ không bắn: người vừa cài lại app không đáng bị dội
     * 30 thông báo cũ cùng lúc. Trạng thái "đã đọc" luôn lấy theo máy chủ vì đó là nơi web cũng ghi.
     */
    suspend fun ingestFromServer(feed: List<ServerNotification>): List<AppNotification> {
        if (feed.isEmpty()) return emptyList()
        return mutationMutex.withLock {
            reload()
            val firstSync = seen.isEmpty()
            val byId = items.associateBy(AppNotification::id)
            val fresh = mutableListOf<AppNotification>()
            val merged = mutableListOf<AppNotification>()

            for (row in feed) {
                val id = row.notifId.ifBlank { "srv:${row.id}" }
                val existing = byId[id]
                val notification = AppNotification(
                    id = id,
                    kind = notificationKindFromCategory(row.category),
                    title = row.title,
                    body = row.body,
                    createdAt = parseServerTime(row.createdAt) ?: existing?.createdAt ?: System.currentTimeMillis(),
                    // Máy chủ là nguồn sự thật của "đã đọc"; đọc trên web thì app cũng phải hết đỏ.
                    read = row.read || existing?.read == true,
                    target = row.appTarget.takeIf(String::isNotBlank) ?: existing?.target,
                    entityId = existing?.entityId ?: entityIdFromNotificationId(id),
                    systemDelivered = existing?.systemDelivered ?: false,
                    serverId = row.id,
                )
                merged += notification
                val isNew = seen.add(id)
                if (isNew && !firstSync && !notification.read) fresh += notification
            }

            val mergedIds = merged.mapTo(hashSetOf(), AppNotification::id)
            items = (merged + items.filterNot { it.id in mergedIds }).sortedByDescending { it.createdAt }
            store.save(items, seen)
            fresh
        }
    }

    /** Các nhắc công chưa từng lên khay (thường do lúc phát hiện chưa có quyền thông báo). */
    suspend fun pendingSystemAttendance(): List<AppNotification> = mutationMutex.withLock {
        reload()
        items.filter { !it.read && !it.systemDelivered && it.kind == NotificationKind.Attendance }
    }

    /** Claim + show + persist nằm trong cùng critical section để UI/Worker/FCM không rung hai lần. */
    suspend fun deliverPendingSystemAttendance(): List<AppNotification> = mutationMutex.withLock {
        reload()
        val delivered = items.asSequence()
            .filter { !it.read && !it.systemDelivered && it.kind == NotificationKind.Attendance }
            .filter { AppNotifier.show(appContext, it, accountId) }
            .map(AppNotification::id)
            .toHashSet()
        if (delivered.isNotEmpty()) {
            items = items.map { if (it.id in delivered) it.copy(systemDelivered = true) else it }
            store.save(items, seen)
        }
        items
    }

    suspend fun markSystemDelivered(ids: Collection<String>): List<AppNotification> = mutationMutex.withLock {
        if (ids.isEmpty()) return@withLock items
        reload()
        val delivered = ids.toHashSet()
        items = items.map { if (it.id in delivered) it.copy(systemDelivered = true) else it }
        store.save(items, seen)
        items
    }

    /** Xoá toàn bộ dữ liệu thông báo khi đăng xuất. */
    suspend fun reset() {
        mutationMutex.withLock {
            reload()
            items.forEach(::cancelSystem)
            items = emptyList()
            seen = linkedSetOf()
            loaded = false
            store.clear()
        }
    }

    /**
     * Đồng bộ với dữ liệu mới. Trả về các thông báo MỚI sinh ra (để bắn thông báo hệ thống).
     * Danh sách tổng có thể lấy qua [current] sau khi gọi.
     */
    suspend fun sync(
        myRequests: List<RequestListItem>,
        inbox: List<RequestListItem>,
        penalties: List<Penalty>,
        isAdmin: Boolean,
        attendanceSheets: List<Timesheet> = emptyList(),
        nowVietnam: ZonedDateTime = ServerClock.nowVietnam(),
        lookbackDays: Int = MISSED_CHECKOUT_LOOKBACK_DAYS,
    ): List<AppNotification> {
        return mutationMutex.withLock {
            reload()
            val firstRun = seen.isEmpty()
            val now = System.currentTimeMillis()
            val fresh = mutableListOf<AppNotification>()

        // 1) Đơn của mình được duyệt / từ chối.
        for (r in myRequests) {
            val status = r.status.lowercase()
            if (status != "approved" && status != "rejected") continue
            val sig = "req:${r.id}:$status"
            if (seen.add(sig) && !firstRun) {
                val approved = status == "approved"
                fresh += AppNotification(
                    id = sig,
                    kind = NotificationKind.Request,
                    title = if (approved) "Đơn đã được duyệt" else "Đơn bị từ chối",
                    body = "${r.typeLabel.ifBlank { r.title }} · ${r.requestNo}",
                    createdAt = now,
                    target = "Requests",
                    entityId = r.id,
                )
            }
        }

        // 2) Đơn mới chờ duyệt — cho MỌI người có trong hộp thư (đã lọc sẵn ở máy chủ theo
        //    người duyệt: quản lý trực tiếp hoặc quản trị). Nhờ đó quản lý không phải admin cũng được nhắc.
        for (r in inbox) {
            if (!r.status.equals("Pending", true)) continue
            val sig = "inbox:${r.id}"
            if (seen.add(sig) && !firstRun) {
                fresh += AppNotification(
                    id = sig,
                    kind = NotificationKind.Approval,
                    title = "Đơn mới chờ duyệt",
                    body = "${r.employeeName.ifBlank { r.requesterUsername }} · ${r.typeLabel.ifBlank { r.title }}",
                    createdAt = now,
                    target = "Approval",
                    entityId = r.id,
                )
            }
        }

        // 3) Quyết định phạt mới (danh sách "của mình" với nhân viên thường).
        if (!isAdmin) {
            for (p in penalties) {
                val sig = "pen:${p.id}"
                if (seen.add(sig) && !firstRun) {
                    fresh += AppNotification(
                        id = sig,
                        kind = NotificationKind.Penalty,
                        title = "Quyết định phạt mới",
                        body = "${p.penaltyTypeLabel.ifBlank { p.penaltyType }} · ${p.penaltyNo}",
                        createdAt = now,
                        target = "Penalty",
                    )
                }
            }
        }

        // 4) Ngày đã kết thúc nhưng có giờ vào mà chưa có giờ ra. Đây là việc người dùng cần xử lý
        //    ngay cả ở lần đồng bộ đầu tiên, nên vẫn phát nhắc nhở trong khi các thông báo lịch sử khác
        //    được im lặng ghi nhận để tránh spam khi mới cài/đăng nhập app.
            val missed = missedCheckoutNotifications(attendanceSheets, nowVietnam, lookbackDays, now)
            val missedIds = missed.mapTo(hashSetOf()) { it.id }
            val coveredMonths = attendanceSheets.mapTo(hashSetOf()) { it.period.take(7) }
            val coveredDates = (1..lookbackDays.coerceAtLeast(0)).asSequence()
                .map { nowVietnam.toLocalDate().minusDays(it.toLong()) }
                .filter { it.toString().take(7) in coveredMonths }
                .map(LocalDate::toString)
                .toHashSet()

            // Nếu server đã có giờ ra (hoặc ngày không còn dòng vào), reminder cũ không còn actionable.
            val staleIds = items.asSequence()
                .filter { it.id.startsWith(MISSED_CHECKOUT_NOTIFICATION_PREFIX) }
                .filter { it.id.removePrefix(MISSED_CHECKOUT_NOTIFICATION_PREFIX).take(10) in coveredDates }
                .filter { it.id !in missedIds }
                .map(AppNotification::id)
                .toHashSet()
            if (staleIds.isNotEmpty()) {
                items.filter { it.id in staleIds }.forEach(::cancelSystem)
                items = items.map { if (it.id in staleIds) it.copy(read = true, systemDelivered = true) else it }
            }

            missed.forEach { reminder ->
                if (seen.add(reminder.id)) fresh += reminder
            }

            if (fresh.isNotEmpty() || firstRun || staleIds.isNotEmpty()) {
                items = (fresh + items).distinctBy { it.id }
                store.save(items, seen)
            }
            if (firstRun) fresh.filter { it.kind == NotificationKind.Attendance } else fresh
        }
    }

    private fun cancelSystem(notification: AppNotification) {
        AppNotifier.cancel(appContext, notification, accountId)
        // Dọn notification không tag từ các bản app trước khi account-scoping.
        NotificationManagerCompat.from(appContext).cancel(notification.id.hashCode())
    }
}

internal fun missedCheckoutMonthKeys(
    today: LocalDate,
    lookbackDays: Int = MISSED_CHECKOUT_LOOKBACK_DAYS,
): List<String> = (1..lookbackDays.coerceAtLeast(0))
    .map { today.minusDays(it.toLong()).format(DateTimeFormatter.ofPattern("yyyy-MM")) }
    .distinct()

/**
 * Quét các ngày chưa được xử lý trong cửa sổ catch-up. Ca qua đêm chỉ được nhắc sau giờ kết thúc D+1
 * và một khoảng an toàn; ca không có metadata được hoãn tới 06:00 sáng hôm sau để tránh báo lúc 00:xx.
 */
internal fun missedCheckoutNotifications(
    sheets: List<Timesheet>,
    nowVietnam: ZonedDateTime,
    lookbackDays: Int = MISSED_CHECKOUT_LOOKBACK_DAYS,
    createdAt: Long = System.currentTimeMillis(),
): List<AppNotification> {
    if (lookbackDays <= 0) return emptyList()
    val daysByDate = sheets.asSequence().flatMap { it.days.asSequence() }
        .associateBy { it.date.take(10) }
    return (1..lookbackDays).mapNotNull { offset ->
        val missedDate = nowVietnam.toLocalDate().minusDays(offset.toLong())
        val day = daysByDate[missedDate.toString()] ?: return@mapNotNull null
        if (day.checkIn.isNullOrBlank() || !day.checkOut.isNullOrBlank()) return@mapNotNull null
        val requestStatus = day.missingCheckoutRequestStatus?.trim()?.lowercase(Locale.ROOT)
        val requestAlreadyHandlesIt = day.hasOpenCheckoutRequest == true || requestStatus in
            setOf("pending", "approved", "resolved", "completed")
        if (requestAlreadyHandlesIt) return@mapNotNull null
        if (!missedCheckoutWindowElapsed(day, missedDate, nowVietnam.toLocalDateTime())) return@mapNotNull null
        val retryGeneration = if (requestStatus in setOf("rejected", "cancelled", "canceled")) {
            day.missingCheckoutRequestId?.takeIf(String::isNotBlank) ?: "latest"
        } else null
        missedCheckoutNotificationForDate(missedDate, createdAt, retryGeneration)
    }
}

private fun missedCheckoutWindowElapsed(
    day: TimesheetDay,
    workDate: LocalDate,
    nowVietnam: LocalDateTime,
): Boolean {
    val start = runCatching { LocalTime.parse(day.shiftStart) }.getOrNull()
    val end = runCatching { LocalTime.parse(day.shiftEnd) }.getOrNull()
    val eligibleAt = if (end != null) {
        val overnight = day.isOvernight ?: (start != null && !end.isAfter(start))
        val graceMinutes = day.checkoutGraceMinutes
            ?.coerceIn(0, 12 * 60)
            ?.toLong()
            ?: MISSED_CHECKOUT_FALLBACK_GRACE_MINUTES
        LocalDateTime.of(if (overnight) workDate.plusDays(1) else workDate, end)
            .plusMinutes(graceMinutes)
    } else {
        LocalDateTime.of(workDate.plusDays(1), LocalTime.of(6, 0))
    }
    return !nowVietnam.isBefore(eligibleAt)
}

private fun missedCheckoutNotificationForDate(
    missedDate: LocalDate,
    createdAt: Long,
    retryGeneration: String? = null,
): AppNotification {
    val date = missedDate.toString()
    val displayDate = missedDate.format(DateTimeFormatter.ofPattern("d/M/yyyy"))
    return AppNotification(
        id = buildString {
            append(MISSED_CHECKOUT_NOTIFICATION_PREFIX)
            append(date)
            retryGeneration?.let { append(":retry:"); append(it) }
        },
        kind = NotificationKind.Attendance,
        title = "Bạn chưa chấm giờ ra",
        body = "Ngày $displayDate chưa có giờ ra. Bấm để tạo đơn báo quên chấm công.",
        createdAt = createdAt,
        target = "Requests",
        entityId = "$MISSED_CHECKOUT_ENTITY_PREFIX$date",
    )
}

/**
 * Tạo đúng một nhắc nhở cho ngày hôm trước nếu đã chấm giờ vào nhưng còn thiếu giờ ra.
 * Dùng dữ liệu có cấu trúc thay vì chuỗi trạng thái tiếng Việt để vẫn đúng khi server đổi nhãn.
 */
internal fun missedCheckoutNotification(
    sheet: Timesheet,
    today: LocalDate = LocalDate.now(),
    createdAt: Long = System.currentTimeMillis(),
): AppNotification? {
    val missedDate = today.minusDays(1)
    val day = sheet.days.firstOrNull { it.date.take(10) == missedDate.toString() } ?: return null
    if (day.checkIn.isNullOrBlank() || !day.checkOut.isNullOrBlank()) return null
    return missedCheckoutNotificationForDate(missedDate, createdAt)
}

/** Ngày cần điền vào đơn báo quên chấm công, lấy từ entity của thông báo. */
internal fun missedCheckoutDateFromEntityId(value: String?): String? {
    val raw = value?.takeIf { it.startsWith(MISSED_CHECKOUT_ENTITY_PREFIX) }
        ?.removePrefix(MISSED_CHECKOUT_ENTITY_PREFIX)
        ?: return null
    return runCatching { LocalDate.parse(raw).toString() }.getOrNull()
}

private fun missedCheckoutEntityIdFromNotificationId(value: String): String? {
    val raw = value.takeIf { it.startsWith(MISSED_CHECKOUT_NOTIFICATION_PREFIX) }
        ?.removePrefix(MISSED_CHECKOUT_NOTIFICATION_PREFIX)
        ?: return null
    if (raw.length > 10 && raw.getOrNull(10) != ':') return null
    val date = runCatching { LocalDate.parse(raw.take(10)).toString() }.getOrNull() ?: return null
    return "$MISSED_CHECKOUT_ENTITY_PREFIX$date"
}

internal fun requestIdFromNotificationId(value: String): String? {
    val parts = value.split(':')
    if (parts.size < 2 || parts[0] !in setOf("req", "inbox")) return null
    return parts[1].takeIf { it.isNotBlank() }
}

internal fun entityIdFromNotificationId(value: String): String? {
    if (value.startsWith("chat:")) return value.split(':').getOrNull(1)?.takeIf { it.isNotBlank() }
    missedCheckoutEntityIdFromNotificationId(value)?.let { return it }
    return requestIdFromNotificationId(value)
}

internal fun appUpdateVersionCode(notification: AppNotification): Int? {
    if (notification.target != APP_UPDATE_NOTIFICATION_TARGET) return null
    if (!notification.id.startsWith("release:")) return null
    return notification.id.substringAfter("release:").toIntOrNull()?.takeIf { it > 0 }
}

internal fun isObsoleteAppUpdateNotification(
    notification: AppNotification,
    installedVersionCode: Int,
    noUpdateAvailable: Boolean = false,
): Boolean {
    if (notification.target != APP_UPDATE_NOTIFICATION_TARGET) return false
    val version = appUpdateVersionCode(notification)
    // Notification có version mới hơn app không được một response "không có update" cũ (bắt đầu
    // trước lúc publish) đánh dấu đã đọc. Chỉ dùng cờ blanket cho payload legacy không mang version.
    return version?.let { it <= installedVersionCode } ?: noUpdateAvailable
}
