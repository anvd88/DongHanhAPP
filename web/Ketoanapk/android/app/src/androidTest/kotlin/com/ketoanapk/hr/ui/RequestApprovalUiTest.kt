package com.ketoanapk.hr.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextInput
import com.ketoanapk.hr.data.RequestDetail
import com.ketoanapk.hr.data.RequestHead
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test

class RequestApprovalUiTest {
    @get:Rule
    val compose = createComposeRule()

    private fun pendingState() = RequestDetailUiState(
        id = "request-1",
        detail = RequestDetail(
            request = RequestHead(
                id = "request-1",
                requestNo = "DX-001",
                title = "Xin nghỉ phép",
                employeeName = "Nguyễn Văn An",
                status = "Pending",
            ),
        ),
        canDecide = true,
    )

    @Test
    fun approveRequiresConfirmationAndReturnsRequestId() {
        var result: Triple<String, Boolean, String>? = null
        compose.setContent {
            MaterialTheme {
                RequestDetailView(pendingState(), {}, {}, onDecide = { id, approve, comment ->
                    result = Triple(id, approve, comment)
                })
            }
        }

        compose.onNodeWithText("Duyệt").performScrollTo().performClick()
        compose.onNodeWithText("Xác nhận duyệt đơn").assertIsDisplayed()
        compose.onNodeWithTag("confirm_approve").performClick()

        assertEquals(Triple("request-1", true, ""), result)
    }

    @Test
    fun rejectSendsRequiredCommentAfterConfirmation() {
        var result: Triple<String, Boolean, String>? = null
        compose.setContent {
            MaterialTheme {
                RequestDetailView(pendingState(), {}, {}, onDecide = { id, approve, comment ->
                    result = Triple(id, approve, comment)
                })
            }
        }

        compose.onNodeWithText("Từ chối").performScrollTo().performClick()
        compose.onNodeWithText("Xác nhận từ chối").assertIsDisplayed()
        compose.onNodeWithText("Nhận xét").performTextInput("Không đủ thông tin")
        compose.onNodeWithTag("confirm_reject").performClick()

        assertEquals(Triple("request-1", false, "Không đủ thông tin"), result)
    }
}
