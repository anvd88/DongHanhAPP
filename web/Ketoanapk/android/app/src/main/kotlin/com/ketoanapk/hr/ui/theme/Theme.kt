package com.ketoanapk.hr.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

// Bảng màu thương hiệu lấy cảm hứng từ VNeID: đỏ trầm, chuyên nghiệp, dễ nhìn (không chói).
val BrandRed = Color(0xFFC62828)
val BrandRedDark = Color(0xFF8E1B1B)
val BrandRedDeep = Color(0xFF6E1212)
val BrandRedLight = Color(0xFFE05B57)
// Dải gradient đỏ dùng cho header/đăng nhập (giống nền đỏ chuyển sắc của VNeID).
val BrandGradientTop = Color(0xFFD1362F)
val BrandGradientBottom = Color(0xFF9E1B1B)

val Success = Color(0xFF15803D)
val Warning = Color(0xFFB7791F)
val Danger = Color(0xFFB42318)

private val LightColors = lightColorScheme(
    primary = BrandRed,
    onPrimary = Color.White,
    primaryContainer = Color(0xFFFBE3E3),
    onPrimaryContainer = BrandRedDeep,
    secondary = BrandRedDark,
    onSecondary = Color.White,
    secondaryContainer = Color(0xFFF7D9D9),
    onSecondaryContainer = BrandRedDeep,
    background = Color(0xFFF5F6F8),
    onBackground = Color(0xFF1B1D21),
    surface = Color.White,
    onSurface = Color(0xFF1B1D21),
    surfaceVariant = Color(0xFFF0F1F4),
    onSurfaceVariant = Color(0xFF6B7280),
    outline = Color(0xFFE1E4EA),
    error = Danger,
    onError = Color.White,
)

private val DarkColors = darkColorScheme(
    primary = BrandRedLight,
    onPrimary = Color(0xFF3A0808),
    primaryContainer = Color(0xFF3A1414),
    onPrimaryContainer = Color(0xFFF6C6C4),
    secondary = BrandRedLight,
    onSecondary = Color(0xFF3A0808),
    secondaryContainer = Color(0xFF3A1414),
    onSecondaryContainer = Color(0xFFF6C6C4),
    background = Color(0xFF121316),
    onBackground = Color(0xFFF2F4F7),
    surface = Color(0xFF1B1D21),
    onSurface = Color(0xFFF2F4F7),
    surfaceVariant = Color(0xFF262A30),
    onSurfaceVariant = Color(0xFFA6AFBD),
    outline = Color(0xFF32373F),
    error = Color(0xFFE57373),
    onError = Color(0xFF2A0A08),
)

private val AppTypography = Typography(
    headlineSmall = Typography().headlineSmall.copy(fontWeight = FontWeight.ExtraBold),
    titleLarge = Typography().titleLarge.copy(fontWeight = FontWeight.ExtraBold),
    titleMedium = Typography().titleMedium.copy(fontWeight = FontWeight.Bold),
    labelSmall = Typography().labelSmall.copy(fontWeight = FontWeight.ExtraBold, letterSpacing = 0.5.sp),
)

@Composable
fun KetoanTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    MaterialTheme(
        colorScheme = if (darkTheme) DarkColors else LightColors,
        typography = AppTypography,
        content = content,
    )
}
