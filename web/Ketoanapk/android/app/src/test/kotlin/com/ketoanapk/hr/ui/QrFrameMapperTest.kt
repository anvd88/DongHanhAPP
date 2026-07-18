package com.ketoanapk.hr.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class QrFrameMapperTest {
    private fun corners(vararg xy: Float) =
        (xy.indices step 2).map { TrackPoint(xy[it], xy[it + 1]) }

    @Test
    fun cropIsRotatedIntoTheSameSpaceAsMlKitCoordinates() {
        // Ảnh 640x480 chưa xoay; cắt một ô ở góc trên-trái.
        val crop = QrCropRect(left = 10, top = 20, right = 110, bottom = 220)

        assertEquals(crop, QrFrameMapper.rotateCrop(crop, 640, 480, 0))

        // Xoay 90° theo chiều kim đồng hồ: ảnh thành 480x640, x mới = 480 - y cũ.
        assertEquals(
            QrCropRect(left = 480 - 220, top = 10, right = 480 - 20, bottom = 110),
            QrFrameMapper.rotateCrop(crop, 640, 480, 90),
        )
        assertEquals(
            QrCropRect(left = 640 - 110, top = 480 - 220, right = 640 - 10, bottom = 480 - 20),
            QrFrameMapper.rotateCrop(crop, 640, 480, 180),
        )
        assertEquals(
            QrCropRect(left = 20, top = 640 - 110, right = 220, bottom = 640 - 10),
            QrFrameMapper.rotateCrop(crop, 640, 480, 270),
        )
        // 360 và số âm phải quy về cùng một góc, không rơi vào nhánh "else" một cách tình cờ.
        assertEquals(
            QrFrameMapper.rotateCrop(crop, 640, 480, 90),
            QrFrameMapper.rotateCrop(crop, 640, 480, -270),
        )
        assertEquals(crop, QrFrameMapper.rotateCrop(crop, 640, 480, 360))
    }

    @Test
    fun cornersMapOntoTheViewRelativeToTheCropRegion() {
        // Vùng hiển thị là ô 100x200 nằm lệch trong ảnh; view 300x600 → phóng đúng 3 lần mỗi chiều.
        val crop = QrCropRect(left = 50, top = 100, right = 150, bottom = 300)
        val quad = QrFrameMapper.map(
            corners = corners(50f, 100f, 150f, 100f, 150f, 300f, 50f, 300f),
            crop = crop,
            viewWidth = 300f,
            viewHeight = 600f,
        )!!

        // Góc mã trùng biên vùng cắt → phải trùng đúng 4 góc màn hình, không lệch tâm.
        assertEquals(0f, quad.topLeft.x, 0.001f)
        assertEquals(0f, quad.topLeft.y, 0.001f)
        assertEquals(300f, quad.topRight.x, 0.001f)
        assertEquals(0f, quad.topRight.y, 0.001f)
        assertEquals(300f, quad.bottomRight.x, 0.001f)
        assertEquals(600f, quad.bottomRight.y, 0.001f)
        assertEquals(0f, quad.bottomLeft.x, 0.001f)
        assertEquals(600f, quad.bottomLeft.y, 0.001f)
    }

    @Test
    fun cropOffsetIsSubtractedInsteadOfAssumingCenteredFullFrame() {
        // Đây là hồi quy cho lỗi cũ: bản trước bỏ qua cropRect và giả định Preview với ImageAnalysis
        // cùng tỉ lệ, nên mã nằm giữa vùng cắt bị vẽ lệch hẳn sang chỗ khác.
        val crop = QrCropRect(left = 100, top = 0, right = 200, bottom = 100)
        val quad = QrFrameMapper.map(
            corners = corners(150f, 50f, 150f, 50f, 150f, 50f, 150f, 50f),
            crop = crop,
            viewWidth = 100f,
            viewHeight = 100f,
        )!!
        assertEquals(50f, quad.topLeft.x, 0.001f)
        assertEquals(50f, quad.topLeft.y, 0.001f)
    }

    @Test
    fun onlyCodesInsideTheVisibleRegionAreAccepted() {
        // ViewPort chỉ hiển thị ô giữa; phần ảnh ngoài ô đó người dùng không nhìn thấy.
        val crop = QrCropRect(left = 100, top = 100, right = 200, bottom = 200)

        // Mã nằm gọn trong vùng nhìn.
        assertTrue(QrFrameMapper.isVisible(corners(140f, 140f, 160f, 140f, 160f, 160f, 140f, 160f), crop))
        // Mã nằm ngoài vùng nhìn (bên trái) → không được quét trúng.
        assertFalse(QrFrameMapper.isVisible(corners(10f, 140f, 30f, 140f, 30f, 160f, 10f, 160f), crop))
        // Ngoài theo chiều dọc cũng vậy — màn hình dài cắt rất nhiều theo chiều này.
        assertFalse(QrFrameMapper.isVisible(corners(140f, 10f, 160f, 10f, 160f, 30f, 140f, 30f), crop))
        // Mã nằm vắt qua mép nhưng TÂM vẫn trong vùng nhìn thì vẫn nhận (người dùng đang chĩa vào nó).
        assertTrue(QrFrameMapper.isVisible(corners(80f, 140f, 180f, 140f, 180f, 160f, 80f, 160f), crop))
        // Thiếu góc không đủ chứng minh vị trí. Caller phải dùng tâm bounding-box làm fallback.
        assertFalse(QrFrameMapper.isVisible(corners(0f, 0f, 1f, 0f), crop))
        assertTrue(QrFrameMapper.isPointVisible(TrackPoint(150f, 150f), crop))
        assertFalse(QrFrameMapper.isPointVisible(TrackPoint(50f, 150f), crop))
        assertFalse(QrFrameMapper.isPointVisible(TrackPoint(150f, 150f), QrCropRect(0, 0, 0, 0)))
    }

    @Test
    fun gridPointsFollowTheTiltOfTheQuad() {
        // Tứ giác VUÔNG: điểm giữa phải nằm đúng tâm, điểm 1/4 nằm đúng 1/4.
        val square = TrackQuad(
            TrackPoint(0f, 0f), TrackPoint(100f, 0f), TrackPoint(100f, 100f), TrackPoint(0f, 100f),
        )
        assertEquals(50f, square.pointAt(0.5f, 0.5f).x, 0.001f)
        assertEquals(50f, square.pointAt(0.5f, 0.5f).y, 0.001f)
        assertEquals(25f, square.pointAt(0.25f, 0.75f).x, 0.001f)
        assertEquals(75f, square.pointAt(0.25f, 0.75f).y, 0.001f)
        // Bốn góc phải trùng đúng bốn đỉnh.
        assertEquals(square.topRight.x, square.pointAt(1f, 0f).x, 0.001f)
        assertEquals(square.bottomLeft.y, square.pointAt(0f, 1f).y, 0.001f)

        // Tứ giác MÉO (mã nhìn chếch): cạnh phải ngắn hơn cạnh trái. Đường lưới dọc ở giữa phải nằm
        // đúng giữa hai cạnh trên/dưới — đó là thứ khiến lưới nghiêng theo mã thay vì dán vuông đè lên.
        val skewed = TrackQuad(
            TrackPoint(0f, 0f), TrackPoint(100f, 20f), TrackPoint(100f, 80f), TrackPoint(0f, 120f),
        )
        val midTop = skewed.pointAt(0.5f, 0f)
        assertEquals(50f, midTop.x, 0.001f)
        assertEquals(10f, midTop.y, 0.001f)
        // Tâm: cạnh trái chạy y 0→120 (trung điểm 60), cạnh phải y 20→80 (trung điểm 50) ⇒ 55.
        val center = skewed.pointAt(0.5f, 0.5f)
        assertEquals(50f, center.x, 0.001f)
        assertEquals(55f, center.y, 0.001f)
    }

    @Test
    fun gridSlidesFromItsOldPositionToTheCode() {
        val from = TrackQuad(
            TrackPoint(0f, 0f), TrackPoint(100f, 0f), TrackPoint(100f, 100f), TrackPoint(0f, 100f),
        )
        val to = TrackQuad(
            TrackPoint(200f, 200f), TrackPoint(300f, 200f), TrackPoint(300f, 300f), TrackPoint(200f, 300f),
        )

        assertEquals(from, lerpQuad(from, to, 0f))
        assertEquals(to, lerpQuad(from, to, 1f))
        assertEquals(100f, lerpQuad(from, to, 0.5f).topLeft.x, 0.001f)
        assertEquals(100f, lerpQuad(from, to, 0.5f).topLeft.y, 0.001f)
        // Tiến độ ngoài khoảng phải bị kẹp, không cho lưới bay vọt qua mã rồi quay lại.
        assertEquals(from, lerpQuad(from, to, -3f))
        assertEquals(to, lerpQuad(from, to, 9f))
    }

    @Test
    fun degenerateInputYieldsNoOverlayInsteadOfCrashing() {
        val crop = QrCropRect(0, 0, 100, 100)
        assertNull(QrFrameMapper.map(corners(0f, 0f, 1f, 0f, 1f, 1f), crop, 100f, 100f))
        assertNull(QrFrameMapper.map(corners(0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f), QrCropRect(0, 0, 0, 0), 100f, 100f))
        assertNull(QrFrameMapper.map(corners(0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f), crop, 0f, 100f))
        assertNull(QrFrameMapper.map(corners(0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f), crop, 100f, 0f))
    }
}
