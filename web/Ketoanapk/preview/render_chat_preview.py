from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent
W, H = 393, 852

ACCENT = "#C62828"
BACKGROUND = "#F5F6F8"
THREAD_BG = "#FAFAFA"
SURFACE = "#FFFFFF"
SURFACE_ALT = "#F1F2F4"
FILE_BUBBLE = "#E7EEF7"
OUTLINE = "#E5E7EB"
TEXT_PRIMARY = "#111827"
TEXT_SECONDARY = "#4B5563"
TEXT_MUTED = "#9CA3AF"
ONLINE = "#22C55E"


def font(name, size):
    fonts_dir = Path("C:/Windows/Fonts")
    candidates = {
        "regular": ["segoeui.ttf", "arial.ttf"],
        "bold": ["segoeuib.ttf", "arialbd.ttf"],
        "semibold": ["seguisb.ttf", "segoeuib.ttf", "arialbd.ttf"],
        "emoji": ["seguiemj.ttf", "segoeui.ttf"],
    }[name]
    for candidate in candidates:
        path = fonts_dir / candidate
        if path.exists():
            return ImageFont.truetype(str(path), size=size)
    return ImageFont.load_default()


F = {
    "h1": font("bold", 24),
    "title": font("bold", 22),
    "body": font("regular", 16),
    "body_bold": font("bold", 16),
    "medium": font("regular", 14),
    "medium_bold": font("semibold", 14),
    "small": font("regular", 12),
    "small_bold": font("bold", 12),
    "tiny": font("bold", 11),
    "icon": font("regular", 24),
    "icon_small": font("regular", 19),
    "emoji": font("emoji", 16),
}


def text_width(draw, value, face):
    if not value:
        return 0
    box = draw.textbbox((0, 0), value, font=face)
    return box[2] - box[0]


def draw_text(draw, xy, value, face, fill, max_width=None, anchor=None):
    value = str(value)
    if max_width is not None and text_width(draw, value, face) > max_width:
        ellipsis = "..."
        while value and text_width(draw, value + ellipsis, face) > max_width:
            value = value[:-1]
        value = value + ellipsis if value else ellipsis
    draw.text(xy, value, font=face, fill=fill, anchor=anchor)


def center_text(draw, box, value, face, fill):
    x1, y1, x2, y2 = box
    bbox = draw.textbbox((0, 0), value, font=face)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    draw.text((x1 + (x2 - x1 - tw) / 2, y1 + (y2 - y1 - th) / 2 - 1), value, font=face, fill=fill)


def icon(draw, x, y, value, fill=TEXT_PRIMARY, size=48, face=None):
    face = face or F["icon"]
    center_text(draw, (x, y, x + size, y + size), value, face, fill)


def rounded(draw, box, radius, fill, outline=None, width=1):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def avatar(base, x, y, size, initials, online=False):
    gradient = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    pix = gradient.load()
    c1 = (203, 213, 225)
    c2 = (254, 226, 226)
    for yy in range(size):
        for xx in range(size):
            t = (xx + yy) / max(1, (size - 1) * 2)
            r = round(c1[0] * (1 - t) + c2[0] * t)
            g = round(c1[1] * (1 - t) + c2[1] * t)
            b = round(c1[2] * (1 - t) + c2[2] * t)
            pix[xx, yy] = (r, g, b, 255)

    mask = Image.new("L", (size, size), 0)
    mdraw = ImageDraw.Draw(mask)
    mdraw.ellipse((0, 0, size - 1, size - 1), fill=255)
    base.paste(gradient, (x, y), mask)

    d = ImageDraw.Draw(base)
    face = font("bold", 24 if size >= 70 else 18)
    center_text(d, (x, y, x + size, y + size), initials, face, "#7F1D1D")
    if online:
        dot = max(10, round(size * 0.25))
        dx = x + size - dot
        dy = y + size - dot
        d.ellipse((dx - 2, dy - 2, dx + dot + 2, dy + dot + 2), fill=SURFACE)
        d.ellipse((dx, dy, dx + dot, dy + dot), fill=ONLINE)


