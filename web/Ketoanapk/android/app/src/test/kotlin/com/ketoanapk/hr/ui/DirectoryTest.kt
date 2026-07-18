package com.ketoanapk.hr.ui

import com.ketoanapk.hr.data.CallContact
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class DirectoryTest {
    @Test
    fun organizationSeparatesDirectManagerAndDepartmentPeers() {
        val manager = CallContact(username = "manager", isDirectManager = true, sameDepartment = true)
        val peer = CallContact(username = "peer", sameDepartment = true)
        val outsider = CallContact(username = "other")

        val (managers, peers) = organizationContacts(listOf(manager, peer, outsider))

        assertEquals(listOf(manager), managers)
        assertEquals(listOf(peer), peers)
    }

    @Test
    fun hiddenContactFieldsRemainEmptyInClientModel() {
        val hidden = CallContact(username = "private")
        assertTrue(hidden.phone.isEmpty())
        assertTrue(hidden.email.isEmpty())
    }
}
