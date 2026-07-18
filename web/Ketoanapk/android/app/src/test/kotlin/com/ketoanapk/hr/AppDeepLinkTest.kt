package com.ketoanapk.hr

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class AppDeepLinkTest {
    @Test
    fun parsesQrLoginCode() {
        assertEquals(
            "ketoanmini:login:abc_123",
            AppDeepLink.qrLoginCode("ketoanhr://qr-login?code=ketoanmini%3Alogin%3Aabc_123"),
        )
    }

    @Test
    fun parsesDedicatedMobileAppLoginRequest() {
        assertEquals(
            "ketoanmini-app-login:abc_123",
            AppDeepLink.mobileAppLoginRequest(
                "ketoanhr://app-login?request=ketoanmini-app-login%3Aabc_123&client_mode=mobile_app",
            ),
        )
        assertNull(AppDeepLink.mobileAppLoginRequest(
            "ketoanhr://app-login?request=abc&client_mode=desktop_qr",
        ))
        assertEquals(
            "ketoanmini-app-login:from_extra",
            AppDeepLink.mobileAppLoginRequest(
                "ketoanhr://app-login",
                "ketoanmini-app-login:from_extra",
                "mobile_app",
            ),
        )
        assertNull(AppDeepLink.mobileAppLoginRequest(
            "ketoanhr://app-login",
            "ketoanmini-app-login:from_extra",
            "desktop_qr",
        ))
    }

    @Test
    fun rejectsUnknownOrMalformedLinks() {
        assertNull(AppDeepLink.qrLoginCode("https://app.ketoancp.click/login?code=abc"))
        assertNull(AppDeepLink.qrLoginCode("ketoanhr://other?code=abc"))
        assertNull(AppDeepLink.qrLoginCode("ketoanhr://qr-login?code="))
        assertNull(AppDeepLink.qrLoginCode(null))
    }
}
