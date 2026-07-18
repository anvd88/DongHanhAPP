package com.ketoanapk.hr.data

import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ChatVoiceMessageTest {
    @Test
    fun recognizesBackendVoiceWithoutDependingOnFileMetadata() {
        assertTrue(ChatMessage(kind = "voice").isVoiceMessage())
    }

    @Test
    fun keepsCompatibilityWithLegacyRecordedFile() {
        assertTrue(ChatMessage(kind = "file", fileName = "ghi-am-123.ogg", fileMime = "audio/ogg").isVoiceMessage())
        assertTrue(ChatMessage(kind = "file", fileName = "ghi-am-123.m4a").isVoiceMessage())
    }

    @Test
    fun doesNotTreatRemovedOrTextMessagesAsVoice() {
        assertFalse(ChatMessage(kind = "voice", removed = true).isVoiceMessage())
        assertFalse(ChatMessage(kind = "text", fileMime = "audio/ogg").isVoiceMessage())
    }

    @Test
    fun recorderRequestSerializesExplicitVoiceKind() {
        val json = Json { encodeDefaults = true }

        assertTrue(json.encodeToString(SendChatFileBody("ghi-am-123.ogg", 10, "audio/ogg", "voice")).contains("\"kind\":\"voice\""))
        assertTrue(json.encodeToString(SendChatFileBody("song.mp3", 10, "audio/mpeg")).contains("\"kind\":\"file\""))
    }

    @Test
    fun voiceRetryKeyIsStableButDifferentForAnotherRecording() {
        val key = voiceClientMessageId("conversation", "ghi-am-1.ogg", 10)
        assertEquals(78, key.length)
        assertTrue(
            key ==
                voiceClientMessageId("conversation", "ghi-am-1.ogg", 10),
        )
        assertFalse(
            voiceClientMessageId("conversation", "ghi-am-1.ogg", 10) ==
                voiceClientMessageId("conversation", "ghi-am-2.ogg", 10),
        )
    }
}
