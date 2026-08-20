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

// Bảng màu thương hiệu xanh da trời. Màu chính dùng sky-700 để chữ/nút trắng vẫn đủ tương phản.
val BrandSky = Color(0xFF0369A1)
val BrandSkyDark = Color(0xFF075985)
val BrandSkyDeep = Color(0xFF0C4A6E)
val BrandSkyLight = Color(0xFF7DD3FC)
val BrandGradientTop = Color(0xFF0EA5E9)
val BrandGradientBottom = Color(0xFF0369A1)

// Alias tương thích cho các màn cũ đang dùng màu thương hiệu trực tiếp. Danger/Warning bên dưới
// vẫn là màu ngữ nghĩa riêng và không bị đổi sang xanh.
val BrandRed = BrandSky
val BrandRedDark = BrandSkyDark
val BrandRedDeep = BrandSkyDeep
val BrandRedLight = BrandSkyLight

val Success = Color(0xFF15803D)
val Warning = Color(0xFFB7791F)
val Danger = Color(0xFFB42318)
val InfoBlue = BrandSky

// Gradient xanh sâu dùng chung cho các thẻ "hero" (Trang chủ, Hồ sơ).
val HeroTop = Color(0xFF075985)
val HeroBottom = Color(0xFF0C4A6E)

private val LightColors = lightColorScheme(
    primary = BrandSky,
    onPrimary = Color.White,
    primaryContainer = Color(0xFFE0F2FE),
    onPrimaryContainer = BrandSkyDeep,
    secondary = BrandSky,
    onSecondary = Color.White,
    secondaryContainer = Color(0xFFBAE6FD),
    onSecondaryContainer = BrandSkyDeep,
    background = Color(0xFFF0F9FF),
    onBackground = Color(0xFF0F2940),
    surface = Color.White,
    onSurface = Color(0xFF0F2940),
    surfaceVariant = Color(0xFFEAF6FC),
    onSurfaceVariant = Color(0xFF4A6575),
    outline = Color(0xFF7893A3),
    outlineVariant = Color(0xFFBAE6FD),
    error = Danger,
    onError = Color.White,
)

private val DarkColors = darkColorScheme(
    primary = BrandSkyLight,
    onPrimary = Color(0xFF082F49),
    primaryContainer = BrandSkyDark,
    onPrimaryContainer = Color(0xFFE0F2FE),
    secondary = Color(0xFF38BDF8),
    onSecondary = Color(0xFF082F49),
    secondaryContainer = BrandSkyDeep,
    onSecondaryContainer = Color(0xFFE0F2FE),
    background = Color(0xFF061923),
    onBackground = Color(0xFFE6F6FD),
    surface = Color(0xFF0B2533),
    onSurface = Color(0xFFE6F6FD),
    surfaceVariant = Color(0xFF12394B),
    onSurfaceVariant = Color(0xFFB5D4E2),
    outline = Color(0xFF4D778A),
    outlineVariant = Color(0xFF2C586B),
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
    fontScale: Float = 1f,
    content: @Composable () -> Unit,
) {
    MaterialTheme(
        colorScheme = if (darkTheme) DarkColors else LightColors,
        typography = AppTypography.copy(
            bodySmall=AppTypography.bodySmall.copy(fontSize=AppTypography.bodySmall.fontSize*fontScale),
            bodyMedium=AppTypography.bodyMedium.copy(fontSize=AppTypography.bodyMedium.fontSize*fontScale),
            bodyLarge=AppTypography.bodyLarge.copy(fontSize=AppTypography.bodyLarge.fontSize*fontScale),
            titleSmall=AppTypography.titleSmall.copy(fontSize=AppTypography.titleSmall.fontSize*fontScale),
            titleMedium=AppTypography.titleMedium.copy(fontSize=AppTypography.titleMedium.fontSize*fontScale),
            titleLarge=AppTypography.titleLarge.copy(fontSize=AppTypography.titleLarge.fontSize*fontScale),
        ),
        content = content,
    )
}
