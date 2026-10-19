using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace ClipTyper
{
    /// <summary>
    /// Windows 11 style for the tray and overlay menus: flat background, roomier rows, a rounded
    /// neutral highlight, simple check marks and rounded window corners. Colors follow the current
    /// light/dark appearance at paint time, so a theme change applies without rebuilding the menus.
    /// Submenus inherit the renderer from their owner menu.
    /// </summary>
    internal sealed class ModernMenuRenderer : ToolStripProfessionalRenderer
    {
        /// <summary>Set as an item's <see cref="ToolStripItem.Tag"/> to draw it in the warning color (e.g. "Stop Typing").</summary>
        public const string DangerTag = "danger";

        public static ModernMenuRenderer Instance { get; } = new();

        private static readonly ConditionalWeakTable<ToolStripDropDown, object> Prepared = new();

        private ModernMenuRenderer() : base(new ModernMenuColors())
        {
            RoundedEdges = false;
        }

        private static Color Text => Theme.IsDark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(28, 28, 28);
        private static Color DisabledText => Theme.IsDark ? Color.FromArgb(120, 120, 120) : Color.FromArgb(160, 160, 160);
        private static Color Highlight => Theme.IsDark ? Color.FromArgb(61, 61, 61) : Color.FromArgb(232, 232, 232);
        private static Color SeparatorColor => Theme.IsDark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(224, 224, 224);
        private static Color Arrow => Theme.IsDark ? Color.FromArgb(200, 200, 200) : Color.FromArgb(96, 96, 96);

        protected override void Initialize(ToolStrip toolStrip)
        {
            base.Initialize(toolStrip);
            if (toolStrip is ToolStripDropDown dropDown)
            {
                PrepareDropDown(dropDown);
            }
        }

        protected override void InitializeItem(ToolStripItem item)
        {
            base.InitializeItem(item);
            if (item is ToolStripMenuItem menuItem)
            {
                menuItem.Padding = new Padding(4, 5, 4, 5);
                PrepareDropDown(menuItem.DropDown); // submenu window: same padding and rounded corners
            }
        }

        private static void PrepareDropDown(ToolStripDropDown dropDown)
        {
            if (Prepared.TryGetValue(dropDown, out _))
            {
                return;
            }
            Prepared.Add(dropDown, new object());

            dropDown.Padding = new Padding(4, 6, 4, 6);
            if (dropDown.IsHandleCreated)
            {
                NativeMethods.RoundWindowCorners(dropDown.Handle);
            }
            dropDown.HandleCreated += (_, _) => NativeMethods.RoundWindowCorners(dropDown.Handle);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled)
            {
                return;
            }

            var bounds = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            using var path = RoundedRectangle(bounds, 4);
            using var brush = new SolidBrush(Highlight);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (!e.Item.Enabled)
            {
                e.TextColor = DisabledText;
            }
            else if (DangerTag.Equals(e.Item.Tag))
            {
                e.TextColor = Theme.Danger;
            }
            else
            {
                e.TextColor = Text;
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using var pen = new Pen(Text, Math.Max(1.6f, r.Width / 10f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float x = r.Left + r.Width * 0.22f;
            float y = r.Top + r.Height * 0.52f;
            e.Graphics.DrawLines(pen, new[]
            {
                new PointF(x, y),
                new PointF(x + r.Width * 0.2f, y + r.Height * 0.2f),
                new PointF(x + r.Width * 0.58f, y - r.Height * 0.25f)
            });
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using var pen = new Pen(SeparatorColor);
            e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Arrow;
            base.OnRenderArrow(e);
        }

        private static GraphicsPath RoundedRectangle(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private sealed class ModernMenuColors : ProfessionalColorTable
        {
            public ModernMenuColors()
            {
                UseSystemColors = false;
            }

            private static Color Background => Theme.IsDark ? Color.FromArgb(44, 44, 44) : Color.FromArgb(249, 249, 249);
            private static Color Border => Theme.IsDark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(214, 214, 214);

            public override Color ToolStripDropDownBackground => Background;
            public override Color ImageMarginGradientBegin => Background;
            public override Color ImageMarginGradientMiddle => Background;
            public override Color ImageMarginGradientEnd => Background;
            public override Color MenuBorder => Border;
            public override Color MenuItemBorder => Color.Transparent;
            public override Color CheckBackground => Color.Transparent;
            public override Color CheckSelectedBackground => Color.Transparent;
            public override Color CheckPressedBackground => Color.Transparent;
        }
    }
}
