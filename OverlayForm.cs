using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipTyper
{
    /// <summary>
    /// A floating, borderless, semi-transparent overlay button that provides
    /// a clickable alternative to the global hotkey for triggering clip-type.
    /// Designed for fullscreen RDP sessions where global hotkeys are forwarded
    /// to the remote session.
    /// </summary>
    public class OverlayForm : Form
    {
        // ── Size presets — match native icon sizes for crisp rendering ──
        private const int SizeSmall  = 64;
        private const int SizeMedium = 128;
        private const int SizeLarge  = 256;

        // ── Peek / animation constants ──────────────────────────────
        private const int EdgeThreshold    = 10;   // px — considered "at edge"
        private const int SnapThreshold    = 20;   // px — snap to edge on drag release
        private const double OpacityNormal = 0.7;
        private const double OpacityHover  = 0.95;
        private const int AnimationStepMs  = 15;   // Timer interval for slide animation
        private const int AnimationTotalMs = 200;  // Total slide duration

        // ── Fields ──────────────────────────────────────────────────
        private Icon? _overlayIcon128;   // 128×128 from media/ClipTyper_128.ico
        private Icon? _overlayIcon256;   // 256×256 from media/ClipTyper_256.ico
        private PictureBox _iconBox = null!;
        private ContextMenuStrip _overlayMenu = null!;

        // Drag state
        private bool _isDragging;
        private Point _dragOffset;
        private bool _hasDragged;
        private Point _dragStartScreenPos;

        // Peek animation
        private System.Windows.Forms.Timer _peekTimer = null!;
        private int _peekTarget;       // target X for slide animation
        private int _peekStep;         // pixels per timer tick
        private bool _isPeeking;       // true when parked at edge in peek mode
        private int _peekHiddenX;      // X position when hidden behind edge
        private int _peekVisibleX;     // X position when fully visible

        // Focus tracking — polls the foreground window so we know where to
        // return focus when the overlay is clicked.
        private System.Windows.Forms.Timer _focusTracker = null!;
        private IntPtr _lastForegroundWindow = IntPtr.Zero;

        // Callback to trigger clip-type
        private readonly Action<bool> _triggerClipType;

        // Initialization state
        private bool _isInitialized;

        /// <summary>
        /// Raised when the user selects "Hide Overlay" from the context menu.
        /// </summary>
        public event Action? OverlayHidden;

        /// <summary>
        /// Raised when the user changes the overlay size from the context menu.
        /// </summary>
        public new event Action<string>? SizeChanged;

        // ── Constructor ─────────────────────────────────────────────

        /// <param name="triggerClipType">
        /// Callback to invoke when the user clicks the overlay.
        /// The bool parameter indicates whether focus restoration is needed.
        /// </param>
        public OverlayForm(Action<bool> triggerClipType)
        {
            _triggerClipType = triggerClipType;
            InitializeForm();
            InitializeIcon();
            ApplySize(SettingsManager.Current.OverlaySize);
            InitializeContextMenu();
            InitializeFocusTracker();
            InitializePeekTimer();
            _isInitialized = true;
        }

        // ── Initialization ──────────────────────────────────────────

        private void InitializeForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Opacity = OpacityNormal;
            // Near-black instead of Magenta to avoid visible purple fringe
            // from anti-aliasing when the icon is composited.
            BackColor = Color.FromArgb(1, 1, 1);
            TransparencyKey = Color.FromArgb(1, 1, 1);

            // Size is applied after InitializeIcon() in the constructor
            Size = new Size(SizeMedium, SizeMedium);
        }

        private void InitializeIcon()
        {
            _overlayIcon128 = LoadEmbeddedIcon("ClipTyper.icon_overlay.ico");
            _overlayIcon256 = LoadEmbeddedIcon("ClipTyper.icon_app.ico");

            _iconBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };

            UpdateIconImage();

            _iconBox.MouseDown += OnIconMouseDown;
            _iconBox.MouseMove += OnIconMouseMove;
            _iconBox.MouseUp += OnIconMouseUp;
            _iconBox.MouseEnter += OnMouseEnterOverlay;
            _iconBox.MouseLeave += OnMouseLeaveOverlay;

            Controls.Add(_iconBox);
        }

        /// <summary>
        /// Selects the best icon for the current form size to avoid scaling.
        /// Small (64) and Medium (128) use the 128px icon.
        /// Large (256) uses the 256px icon.
        /// </summary>
        private void UpdateIconImage()
        {
            if (_iconBox == null) return;
            var icon = Width >= SizeLarge ? _overlayIcon256 : _overlayIcon128;
            _iconBox.Image = icon?.ToBitmap();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            SetInitialPosition();
        }

        private void InitializeContextMenu()
        {
            _overlayMenu = new ContextMenuStrip();
            _overlayMenu.Items.Add("Hide Overlay", null, (_, _) =>
            {
                OverlayHidden?.Invoke();
            });

            var sizeMenu = new ToolStripMenuItem("Size");
            sizeMenu.DropDownItems.Add(CreateSizeMenuItem("Small"));
            sizeMenu.DropDownItems.Add(CreateSizeMenuItem("Medium"));
            sizeMenu.DropDownItems.Add(CreateSizeMenuItem("Large"));
            _overlayMenu.Items.Add(sizeMenu);

            UpdateSizeCheckmarks();
        }

        private ToolStripMenuItem CreateSizeMenuItem(string size)
        {
            var item = new ToolStripMenuItem(size, null, (_, _) =>
            {
                ApplySize(size);
                SettingsManager.Current.OverlaySize = size;
                SettingsManager.Save();
                UpdateSizeCheckmarks();
                SizeChanged?.Invoke(size);
            });
            item.Tag = size;
            return item;
        }

        private void UpdateSizeCheckmarks()
        {
            if (_overlayMenu.Items.Count < 2) return;
            var sizeMenu = _overlayMenu.Items[1] as ToolStripMenuItem;
            if (sizeMenu == null) return;

            foreach (ToolStripMenuItem item in sizeMenu.DropDownItems)
            {
                item.Checked = item.Tag?.ToString() == SettingsManager.Current.OverlaySize;
            }
        }

        /// <summary>
        /// Polls the foreground window every 250ms and stores the handle
        /// of the last window that is NOT a ClipTyper window. This is used
        /// for focus restoration when the overlay is clicked.
        /// </summary>
        private void InitializeFocusTracker()
        {
            _focusTracker = new System.Windows.Forms.Timer { Interval = 250 };
            _focusTracker.Tick += (_, _) =>
            {
                var fg = NativeMethods.GetForegroundWindow();
                if (fg != IntPtr.Zero && fg != Handle)
                {
                    _lastForegroundWindow = fg;
                }
            };
            _focusTracker.Start();
        }

        private void InitializePeekTimer()
        {
            _peekTimer = new System.Windows.Forms.Timer { Interval = AnimationStepMs };
            _peekTimer.Tick += OnPeekTimerTick;
        }

        // ── Public methods ──────────────────────────────────────────

        /// <summary>
        /// Sets the overlay position. If coordinates are -1 (default),
        /// positions at the right edge of the primary screen, vertically centered.
        /// Validates that the position is within screen bounds.
        /// </summary>
        public void SetInitialPosition()
        {
            var settings = SettingsManager.Current;

            if (settings.OverlayX == -1 || settings.OverlayY == -1)
            {
                MoveToDefaultPosition();
                return;
            }

            // Validate stored position against current screen configuration
            var pos = new Point(settings.OverlayX, settings.OverlayY);
            bool isOnScreen = false;
            foreach (var screen in Screen.AllScreens)
            {
                // Check if the overlay center is within any screen's bounds
                var center = new Point(pos.X + Width / 2, pos.Y + Height / 2);
                if (screen.WorkingArea.Contains(center))
                {
                    isOnScreen = true;
                    break;
                }
            }

            if (isOnScreen)
            {
                Location = pos;
                CheckPeekMode();
            }
            else
            {
                MoveToDefaultPosition();
            }
        }

        /// <summary>
        /// Moves the overlay to the default position: right edge of the
        /// primary monitor, vertically centered, in peek mode.
        /// </summary>
        public void MoveToDefaultPosition()
        {
            var workArea = Screen.PrimaryScreen!.WorkingArea;
            int x = workArea.Right - (int)(Width * 0.3); // 30% visible
            int y = workArea.Top + (workArea.Height - Height) / 2;
            Location = new Point(x, y);
            SavePosition();
            CheckPeekMode();
        }

        /// <summary>
        /// Applies a size preset ("Small", "Medium", "Large").
        /// Preserves the overlay's anchor position so it stays visually
        /// in the same spot after resizing.
        /// </summary>
        public void ApplySize(string preset)
        {
            int formSize = preset switch
            {
                "Small"  => SizeSmall,
                "Large"  => SizeLarge,
                _        => SizeMedium
            };

            // Preserve anchor point before resizing
            int oldWidth = Width;
            int oldHeight = Height;
            bool hadSize = oldWidth > 0 && oldHeight > 0;
            int anchorRight = Right;
            int anchorLeft = Left;
            int centerX = Left + oldWidth / 2;
            int centerY = Top + oldHeight / 2;

            Size = new Size(formSize, formSize);
            UpdateIconImage();

            if (!_isInitialized || !hadSize) return; // First call during init, no reposition/saving needed

            // Reposition to keep the same visual anchor
            if (_isPeeking)
            {
                // At an edge — recalculate peek positions for new size
                CheckPeekMode();
            }
            else
            {
                // Free-floating — keep center position stable
                Left = centerX - Width / 2;
                Top = centerY - Height / 2;
            }

            SavePosition();
        }

        // ── Drag & Drop ─────────────────────────────────────────────

        private void OnIconMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _dragOffset = e.Location;
                _dragStartScreenPos = Cursor.Position;
                _hasDragged = false;
            }
            else if (e.Button == MouseButtons.Right)
            {
                _overlayMenu.Show(this, e.Location);
            }
        }

        private void OnIconMouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            Point currentScreenPos = Cursor.Position;
            if (!_hasDragged)
            {
                if (Math.Abs(currentScreenPos.X - _dragStartScreenPos.X) >= SystemInformation.DragSize.Width ||
                    Math.Abs(currentScreenPos.Y - _dragStartScreenPos.Y) >= SystemInformation.DragSize.Height)
                {
                    _hasDragged = true;
                }
            }

            if (_hasDragged)
            {
                var newPos = PointToScreen(e.Location);
                Location = new Point(newPos.X - _dragOffset.X, newPos.Y - _dragOffset.Y);
            }
        }

        private void OnIconMouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            bool wasClick = !_hasDragged;

            if (_isDragging)
            {
                _isDragging = false;
                if (_hasDragged)
                {
                    SnapToEdgeIfClose();
                    SavePosition();
                    CheckPeekMode();
                }
            }

            if (wasClick)
            {
                OnOverlayClicked();
            }
        }

        // ── Click → Trigger clip-type ───────────────────────────────

        private void OnOverlayClicked()
        {
            // Because WS_EX_NOACTIVATE is set, clicking the overlay does NOT
            // steal focus from the target window. The cursor stays in whatever
            // input field was active (password box, RDP session, etc.).
            //
            // As a safety net, if the foreground window changed for any reason,
            // we restore it explicitly.

            var targetWindow = _lastForegroundWindow;

            Task.Run(() =>
            {
                // Brief pause to let the mouse-up event finish
                Thread.Sleep(150);

                // Safety net: if focus somehow shifted, force it back
                var currentFg = NativeMethods.GetForegroundWindow();
                if (targetWindow != IntPtr.Zero && currentFg != targetWindow)
                {
                    NativeMethods.ForceForegroundWindow(targetWindow);
                    Thread.Sleep(300);
                }

                _triggerClipType(false);
            });
        }

        // ── Peek / Slide animation ──────────────────────────────────

        private void OnMouseEnterOverlay(object? sender, EventArgs e)
        {
            Opacity = OpacityHover;

            if (_isPeeking)
            {
                // Slide in from edge
                SlideToPosition(_peekVisibleX);
            }
        }

        private void OnMouseLeaveOverlay(object? sender, EventArgs e)
        {
            // Don't slide back while dragging or while context menu is open
            if (_isDragging || _overlayMenu.Visible) return;

            Opacity = OpacityNormal;

            if (_isPeeking)
            {
                // Slide back behind edge
                SlideToPosition(_peekHiddenX);
            }
        }

        private void SlideToPosition(int targetX)
        {
            _peekTarget = targetX;
            int distance = Math.Abs(targetX - Left);
            int steps = Math.Max(1, AnimationTotalMs / AnimationStepMs);
            _peekStep = Math.Max(1, distance / steps);

            _peekTimer.Start();
        }

        private void OnPeekTimerTick(object? sender, EventArgs e)
        {
            if (Math.Abs(Left - _peekTarget) <= _peekStep)
            {
                Left = _peekTarget;
                _peekTimer.Stop();
                return;
            }

            Left += Left < _peekTarget ? _peekStep : -_peekStep;
        }

        /// <summary>
        /// Checks if the overlay is at a screen edge and activates peek mode.
        /// In peek mode, only ~30% of the icon is visible; the rest is
        /// hidden behind the edge.
        /// </summary>
        private void CheckPeekMode()
        {
            _isPeeking = false;

            foreach (var screen in Screen.AllScreens)
            {
                var area = screen.WorkingArea;

                // Right edge
                if (Math.Abs(Right - area.Right) < EdgeThreshold ||
                    Left + Width > area.Right)
                {
                    _peekVisibleX = area.Right - Width;
                    _peekHiddenX = area.Right - (int)(Width * 0.3);
                    _isPeeking = true;
                    Left = _peekHiddenX;
                    return;
                }

                // Left edge
                if (Math.Abs(Left - area.Left) < EdgeThreshold ||
                    Left < area.Left)
                {
                    _peekVisibleX = area.Left;
                    _peekHiddenX = area.Left - (int)(Width * 0.7);
                    _isPeeking = true;
                    Left = _peekHiddenX;
                    return;
                }

                // Top edge
                if (Math.Abs(Top - area.Top) < EdgeThreshold)
                {
                    // For top/bottom we keep X but peek vertically
                    // (simplified: no vertical peek — just stay visible)
                }
            }

            // Not at an edge — fully visible
        }

        /// <summary>
        /// Snaps the overlay to a screen edge if it's close enough (within
        /// <see cref="SnapThreshold"/> pixels).
        /// </summary>
        private void SnapToEdgeIfClose()
        {
            foreach (var screen in Screen.AllScreens)
            {
                var area = screen.WorkingArea;

                // Right edge snap
                if (Math.Abs(Right - area.Right) < SnapThreshold)
                {
                    Left = area.Right - (int)(Width * 0.3);
                    return;
                }

                // Left edge snap
                if (Math.Abs(Left - area.Left) < SnapThreshold)
                {
                    Left = area.Left - (int)(Width * 0.7);
                    return;
                }

                // Top edge snap
                if (Math.Abs(Top - area.Top) < SnapThreshold)
                {
                    Top = area.Top;
                    return;
                }

                // Bottom edge snap
                if (Math.Abs(Bottom - area.Bottom) < SnapThreshold)
                {
                    Top = area.Bottom - Height;
                    return;
                }
            }
        }

        // ── Helpers ─────────────────────────────────────────────────

        private void SavePosition()
        {
            SettingsManager.Current.OverlayX = Left;
            SettingsManager.Current.OverlayY = Top;
            SettingsManager.Save();
        }

        /// <summary>
        /// Loads an embedded icon from assembly resources by logical name.
        /// </summary>
        private static Icon? LoadEmbeddedIcon(string resourceName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    return new Icon(stream);
                }
            }
            catch { /* fall through */ }

            return SystemIcons.Application;
        }

        // ── Cleanup ─────────────────────────────────────────────────

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _focusTracker?.Stop();
                _focusTracker?.Dispose();
                _peekTimer?.Stop();
                _peekTimer?.Dispose();
                _overlayIcon128?.Dispose();
                _overlayIcon256?.Dispose();
                _overlayMenu?.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Prevents the form from appearing in Alt+Tab and from stealing
        /// focus when clicked.
        /// WS_EX_TOOLWINDOW (0x80) — hides from Alt+Tab
        /// WS_EX_NOACTIVATE (0x08000000) — clicking does not activate the
        ///   window, so the target application keeps its focus and the
        ///   cursor stays in whatever input field was active.
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;       // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x08000000;       // WS_EX_NOACTIVATE
                return cp;
            }
        }

        // WM_MOUSEACTIVATE constants
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 0x0003;

        /// <summary>
        /// Intercepts WM_MOUSEACTIVATE to return MA_NOACTIVATE, which
        /// tells Windows not to activate this window when it is clicked.
        /// This is an additional safety layer on top of WS_EX_NOACTIVATE
        /// to ensure focus is never stolen from the target application.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }
    }
}