def draw_chip(draw, x, y, label, active=False):
    tw = text_width(draw, label, F["medium_bold"])
    w = tw + 28
    fill = ACCENT if active else SURFACE
    stroke = ACCENT if active else OUTLINE
    color = SURFACE if active else TEXT_SECONDARY
    rounded(draw, (x, y, x + w, y + 33), 16, fill, stroke)
    draw_text(draw, (x + 14, y + 8), label, F["medium_bold"], color)
    return w


def draw_bottom_nav(draw, y):
    draw.rectangle((0, y, W, H), fill=SURFACE)
    draw.line((0, y, W, y), fill="#EEF0F3")
    items = [("●", "Trò chuyện", True), ("◉", "Danh bạ", False), ("▤", "Công việc", False), ("•••", "Thêm", False)]
    for i, (glyph, label, active) in enumerate(items):
        cx = 34 + i * 108
        color = ACCENT if active else TEXT_MUTED
        center_text(draw, (cx - 25, y + 8, cx + 25, y + 31), glyph, F["icon_small"], color)
        center_text(draw, (cx - 40, y + 34, cx + 40, y + 56), label, F["tiny"], color)


def conversation(screen, y, initials, name, preview, time, unread=0, online=False, pinned=False, muted=False):
    d = ImageDraw.Draw(screen)
    d.rectangle((0, y, W, y + 75), fill=SURFACE)
    avatar(screen, 16, y + 11, 52, initials, online)
    name_w = W - 16 - 52 - 12 - 14 - 54
    x = 80
    draw_text(d, (x, y + 14), name, F["body_bold"], TEXT_PRIMARY, max_width=name_w)
    tx = W - 14 - text_width(d, time, F["small_bold"])
    draw_text(d, (tx, y + 17), time, F["small_bold"], TEXT_MUTED)
    if pinned:
        draw_text(d, (tx - 20, y + 15), "⌖", F["small_bold"], TEXT_MUTED)
    if muted:
        draw_text(d, (tx - 20, y + 15), "◌", F["small_bold"], TEXT_MUTED)

    preview_w = W - x - 14 - (30 if unread else 0)
    draw_text(d, (x, y + 42), preview, F["medium"], TEXT_PRIMARY if unread else TEXT_SECONDARY, max_width=preview_w)
    if unread:
        badge_w = 24 if unread > 9 else 20
        bx = W - 14 - badge_w
        by = y + 40
        rounded(d, (bx, by, bx + badge_w, by + 20), 10, ACCENT)
        center_text(d, (bx, by, bx + badge_w, by + 20), str(unread), F["tiny"], SURFACE)
    d.line((80, y + 74, W, y + 74), fill="#ECEEF2")


def draw_inbox():
    screen = Image.new("RGB", (W, H), BACKGROUND)
    d = ImageDraw.Draw(screen)
    y0 = 22
    d.rectangle((0, y0, W, y0 + 72), fill=BACKGROUND)
    draw_text(d, (16, y0 + 23), "Trò chuyện", F["h1"], TEXT_PRIMARY)
    icon(d, W - 102, y0 + 12, "⌕")
    icon(d, W - 54, y0 + 12, "+")

    rounded(d, (16, 94, W - 16, 140), 16, SURFACE_ALT)
    icon(d, 22, 93, "⌕", TEXT_SECONDARY, 46, F["icon_small"])
    draw_text(d, (54, 106), "Tìm kiếm", F["body"], TEXT_MUTED)

    x = 16
    for label, active in [("Tất cả", True), ("Cá nhân", False), ("Nhóm", False), ("Chưa đọc", False)]:
        w = draw_chip(d, x, 152, label, active)
        x += w + 8

    y = 195
    conversation(screen, y, "AM", "Nguyễn Anh Minh", "Mai nộp bảng công giúp em nhé", "16:32", 2, True, True)
    y += 75
    conversation(screen, y, "HR", "Phòng Nhân sự", "Bảng công tháng này đã cập nhật", "15:08", 5)
    y += 75
    conversation(screen, y, "KT", "Kế toán", "Có file mới cần kiểm tra", "Hôm qua", muted=True)
    y += 75
    conversation(screen, y, "CC", "Tổ Chấm công", "Nhắc xác nhận ca làm hôm nay", "Thứ 3", 1)
    y += 75
    conversation(screen, y, "TB", "Thông báo công ty", "Lịch nghỉ và sự kiện nội bộ", "12/07")
    draw_bottom_nav(d, 788)
    return screen


