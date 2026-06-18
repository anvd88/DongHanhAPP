using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace KetoanMini
{
    // ════════════════════════════════════════════════════════════════════════════
    // 0. BufferedPanel
    //    A plain Panel with double-buffering enabled to avoid flicker when its
    //    children are rebuilt (used by the Gia công detail pane).
    // ════════════════════════════════════════════════════════════════════════════
    public class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 1. RoundedPanel
    //    A Panel that paints itself with rounded corners, optional shadow,
    //    and clips child controls to the rounded shape.
    // ════════════════════════════════════════════════════════════════════════════
    public class RoundedPanel : Panel
    {
        private Color _fillColor    = AppTheme.Surface;
        private Color _borderColor  = Color.Transparent;
        private int   _cornerRadius = 12;
        private int   _shadowDepth  = 0;

        public Color FillColor
        {
            get => _fillColor;
            set { _fillColor = value; Invalidate(); }
        }

        public Color BorderColor
        {
            get => _borderColor;
            set { _borderColor = value; Invalidate(); }
        }

        public int CornerRadius
        {
            get => _cornerRadius;
            set { _cornerRadius = value; UpdateRegion(); Invalidate(); }
        }

        public int ShadowDepth
        {
            get => _shadowDepth;
            set { _shadowDepth = value; Invalidate(); }
        }

        public RoundedPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint  |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw          |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
            BackColor = Color.Transparent;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using var path = RoundedRect(new Rectangle(0, 0, Width, Height), _cornerRadius);
            var previous = Region;        // assigning Region does NOT dispose the old one
            Region = new Region(path);
            previous?.Dispose();          // avoid GDI handle churn/leak during live resize
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g   = e.Graphics;
            g.SmoothingMode      = SmoothingMode.AntiAlias;
            g.PixelOffsetMode    = PixelOffsetMode.HighQuality;
            g.TextRenderingHint  = TextRenderingHint.ClearTypeGridFit;

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

            // ── Shadow ──────────────────────────────────────────────
            if (_shadowDepth > 0)
            {
                for (int i = _shadowDepth; i >= 1; i--)
                {
                    int alpha = (int)(40.0 / i);
                    var shadowRect = new Rectangle(
                        bounds.X + i,
                        bounds.Y + i,
                        bounds.Width,
                        bounds.Height);
                    using var shadowPath = RoundedRect(shadowRect, _cornerRadius);
                    using var shadowBrush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
                    g.FillPath(shadowBrush, shadowPath);
                }
            }

            // ── Fill ────────────────────────────────────────────────
            using var fillPath = RoundedRect(bounds, _cornerRadius);
            using var fillBrush = new SolidBrush(_fillColor);
            g.FillPath(fillBrush, fillPath);

            // ── Border ──────────────────────────────────────────────
            if (_borderColor != Color.Transparent && _borderColor.A > 0)
            {
                using var pen = new Pen(_borderColor, 1f);
                g.DrawPath(pen, fillPath);
            }

            base.OnPaint(e);
        }

        /// <summary>
        /// Builds a GraphicsPath for a rectangle with rounded corners.
        /// </summary>
        internal static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            int maxR = Math.Max(1, Math.Min(rect.Width, rect.Height) / 2);
            radius   = Math.Clamp(radius, 1, maxR);

            int d   = radius * 2;
            var path = new GraphicsPath();

            path.AddArc(rect.X,                    rect.Y,                     d, d, 180, 90); // top-left
            path.AddArc(rect.Right - d,            rect.Y,                     d, d, 270, 90); // top-right
            path.AddArc(rect.Right - d,            rect.Bottom - d,            d, d,   0, 90); // bottom-right
            path.AddArc(rect.X,                    rect.Bottom - d,            d, d,  90, 90); // bottom-left
            path.CloseFigure();

            return path;
        }
    }


    // ════════════════════════════════════════════════════════════════════════════
    // 2. RoundedButton
    //    Owner-drawn button with rounded corners and hover/press color shift.
    // ════════════════════════════════════════════════════════════════════════════
    public class RoundedButton : Button
    {
        private int   _cornerRadius = 8;
        private Color _borderColor  = Color.Transparent;
        private bool  _hovered      = false;
        private bool  _pressed      = false;

        public int CornerRadius
        {
            get => _cornerRadius;
            set { _cornerRadius = value; Invalidate(); }
        }

        public Color BorderColor
        {
            get => _borderColor;
            set { _borderColor = value; Invalidate(); }
        }

        public RoundedButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint  |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw          |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);

            FlatStyle                    = FlatStyle.Flat;
            FlatAppearance.BorderSize    = 0;
            Cursor                       = Cursors.Hand;
        }

        // Không vẽ khung focus chấm chấm (dotted focus rectangle) — nút tự vẽ bo góc
        // nên khung mặc định của WinForms khiến nút trông bị "vỡ".
        protected override bool ShowFocusCues => false;

        protected override void OnMouseEnter(EventArgs e) { _hovered = true;  _pressed = false; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode   = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Clear with parent background to avoid square artifacts
            Color parentBg = Parent?.BackColor ?? Color.Transparent;
            if (parentBg != Color.Transparent)
            {
                using var clearBrush = new SolidBrush(parentBg);
                g.FillRectangle(clearBrush, ClientRectangle);
            }

            // Compute effective fill color
            Color fill = BackColor;
            if (_pressed)
                fill = ShiftBrightness(fill, -0.06f);
            else if (_hovered)
                fill = ShiftBrightness(fill, 0.06f);

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedPanel.RoundedRect(bounds, _cornerRadius);

            // Fill
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);

            // Border
            if (_borderColor != Color.Transparent && _borderColor.A > 0)
            {
                using var pen = new Pen(_borderColor, 1f);
                g.DrawPath(pen, path);
            }

            // Text
            TextRenderer.DrawText(
                g, Text, Font, bounds, ForeColor,
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.SingleLine);
        }

        private static Color ShiftBrightness(Color c, float delta)
        {
            static int Clamp(float v) => (int)Math.Clamp(v, 0, 255);
            float factor = 1f + delta;
            return Color.FromArgb(
                c.A,
                Clamp(c.R * factor),
                Clamp(c.G * factor),
                Clamp(c.B * factor));
        }
    }


    // ════════════════════════════════════════════════════════════════════════════
    // 3. SidebarNavButton
    //    A sidebar navigation item that draws icon + label with active/hover state.
    // ════════════════════════════════════════════════════════════════════════════
    public class SidebarNavButton : Control
    {
        private string _navKey   = string.Empty;
        private string _icon     = string.Empty;
        private string _title    = string.Empty;
        private bool   _isActive = false;
        private Color  _activeBg = AppTheme.SidebarActive;
        private Color  _hoverBg  = AppTheme.SidebarHover;
        private bool   _hovered  = false;

        public string NavKey
        {
            get => _navKey;
            set { _navKey = value; Invalidate(); }
        }

        public string Icon
        {
            get => _icon;
            set { _icon = value; Invalidate(); }
        }

        public string Title
        {
            get => _title;
            set { _title = value; Invalidate(); }
        }

        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; Invalidate(); }
        }

        public Color ActiveBg
        {
            get => _activeBg;
            set { _activeBg = value; Invalidate(); }
        }

        public Color HoverBg
        {
            get => _hoverBg;
            set { _hoverBg = value; Invalidate(); }
        }

        public SidebarNavButton()
        {
            Height = 40;
            Cursor = Cursors.Hand;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint  |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw          |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { _hovered = true;  Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e) => base.OnClick(e);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode      = SmoothingMode.AntiAlias;
            g.TextRenderingHint  = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode    = PixelOffsetMode.HighQuality;

            // ── Background pill ─────────────────────────────────────
            Color bg = _isActive  ? _activeBg :
                       _hovered   ? _hoverBg  :
                                    Color.Transparent;

            if (bg != Color.Transparent)
            {
                const int marginX = 8;
                var bgRect = new Rectangle(
                    marginX, 2,
                    Width - marginX * 2, Height - 4);
                using var bgPath = RoundedPanel.RoundedRect(bgRect, 8);
                using var bgBrush = new SolidBrush(bg);
                g.FillPath(bgBrush, bgPath);
            }

            Color textColor = _isActive ? Color.White : AppTheme.SidebarText;
            int   midY      = Height / 2;

            // ── Icon ────────────────────────────────────────────────
            if (!string.IsNullOrEmpty(_icon))
            {
                using var iconFont = new Font("Segoe MDL2 Assets", 14f, FontStyle.Regular, GraphicsUnit.Point);
                var iconRect = new Rectangle(18, 0, 24, Height);
                TextRenderer.DrawText(g, _icon, iconFont, iconRect, textColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
            }

            // ── Title text ──────────────────────────────────────────
            if (!string.IsNullOrEmpty(_title))
            {
                var titleRect = new Rectangle(46, 0, Math.Max(0, Width - 54), Height);
                TextRenderer.DrawText(g, _title, AppTheme.F9, titleRect, textColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }
        }
    }


    // ════════════════════════════════════════════════════════════════════════════
    // 4. StatCard
    //    KPI card with icon block, big value, label, and optional trend indicator.
    // ════════════════════════════════════════════════════════════════════════════
    public class StatCard : RoundedPanel
    {
        private string _cardTitle    = string.Empty;
        private string _valueText    = string.Empty;
        private string _subText      = string.Empty;
        private string _iconText     = string.Empty;
        private Color  _iconBg       = AppTheme.AccentLight;
        private string _trendText    = string.Empty;
        private bool   _trendPositive = true;

        public string CardTitle
        {
            get => _cardTitle;
            set { _cardTitle = value; Invalidate(); }
        }

        public string ValueText
        {
            get => _valueText;
            set { _valueText = value; Invalidate(); }
        }

        public string SubText
        {
            get => _subText;
            set { _subText = value; Invalidate(); }
        }

        public string IconText
        {
            get => _iconText;
            set { _iconText = value; Invalidate(); }
        }

        public Color IconBg
        {
            get => _iconBg;
            set { _iconBg = value; Invalidate(); }
        }

        public string TrendText
        {
            get => _trendText;
            set { _trendText = value; Invalidate(); }
        }

        public bool TrendPositive
        {
            get => _trendPositive;
            set { _trendPositive = value; Invalidate(); }
        }

        public StatCard()
        {
            // Đọc màu theo theme hiện tại (thẻ được dựng lại khi đổi theme) để
            // không bị nền trắng cố định ở chế độ tối. Viền giúp thấy mép thẻ khi
            // bóng đổ gần như vô hình trên nền tối.
            FillColor    = AppTheme.Surface;
            BorderColor  = AppTheme.Border;
            CornerRadius = 12;
            ShadowDepth  = 2;
            Height       = 110;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);   // draws shadow, fill, border

            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            const int iconSize = 48;
            const int iconX    = 16;
            int       iconY    = (Height - iconSize) / 2;

            // ── Icon block ──────────────────────────────────────────
            if (!string.IsNullOrEmpty(_iconText))
            {
                var iconRect = new Rectangle(iconX, iconY, iconSize, iconSize);
                using var iconPath = RoundedPanel.RoundedRect(iconRect, 10);
                using var iconBrush = new SolidBrush(_iconBg);
                g.FillPath(iconBrush, iconPath);

                using var iconFont = new Font("Segoe UI Emoji", 20f, FontStyle.Regular, GraphicsUnit.Point);
                TextRenderer.DrawText(g, _iconText, iconFont, iconRect, AppTheme.TextPrimary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
            }

            // ── Text area (right of icon) ───────────────────────────
            int textX    = iconX + iconSize + 14;
            int textW    = Math.Max(0, Width - textX - 12);

            // Trend badge width estimation
            int trendW   = 0;
            if (!string.IsNullOrEmpty(_trendText))
                trendW = 64;

            // Title
            var titleRect = new Rectangle(textX, 14, textW, 18);
            TextRenderer.DrawText(g, _cardTitle, AppTheme.F8, titleRect, AppTheme.TextSecondary,
                TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            // Value
            var valueRect = new Rectangle(textX, 30, textW, 38);
            TextRenderer.DrawText(g, _valueText, AppTheme.F22B, valueRect, AppTheme.TextPrimary,
                TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            // SubText
            var subRect = new Rectangle(textX, 72, textW - trendW, 20);
            TextRenderer.DrawText(g, _subText, AppTheme.F8, subRect, AppTheme.TextMuted,
                TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            // ── Trend badge ─────────────────────────────────────────
            if (!string.IsNullOrEmpty(_trendText))
            {
                Color trendFg = _trendPositive ? AppTheme.Success : AppTheme.Danger;
                Color trendBg = _trendPositive ? AppTheme.SuccessLight : AppTheme.DangerLight;
                string arrow  = _trendPositive ? "▲ " : "▼ ";

                var trendRect = new Rectangle(Width - trendW - 12, Height - 28, trendW, 18);
                using var trendPath = RoundedPanel.RoundedRect(trendRect, 6);
                using var trendBrush = new SolidBrush(trendBg);
                g.FillPath(trendBrush, trendPath);

                TextRenderer.DrawText(g, arrow + _trendText, AppTheme.F8B, trendRect, trendFg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
            }
        }
    }


    // ════════════════════════════════════════════════════════════════════════════
    // 5. BadgeLabel
    //    A small pill-shaped label for status badges.
    // ════════════════════════════════════════════════════════════════════════════
    public class BadgeLabel : Label
    {
        private Color _badgeBg       = AppTheme.AccentLight;
        private Color _badgeFg       = AppTheme.Accent;
        private int   _cornerRadius  = 10;

        public Color BadgeBg
        {
            get => _badgeBg;
            set { _badgeBg = value; Invalidate(); }
        }

        public Color BadgeFg
        {
            get => _badgeFg;
            set { _badgeFg = value; Invalidate(); }
        }

        public int CornerRadius
        {
            get => _cornerRadius;
            set { _cornerRadius = value; Invalidate(); }
        }

        public BadgeLabel()
        {
            AutoSize  = false;
            Height    = 22;
            Padding   = new Padding(8, 0, 8, 0);
            TextAlign = ContentAlignment.MiddleCenter;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint  |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw          |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode      = SmoothingMode.AntiAlias;
            g.TextRenderingHint  = TextRenderingHint.ClearTypeGridFit;

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedPanel.RoundedRect(bounds, _cornerRadius);
            using var brush = new SolidBrush(_badgeBg);
            g.FillPath(brush, path);

            TextRenderer.DrawText(g, Text, Font, bounds, _badgeFg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }


    // ════════════════════════════════════════════════════════════════════════════
    // 6. SearchBox
    //    Display-only search bar with placeholder, icon, and shortcut hint badge.
    // ════════════════════════════════════════════════════════════════════════════
    public class SearchBox : Control
    {
        private string _placeholderText = "Search...";
        private string _hintText        = "Ctrl + K";
        private string _text            = string.Empty;
        private int    _cornerRadius    = 8;
        private bool   _hovered         = false;

        public string PlaceholderText
        {
            get => _placeholderText;
            set { _placeholderText = value; Invalidate(); }
        }

        public string HintText
        {
            get => _hintText;
            set { _hintText = value; Invalidate(); }
        }

#pragma warning disable CS8764
        public override string? Text
        {
            get => _text;
            set { _text = value ?? ""; Invalidate(); }
        }
#pragma warning restore CS8764

        public int CornerRadius
        {
            get => _cornerRadius;
            set { _cornerRadius = value; Invalidate(); }
        }

        public SearchBox()
        {
            Height    = 36;
            Cursor    = Cursors.IBeam;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint  |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw          |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { _hovered = true;  Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode      = SmoothingMode.AntiAlias;
            g.TextRenderingHint  = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode    = PixelOffsetMode.HighQuality;

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedPanel.RoundedRect(bounds, _cornerRadius);

            // ── Background fill ─────────────────────────────────────
            Color bgColor = _hovered ? AppTheme.SurfaceAlt : AppTheme.Surface;
            using var bgBrush = new SolidBrush(bgColor);
            g.FillPath(bgBrush, path);

            // ── Border ──────────────────────────────────────────────
            Color borderColor = _hovered ? AppTheme.Accent : AppTheme.Border;
            using var borderPen = new Pen(borderColor, 1f);
            g.DrawPath(borderPen, path);

            // ── Magnifier icon ──────────────────────────────────────
            const string searchIcon = "🔍";
            var iconRect = new Rectangle(6, 0, 28, Height);
            using var iconFont = new Font("Segoe UI Emoji", 11f, FontStyle.Regular, GraphicsUnit.Point);
            TextRenderer.DrawText(g, searchIcon, iconFont, iconRect, AppTheme.TextMuted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);

            // ── Hint badge on the right ─────────────────────────────
            const int hintPadX = 8;
            const int hintPadY = 5;
            int hintBadgeW = 0;

            if (!string.IsNullOrEmpty(_hintText))
            {
                Size hintSize = TextRenderer.MeasureText(_hintText, AppTheme.F8);
                hintBadgeW    = hintSize.Width + 12;
                int hintBadgeH = Height - hintPadY * 2;
                var hintRect   = new Rectangle(
                    Width - hintBadgeW - hintPadX,
                    hintPadY,
                    hintBadgeW,
                    hintBadgeH);

                using var hintPath = RoundedPanel.RoundedRect(hintRect, 4);
                using var hintBgBrush = new SolidBrush(AppTheme.NeutralBadgeLight);
                g.FillPath(hintBgBrush, hintPath);

                using var hintBorderPen = new Pen(AppTheme.Border, 1f);
                g.DrawPath(hintBorderPen, hintPath);

                TextRenderer.DrawText(g, _hintText, AppTheme.F8, hintRect, AppTheme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
            }

            // ── Placeholder / value text ────────────────────────────
            string display    = string.IsNullOrEmpty(_text) ? _placeholderText : _text;
            Color  displayFg  = string.IsNullOrEmpty(_text) ? AppTheme.TextMuted : AppTheme.TextPrimary;
            int    textLeft   = 38;
            int    textRight  = hintBadgeW > 0 ? hintBadgeW + hintPadX + 4 : 8;
            var textRect      = new Rectangle(textLeft, 0, Math.Max(0, Width - textLeft - textRight), Height);

            TextRenderer.DrawText(g, display, AppTheme.F9, textRect, displayFg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }


    // ════════════════════════════════════════════════════════════════════════════
    // 7. WorkShiftCard
    //    Compact navy card showing the current work shift. Two rows:
    //      • top:    "Ca làm việc" label (left) + status (right, green)
    //      • bottom: clock icon + time (left) + chevron-down (right)
    //    The faint blue border brightens smoothly on hover (timer-animated).
    // ════════════════════════════════════════════════════════════════════════════
    public class WorkShiftCard : Control
    {
        private int    _cornerRadius = 8;
        private string _label        = "Ca làm việc";
        private string _status       = "Đang làm việc";
        private string _time         = "08:00 - 17:00";

        // ── Hover animation (0 = idle, 1 = fully hovered) ──
        private readonly System.Windows.Forms.Timer _anim;
        private float          _hoverT  = 0f;
        private bool           _hovered = false;

        // ── Palette ──
        private static readonly Color CardFill     = Color.FromArgb(15,  23,  42);    // #0F172A navy
        private static readonly Color BorderIdle    = Color.FromArgb(70,  59,  130, 246); // faint blue
        private static readonly Color BorderHover    = Color.FromArgb(205, 96,  165, 250); // bright blue
        private static readonly Color LabelColor     = Color.FromArgb(203, 213, 225);  // #CBD5E1 light gray
        private static readonly Color TimeColor      = Color.White;
        private static readonly Color ChevronColor   = Color.FromArgb(148, 163, 184);  // #94A3B8 gray

        // Status colour is dynamic (green = working, red = off-hours, amber = overtime…)
        private Color _statusColor = Color.FromArgb(52, 211, 153);  // #34D399 green (default)

        public string StatusText  { get => _status; set { if (_status == value) return; _status = value; Invalidate(); } }
        public string TimeText    { get => _time;   set { if (_time == value) return; _time = value; Invalidate(); } }
        public string LabelText   { get => _label;  set { if (_label == value) return; _label = value; Invalidate(); } }
        public Color  StatusColor { get => _statusColor; set { if (_statusColor == value) return; _statusColor = value; Invalidate(); } }
        public int    CornerRadius { get => _cornerRadius; set { if (_cornerRadius == value) return; _cornerRadius = value; Invalidate(); } }

        public WorkShiftCard()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint  |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw          |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
            BackColor = Color.Transparent;
            Cursor    = Cursors.Hand;
            Size      = new Size(164, 52);

            _anim = new System.Windows.Forms.Timer { Interval = 15 };
            _anim.Tick += (s, e) =>
            {
                float target = _hovered ? 1f : 0f;
                const float step = 0.16f;
                if (Math.Abs(_hoverT - target) <= step) { _hoverT = target; _anim.Stop(); }
                else _hoverT += Math.Sign(target - _hoverT) * step;
                Invalidate();
            };
        }

        protected override void OnMouseEnter(EventArgs e) { _hovered = true;  _anim.Start(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; _anim.Start(); base.OnMouseLeave(e); }

        private static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int w = Width, h = Height;
            var bounds = new Rectangle(0, 0, w - 1, h - 1);

            // ── Fill ──
            using (var fillPath = RoundedPanel.RoundedRect(bounds, _cornerRadius))
            using (var fillBrush = new SolidBrush(CardFill))
                g.FillPath(fillBrush, fillPath);

            // ── Border (faint blue → bright on hover) ──
            using (var borderPath = RoundedPanel.RoundedRect(bounds, _cornerRadius))
            using (var pen = new Pen(Lerp(BorderIdle, BorderHover, _hoverT), 1.2f))
                g.DrawPath(pen, borderPath);

            const TextFormatFlags tf = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.Top;
            const int padL = 12, padR = 12;
            int row1Y = 3, row2Y = 22;

            // ── Row 1: label (left) ──
            TextRenderer.DrawText(g, _label, AppTheme.F8, new Point(padL, row1Y), LabelColor, tf);

            // ── Row 1: status (right-aligned, colour reflects shift state) ──
            Size statusSize = TextRenderer.MeasureText(g, _status, AppTheme.F8, Size.Empty, tf);
            TextRenderer.DrawText(g, _status, AppTheme.F8,
                new Point(w - padR - statusSize.Width, row1Y), _statusColor, tf);

            // ── Row 2: clock icon + time (left) ──
            var clockRect = new Rectangle(padL, row2Y + 1, 13, 13);
            DrawClock(g, clockRect, _statusColor);
            TextRenderer.DrawText(g, _time, AppTheme.F9B,
                new Point(clockRect.Right + 6, row2Y), TimeColor, tf);

            // ── Row 2: chevron-down (right) ──
            DrawChevron(g, new PointF(w - padR - 9, row2Y + 5), ChevronColor);
        }

        private static void DrawClock(Graphics g, Rectangle r, Color color)
        {
            using var pen = new Pen(color, 1.4f);
            g.DrawEllipse(pen, r);
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            g.DrawLine(pen, cx, cy, cx, cy - r.Height * 0.30f);   // minute hand (up)
            g.DrawLine(pen, cx, cy, cx + r.Width * 0.24f, cy);    // hour hand (right)
        }

        private static void DrawChevron(Graphics g, PointF top, Color color)
        {
            using var pen = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, new[]
            {
                new PointF(top.X,        top.Y),
                new PointF(top.X + 4.5f, top.Y + 4.5f),
                new PointF(top.X + 9f,   top.Y)
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _anim?.Dispose();
            base.Dispose(disposing);
        }
    }
}
