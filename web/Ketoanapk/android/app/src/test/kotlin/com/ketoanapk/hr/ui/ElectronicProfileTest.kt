package com.ketoanapk.hr.ui

import org.junit.Assert.assertEquals
import org.junit.Test

class ElectronicProfileTest {
    @Test fun sensitiveDocumentNumberIsMasked() {
        assertEquals("••••••••9012", maskDocumentNumber("123456789012"))
    }
}