def time_pill(draw, x, y, label):
    tw = text_width(draw, label, F["medium_bold"]) + 18
    rounded(draw, (x, y, x + tw, y + 22), 11, "#E5E7EB")
    center_text(draw, (x, y, x + tw, y + 22), label, F["medium_bold"], TEXT_SECONDARY)
    return tw


def draw_thread():
    screen = Image.new("RGB", (W, H), THREAD_BG)
    d = ImageDraw.Draw(screen)
    y0 = 22
    d.rectangle((0, y0, W, y0 + 72), fill=SURFACE)
    icon(d, 4, y0 + 8, "‹")
    draw_text(d, (52, y0 + 12), "Nguyễn Anh Minh", F["title"], TEXT_PRIMARY, max_width=185)
    rounded(d, (52, y0 + 42, 128, y0 + 65), 12, SURFACE_ALT)
    center_text(d, (52, y0 + 42, 128, y0 + 65), "Nhân viên", F["medium_bold"], TEXT_SECONDARY)
    icon(d, W - 148, y0 + 8, "☎", TEXT_PRIMARY)
    icon(d, W - 100, y0 + 8, "▣", TEXT_PRIMARY)
    icon(d, W - 52, y0 + 8, "⋮", TEXT_PRIMARY)

    d.rectangle((0, 94, W, 149), fill=SURFACE_ALT)
    center_text(d, (123, 104, 160, 140), "⊕", F["icon"], TEXT_SECONDARY)
    draw_text(d, (166, 111), "Kết bạn", F["body_bold"], TEXT_PRIMARY)

    # Message area
    x_file, y = 95, 171
    rounded(d, (x_file, y, x_file + 282, y + 82), 18, FILE_BUBBLE)
    d.ellipse((x_file + 14, y + 14, x_file + 68, y + 68), fill="#CED6E2")
    center_text(d, (x_file + 14, y + 14, x_file + 68, y + 68), "▤", F["icon"], TEXT_SECONDARY)
    draw_text(d, (x_file + 80, y + 15), "BANGCONG_T07.xlsx", F["body_bold"], TEXT_PRIMARY, max_width=170)
    draw_text(d, (x_file + 80, y + 38), "XLSX · 248 KB", F["medium"], TEXT_SECONDARY)
    draw_text(d, (x_file + 80, y + 58), "File đã hết hạn", F["medium"], TEXT_MUTED)
    tw = time_pill(d, W - 16 - 48, y + 88, "09:25")

    label = "17:13 07/01/2026"
    lw = text_width(d, label, F["medium_bold"]) + 24
    rounded(d, ((W - lw) / 2, 285, (W + lw) / 2, 308), 12, "#E5E7EB")
    center_text(d, ((W - lw) / 2, 285, (W + lw) / 2, 308), label, F["medium_bold"], TEXT_SECONDARY)

    y = 322
    d.ellipse((16, y + 2, 54, y + 40), fill=SURFACE)
    rounded(d, (64, y, 302, y + 330), 16, SURFACE_ALT, "#D1D5DB")
    center_text(d, (64, y + 130, 302, y + 168), "▧", F["icon"], TEXT_MUTED)
    center_text(d, (64, y + 170, 302, y + 210), "Ảnh đã hết hạn", F["body_bold"], TEXT_MUTED)
    time_pill(d, 64, y + 336, "17:13")

    y = 695
    msg = "Anh gửi em file bảng công nhé."
    mw = text_width(d, msg, F["body"]) + 28
    rounded(d, (16, y, 16 + mw, y + 43), 18, SURFACE)
    draw_text(d, (30, y + 11), msg, F["body"], TEXT_PRIMARY)
    draw_text(d, (24, y + 48), "17:15", F["tiny"], TEXT_MUTED)

    y = 755
    msg = "Em nhận được rồi ạ."
    mw = text_width(d, msg, F["body"]) + 28
    x = W - 16 - mw
    rounded(d, (x, y, x + mw, y + 43), 18, ACCENT)
    draw_text(d, (x + 14, y + 11), msg, F["body"], SURFACE)
    rounded(d, (x + 8, y + 48, x + 52, y + 68), 10, SURFACE)
    center_text(d, (x + 8, y + 48, x + 52, y + 68), "👍 2", F["emoji"], TEXT_PRIMARY)
    draw_text(d, (x + 58, y + 52), "17:16  ✓✓", F["tiny"], TEXT_MUTED)

    d.rectangle((0, 785, W, H), fill=SURFACE)
    d.line((0, 785, W, 785), fill=OUTLINE)
    icon(d, 8, 793, "☺", TEXT_SECONDARY, 42)
    draw_text(d, (58, 805), "Tin nhắn", font("regular", 22), TEXT_MUTED)
    icon(d, W - 134, 793, "•••", TEXT_SECONDARY, 42)
    icon(d, W - 92, 793, "♪", TEXT_SECONDARY, 42)
    icon(d, W - 50, 793, "▧", TEXT_SECONDARY, 42)
    return screen


