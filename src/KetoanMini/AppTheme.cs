using System;
using System.Drawing;
using System.Windows.Forms;

namespace KetoanMini
{
    /// <summary>
    /// Central theme definitions for the KetoanMini application.
    /// All colors, fonts, and common styling helpers live here.
    /// </summary>
    public static class AppTheme
    {
        // Mỗi token màu trả về giá trị theo theme đang chọn (Sáng/Tối) để hỗ trợ
        // đổi giao diện "live". D = đang ở chế độ tối. C("#hex") tạo Color từ mã hex.
        private static bool D => ThemeState.IsDark;
        private static Color C(string hex) => ColorTranslator.FromHtml(hex);

        // ────────────────────────────────────────────────────────────
        // SIDEBAR COLORS
        // ────────────────────────────────────────────────────────────
        public static Color SidebarBg         => D ? C("#050608") : C("#0F172A");
        public static Color SidebarHover       => D ? C("#101317") : C("#1E293B");
        public static Color SidebarActive      => D ? C("#11C5BF") : C("#2563EB");
        public static Color SidebarActiveAlpha => D ? Color.FromArgb(40, 17, 197, 191) : Color.FromArgb(30, 37, 99, 235);
        public static Color SidebarText        => D ? C("#9AA3AF") : C("#94A3B8");
        public static Color SidebarTextActive  => D ? C("#F5F7FA") : Color.White;
        public static Color SidebarSection     => D ? C("#5F6875") : C("#475569");

        // ────────────────────────────────────────────────────────────
        // MAIN SURFACE COLORS
        // ────────────────────────────────────────────────────────────
        public static Color Background => D ? C("#050608") : C("#F1F5F9");
        public static Color Surface    => D ? C("#0A0C0F") : Color.White;
        public static Color SurfaceAlt => D ? C("#0E1116") : C("#F8FAFC");
        public static Color Border     => D ? C("#1C222B") : C("#E2E8F0");
        public static Color HeaderBg   => D ? C("#000000") : Color.White;
        public static Color WorkCardBg => D ? C("#0B0E12") : C("#0F172A");

        // ────────────────────────────────────────────────────────────
        // TEXT COLORS
        // ────────────────────────────────────────────────────────────
        public static Color TextPrimary   => D ? C("#F5F7FA") : C("#0F172A");
        public static Color TextSecondary => D ? C("#A8B0BD") : C("#64748B");
        public static Color TextMuted     => D ? C("#7D8794") : C("#94A3B8");

        // ────────────────────────────────────────────────────────────
        // ACCENT / BRAND COLORS
        // ────────────────────────────────────────────────────────────
        public static Color Accent      => D ? C("#11C5BF") : C("#2563EB");
        public static Color AccentLight => D ? C("#0E2221") : C("#DBEAFE");
        public static Color AccentHover => D ? C("#18D7D0") : C("#1D4ED8");

        // ────────────────────────────────────────────────────────────
        // SEMANTIC COLORS
        // ────────────────────────────────────────────────────────────
        public static Color Success      => D ? C("#22C55E") : C("#10B981");
        public static Color SuccessLight => D ? C("#0E1A12") : C("#D1FAE5");

        public static Color Warning      => C("#F59E0B");
        public static Color WarningLight => D ? C("#1C1406") : C("#FEF3C7");

        public static Color Danger      => C("#EF4444");
        public static Color DangerLight => D ? C("#1A0C0C") : C("#FEE2E2");

        public static Color Purple      => C("#8B5CF6");
        public static Color PurpleLight => D ? C("#1A1430") : C("#EDE9FE");

        public static Color NeutralBadge      => D ? C("#7D8794") : C("#64748B");
        public static Color NeutralBadgeLight => D ? C("#111418") : C("#F1F5F9");

