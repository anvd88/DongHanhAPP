package com.ketoanapk.hr.data

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.first
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/** Nhóm thông báo, dùng để chọn icon/màu và điểm đến khi bấm vào. */
@Serializable
enum class NotificationKind { Request, Approval, Penalty, Attendance, Chat, System }

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
)

@Serializable
private data class NotifSnapshot(
    val items: List<AppNotification> = emptyList(),
    val seen: List<String> = emptyList(),
)

private val Context.notifyStore: DataStore<Preferences> by preferencesDataStore(name = "ketoanapk_notifications")

/** Kho lưu thông báo + tập "chữ ký đã thấy" (để không báo trùng) trên thiết bị. */
class NotificationStore(context: Context) {
    private val appContext = context.applicationContext
    private val keyData = stringPreferencesKey("data")
    private val json = Json { ignoreUnknownKeys = true }

    suspend fun load(): Pair<List<AppNotification>, MutableSet<String>> {
        val raw = appContext.notifyStore.data.first()[keyData] ?: return emptyList<AppNotification>() to mutableSetOf()
        val snap = runCatching { json.decodeFromString<NotifSnapshot>(raw) }.getOrDefault(NotifSnapshot())
        return snap.items to snap.seen.toMutableSet()
    }

    suspend fun save(items: List<AppNotification>, seen: Set<String>) {
        val trimmedItems = items.sortedByDescending { it.createdAt }.take(120)
        val trimmedSeen = seen.toList().takeLast(400)
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
class NotificationCenter(context: Context) {
    private val store = NotificationStore(context)
    private var items: List<AppNotification> = emptyList()
    private var seen: MutableSet<String> = mutableSetOf()
    private var loaded = false

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

    suspend fun load(): List<AppNotification> {
        reload()
        return items
    }

    val current: List<AppNotification> get() = items
    fun unreadCount(): Int = items.count { !it.read }

    suspend fun markRead(id: String): List<AppNotification> {
        reload()
        items = items.map { if (it.id == id) it.copy(read = true) else it }
        store.save(items, seen)
        return items
    }

    suspend fun markAllRead(): List<AppNotification> {
        reload()
        items = items.map { if (it.read) it else it.copy(read = true) }
        store.save(items, seen)
        return items
    }

    suspend fun clearAll(): List<AppNotification> {
        reload()
        items = emptyList()
        store.save(items, seen)
        return items
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
        reload()
        if (notifId.isNotBlank() && seen.contains(notifId)) return null
        val id = if (notifId.isBlank()) "fcm:${System.currentTimeMillis()}" else notifId
        if (notifId.isNotBlank()) seen.add(notifId)
        val n = AppNotification(
            id = id,
            kind = kind,
            title = title,
            body = body,
            createdAt = System.currentTimeMillis(),
            target = target,
            entityId = entityIdFromNotificationId(notifId),
        )
        items = (listOf(n) + items).distinctBy { it.id }
        store.save(items, seen)
        return n
    }

    /** Xoá toàn bộ dữ liệu thông báo khi đăng xuất. */
    suspend fun reset() {
        items = emptyList()
        seen = mutableSetOf()
        loaded = false
        store.clear()
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
    ): List<AppNotification> {
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

        if (fresh.isNotEmpty() || firstRun) {
            items = (fresh + items).distinctBy { it.id }
            store.save(items, seen)
        }
        return if (firstRun) emptyList() else fresh
    }
}

internal fun requestIdFromNotificationId(value: String): String? {
    val parts = value.split(':')
    if (parts.size < 2 || parts[0] !in setOf("req", "inbox")) return null
    return parts[1].takeIf { it.isNotBlank() }
}

internal fun entityIdFromNotificationId(value: String): String? {
    if (value.startsWith("chat:")) return value.split(':').getOrNull(1)?.takeIf { it.isNotBlank() }
    return requestIdFromNotificationId(value)
}