def profile_action(draw, x, y, glyph, label):
    draw.ellipse((x, y, x + 48, y + 48), fill="#F9EAEA")
    center_text(draw, (x, y, x + 48, y + 48), glyph, F["icon_small"], ACCENT)
    center_text(draw, (x - 18, y + 55, x + 66, y + 73), label, F["small_bold"], TEXT_SECONDARY)


def profile_row(draw, y, glyph, label, value):
    center_text(draw, (28, y, 48, y + 34), glyph, F["icon_small"], TEXT_MUTED)
    draw_text(draw, (60, y + 8), label, F["medium"], TEXT_SECONDARY)
    vw = text_width(draw, value, F["medium_bold"])
    draw_text(draw, (W - 28 - vw, y + 8), value, F["medium_bold"], TEXT_PRIMARY)


def shared_row(draw, y, glyph, title, subtitle):
    rounded(draw, (28, y, 70, y + 42), 12, FILE_BUBBLE)
    center_text(draw, (28, y, 70, y + 42), glyph, F["icon_small"], TEXT_SECONDARY)
    draw_text(draw, (82, y + 2), title, F["body"], TEXT_PRIMARY, max_width=235)
    draw_text(draw, (82, y + 24), subtitle, F["small"], TEXT_MUTED)
    center_text(draw, (W - 42, y + 6, W - 20, y + 36), "›", F["icon"], TEXT_MUTED)


def option_row(draw, y, glyph, label, danger=False):
    color = ACCENT if danger else TEXT_SECONDARY
    center_text(draw, (28, y, 49, y + 38), glyph, F["icon_small"], color if danger else TEXT_MUTED)
    draw_text(draw, (60, y + 9), label, F["body"], color)
    center_text(draw, (W - 42, y + 4, W - 20, y + 34), "›", F["icon"], TEXT_MUTED)