        // ────────────────────────────────────────────────────────────
        // FONTS
        // ────────────────────────────────────────────────────────────
        public static readonly Font F8    = new Font("Segoe UI",  8F);
        public static readonly Font F8B   = new Font("Segoe UI",  8F,  FontStyle.Bold);
        public static readonly Font F9    = new Font("Segoe UI",  9F);
        public static readonly Font F9B   = new Font("Segoe UI",  9F,  FontStyle.Bold);
        public static readonly Font F10   = new Font("Segoe UI", 10F);
        public static readonly Font F10B  = new Font("Segoe UI", 10F, FontStyle.Bold);
        public static readonly Font F11B  = new Font("Segoe UI", 11F, FontStyle.Bold);
        public static readonly Font F12B  = new Font("Segoe UI", 12F, FontStyle.Bold);
        public static readonly Font F14B  = new Font("Segoe UI", 14F, FontStyle.Bold);
        public static readonly Font F18B  = new Font("Segoe UI", 18F, FontStyle.Bold);
        public static readonly Font F22B  = new Font("Segoe UI", 22F, FontStyle.Bold);
        public static readonly Font FSect = new Font("Segoe UI",  7.5F, FontStyle.Bold);

        // ────────────────────────────────────────────────────────────
        // HELPER METHODS
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Apply modern, flat styling to a DataGridView.
        /// </summary>
        public static void StyleGrid(DataGridView grid)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));

            // Layout
            grid.BorderStyle                    = BorderStyle.None;
            grid.CellBorderStyle               = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.RowHeadersVisible             = false;
            grid.MultiSelect                   = false;
            grid.SelectionMode                 = DataGridViewSelectionMode.FullRowSelect;
            grid.AllowUserToResizeRows         = false;
            grid.AutoSizeColumnsMode           = DataGridViewAutoSizeColumnsMode.None;
            grid.ScrollBars                    = ScrollBars.Both;

            // Colors
            grid.BackgroundColor               = Surface;
            grid.GridColor                     = Border;
            grid.DefaultCellStyle.BackColor    = Surface;
            grid.DefaultCellStyle.ForeColor    = TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = AccentLight;
            grid.DefaultCellStyle.SelectionForeColor = TextPrimary;
            grid.DefaultCellStyle.Font         = F9;
            grid.DefaultCellStyle.Padding      = new Padding(8, 0, 8, 0);

            // Alternating rows
            grid.AlternatingRowsDefaultCellStyle.BackColor = D ? C("#0D1014") : Color.FromArgb(250, 252, 254);

            // Header
            grid.ColumnHeadersDefaultCellStyle.BackColor   = SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.ForeColor   = TextSecondary;
            grid.ColumnHeadersDefaultCellStyle.Font        = F9B;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextSecondary;
            grid.ColumnHeadersDefaultCellStyle.Padding     = new Padding(8, 0, 8, 0);
            grid.ColumnHeadersBorderStyle      = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersHeightSizeMode   = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight           = 36;

            // Row height
            grid.RowTemplate.Height            = 38;

            // Flat appearance
            grid.EnableHeadersVisualStyles     = false;

            // Anti-flicker: enable the grid's protected DoubleBuffered property.
            // DataGridView repaints heavily on populate/resize/scroll otherwise.
            try
            {
                typeof(DataGridView)
                    .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(grid, true);
            }
            catch { /* non-fatal */ }
        }

        /// <summary>
        /// Create a Panel with a given background color and optional uniform padding.
        /// </summary>
        public static Panel MakePanel(Color back, int pad = 0)
        {
            return new Panel
            {
                BackColor = back,
                Padding   = new Padding(pad)
            };
        }

        /// <summary>
        /// Create a Label that fills its parent cell (Dock=Fill, AutoSize=false).
        /// </summary>
        public static Label MakeLabel(
            string text,
            Font font,
            Color fore,
            ContentAlignment align = ContentAlignment.MiddleLeft)
        {
            return new Label
            {
                Text      = text,
                Font      = font,
                ForeColor = fore,
                TextAlign = align,
                AutoSize  = false,
                Dock      = DockStyle.Fill
            };
        }
    }
}
