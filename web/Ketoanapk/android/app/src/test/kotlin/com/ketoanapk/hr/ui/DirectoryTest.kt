package com.ketoanapk.hr.ui

import com.ketoanapk.hr.data.DirectoryContact
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class DirectoryTest {
    @Test
    fun organizationSeparatesDirectManagerAndDepartmentPeers() {
        val manager = DirectoryContact(username = "manager", isDirectManager = true, sameDepartment = true)
        val peer = DirectoryContact(username = "peer", sameDepartment = true)
        val outsider = DirectoryContact(username = "other")

        val (managers, peers) = organizationContacts(listOf(manager, peer, outsider))

        assertEquals(listOf(manager), managers)
        assertEquals(listOf(peer), peers)
    }

    @Test
    fun hiddenContactFieldsRemainEmptyInClientModel() {
        val hidden = DirectoryContact(username = "private")
        assertTrue(hidden.phone.isEmpty())
        assertTrue(hidden.email.isEmpty())
    }
}
