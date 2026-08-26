package com.ketoanapk.hr.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.ketoanapk.hr.data.HrUser

/**
 * Màn "Cá nhân": mọi thứ thuộc về chính người dùng, mở bằng ảnh đại diện trên header.
 *
 * Đây là nơi ở mới của nhóm mục cá nhân sau khi bỏ ngăn kéo hamburger — thay vì 22 mục nằm phẳng trong
 * một ngăn kéo, mỗi màn con giờ nằm trong đúng màn cha của nó.
 */
@Composable
fun PersonalHubScreen(user: HrUser, state: HomeUiState, onSelect: (HrDestination) -> Unit) {
    val name = state.employee?.fullName?.ifBlank { user.displayName } ?: user.displayName
    val position = buildString {
        append(state.employee?.position?.ifBlank { "Nhân viên" } ?: "Nhân viên")
        val dept = state.employee?.departmentName
        if (!dept.isNullOrBlank()) append(" · $dept")
    }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = screenPadding(16.dp, 16.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        item {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(14.dp),
            ) {
                UserAvatar(name, 58, avatar = state.employee?.avatar)
                Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    Text(
                        name,
                        style = MaterialTheme.typography.headlineSmall,
                        color = MaterialTheme.colorScheme.onSurface,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Text(
                        position,
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
        }

        item {
            HrCard {
                CardHeader("Của tôi")
                HubList(
                    destinations = buildList {
                        add(HrDestination.Profile)
                        add(HrDestination.MyPayslips)
                        add(HrDestination.Payout)
                        if (user.can(com.ketoanapk.hr.data.AppPermissions.CollectionsSelf))
                            add(HrDestination.CashCollections)
                        add(HrDestination.Benefits)
                    },
                    onSelect = onSelect,
                )
            }
        }

        item {
            HrCard {
                CardHeader("Phát triển")
                HubList(
                    destinations = listOf(
                        HrDestination.Onboarding,
                        HrDestination.Performance,
                        HrDestination.Training,
                    ),
                    onSelect = onSelect,
                )
            }
        }

        item {
            HrCard {
                CardHeader("Ứng dụng")
                HubList(
                    destinations = listOf(HrDestination.Help, HrDestination.Settings),
                    onSelect = onSelect,
                )
            }
        }
    }
}