def draw_profile():
    screen = Image.new("RGB", (W, H), BACKGROUND)
    d = ImageDraw.Draw(screen)
    y0 = 22
    d.rectangle((0, y0, W, y0 + 72), fill=SURFACE)
    icon(d, 4, y0 + 8, "‹")
    center_text(d, (72, y0 + 8, W - 72, y0 + 56), "Hồ sơ", F["title"], TEXT_PRIMARY)
    icon(d, W - 52, y0 + 8, "⋮")

    # Hero gradient, close to Brush.verticalGradient(Color(0xFFFDF2F2), ChatSurface).
    hero = Image.new("RGB", (W, 217), SURFACE)
    hp = hero.load()
    top, bottom = (253, 242, 242), (255, 255, 255)
    for yy in range(217):
        t = yy / 216
        color = tuple(round(top[i] * (1 - t) + bottom[i] * t) for i in range(3))
        for xx in range(W):
            hp[xx, yy] = color
    screen.paste(hero, (0, 94))
    avatar(screen, 150, 114, 92, "AM", True)
    center_text(d, (20, 214, W - 20, 244), "Nguyễn Anh Minh", F["h1"], TEXT_PRIMARY)
    center_text(d, (20, 248, W - 20, 269), "Đang hoạt động", F["medium_bold"], ONLINE)
    center_text(d, (20, 276, W - 20, 296), "Kế toán tổng hợp", F["medium"], TEXT_SECONDARY)

    d.rectangle((0, 311, W, 407), fill=SURFACE)
    for x, glyph, label in [(35, "●", "Nhắn tin"), (124, "☎", "Gọi"), (213, "▣", "Video"), (302, "◌", "Tắt chuông")]:
        profile_action(d, x, 325, glyph, label)

    rounded(d, (14, 415, W - 14, 600), 18, SURFACE, OUTLINE)
    draw_text(d, (28, 429), "Thông tin", F["body_bold"], TEXT_PRIMARY)
    profile_row(d, 466, "☎", "Số điện thoại", "090 123 4567")
    profile_row(d, 504, "✉", "Email", "minh@congty.vn")
    profile_row(d, 542, "▣", "Phòng ban", "Kế toán")
    profile_row(d, 580, "◉", "Vai trò", "Nhân viên")

    rounded(d, (14, 608, W - 14, 787), 18, SURFACE, OUTLINE)
    draw_text(d, (28, 622), "Ảnh, file đã chia sẻ", F["body_bold"], TEXT_PRIMARY)
    shared_row(d, 658, "▤", "BANGCONG_T07.xlsx", "XLSX · 248 KB")
    shared_row(d, 705, "▧", "Ảnh chấm công", "3 ảnh")
    shared_row(d, 752, "▥", "Tài liệu nhân sự", "5 file")

    rounded(d, (14, 795, W - 14, 945), 18, SURFACE, OUTLINE)
    draw_text(d, (28, 809), "Tùy chọn", F["body_bold"], TEXT_PRIMARY)
    option_row(d, 844, "⌕", "Tìm tin nhắn")
    option_row(d, 888, "⌖", "Ghim hội thoại")
    option_row(d, 932, "⌫", "Xóa lịch sử", True)
    return screen


def rounded_paste(canvas, image, xy, radius):
    mask = Image.new("L", image.size, 0)
    mdraw = ImageDraw.Draw(mask)
    mdraw.rounded_rectangle((0, 0, image.size[0] - 1, image.size[1] - 1), radius=radius, fill=255)
    canvas.paste(image, xy, mask)


def frame_phone(screen, caption):
    frame_w, frame_h = W + 20, H + 20
    panel = Image.new("RGBA", (frame_w, frame_h + 30), (0, 0, 0, 0))
    d = ImageDraw.Draw(panel)
    center_text(d, (0, 0, frame_w, 22), caption, F["medium_bold"], "#374151")
    rounded(d, (0, 30, frame_w, frame_h + 30), 34, "#101827")
    rounded_paste(panel, screen.convert("RGBA"), (10, 40), 24)
    return panel


def save_all():
    inbox = draw_inbox()
    thread = draw_thread()
    profile = draw_profile()
    inbox.save(ROOT / "chat-inbox.png")
    thread.save(ROOT / "chat-thread.png")
    profile.save(ROOT / "chat-profile.png")

    width = 28 * 2 + (W + 20) * 3 + 24 * 2
    height = 28 + 44 + (H + 50) + 28
    overview = Image.new("RGB", (width, height), "#EEF1F5")
    d = ImageDraw.Draw(overview)
    draw_text(d, (28, 24), "KetoanAPK Chat UI", font("bold", 24), "#1F2937")
    draw_text(d, (28, 55), "Render tĩnh từ ChatScreens.kt, dùng dữ liệu mẫu trong source", F["small"], "#6B7280")
    x = 28
    for screen, caption in [
        (inbox, "ChatInboxScreen"),
        (thread, "ChatThreadScreen"),
        (profile, "ChatContactProfileScreen"),
    ]:
        panel = frame_phone(screen, caption)
        overview.paste(panel.convert("RGB"), (x, 88), panel)
        x += W + 20 + 24
    overview.save(ROOT / "chat-preview-overview.png")


if __name__ == "__main__":
    save_all()
    for name in ["chat-preview-overview.png", "chat-inbox.png", "chat-thread.png", "chat-profile.png"]:
        path = ROOT / name
        print(f"{name} {path.stat().st_size} bytes")
