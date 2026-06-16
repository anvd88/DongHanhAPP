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
        // ────────────────────────────────────────────────────────────
        // SIDEBAR COLORS
        // ────────────────────────────────────────────────────────────
        public static readonly Color SidebarBg          = Color.FromArgb(15,  23,  42);   // #0F172A very dark navy
        public static readonly Color SidebarHover        = Color.FromArgb(30,  41,  59);   // #1E293B
        public static readonly Color SidebarActive       = Color.FromArgb(37,  99,  235);  // #2563EB blue
        public static readonly Color SidebarActiveAlpha  = Color.FromArgb(30,  37,  99, 235); // subtle blue overlay
        public static readonly Color SidebarText         = Color.FromArgb(148, 163, 184);  // #94A3B8
        public static readonly Color SidebarTextActive   = Color.White;
        public static readonly Color SidebarSection      = Color.FromArgb(71,  85,  105);  // #475569

        // ────────────────────────────────────────────────────────────
        // MAIN SURFACE COLORS
        // ────────────────────────────────────────────────────────────
        public static readonly Color Background    = Color.FromArgb(241, 245, 249);  // #F1F5F9 light gray-blue
        public static readonly Color Surface       = Color.White;
        public static readonly Color SurfaceAlt    = Color.FromArgb(248, 250, 252);
        public static readonly Color Border        = Color.FromArgb(226, 232, 240);  // #E2E8F0
        public static readonly Color HeaderBg      = Color.White;
        public static readonly Color WorkCardBg    = Color.FromArgb(15,  23,  42);

        // ────────────────────────────────────────────────────────────
        // TEXT COLORS
        // ────────────────────────────────────────────────────────────
        public static readonly Color TextPrimary   = Color.FromArgb(15,  23,  42);   // #0F172A
        public static readonly Color TextSecondary = Color.FromArgb(100, 116, 139);  // #64748B
        public static readonly Color TextMuted     = Color.FromArgb(148, 163, 184);  // #94A3B8

        // ────────────────────────────────────────────────────────────
        // ACCENT / BRAND COLORS
        // ────────────────────────────────────────────────────────────
        public static readonly Color Accent        = Color.FromArgb(37,  99,  235);  // #2563EB
        public static readonly Color AccentLight   = Color.FromArgb(219, 234, 254);  // #DBEAFE
        public static readonly Color AccentHover   = Color.FromArgb(29,  78,  216);  // #1D4ED8

        // ────────────────────────────────────────────────────────────
        // SEMANTIC COLORS
        // ────────────────────────────────────────────────────────────
        public static readonly Color Success       = Color.FromArgb(16,  185, 129);
        public static readonly Color SuccessLight  = Color.FromArgb(209, 250, 229);

        public static readonly Color Warning       = Color.FromArgb(245, 158, 11);
        public static readonly Color WarningLight  = Color.FromArgb(254, 243, 199);

        public static readonly Color Danger        = Color.FromArgb(239, 68,  68);
        public static readonly Color DangerLight   = Color.FromArgb(254, 226, 226);

        public static readonly Color Purple        = Color.FromArgb(139, 92,  246);
        public static readonly Color PurpleLight   = Color.FromArgb(237, 233, 254);

        public static readonly Color NeutralBadge      = Color.FromArgb(100, 116, 139);
        public static readonly Color NeutralBadgeLight = Color.FromArgb(241, 245, 249);

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
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 252, 254);

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
