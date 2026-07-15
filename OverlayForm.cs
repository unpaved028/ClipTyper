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
        // ── Size & Scale Constants ──────────────────────────────────
        private const int BaseSize         = 128;  // Base size in pixels (100% scale)
        private const int SizeLarge        = 256;  // Size threshold for loading high-res icon

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

        // Peek animation & clipping
        private System.Windows.Forms.Timer _peekTimer = null!;
        private int _peekTarget;       // target coordinate for slide animation
        private int _peekStep;         // pixels per timer tick
        private bool _isPeeking;       // true when parked at edge in peek mode
        
        private enum PeekEdge
        {
            None,
            Left,
            Right,
            Top,
            Bottom
        }
        private PeekEdge _currentPeekEdge = PeekEdge.None;
        
        private int _peekHiddenX;      // X position when hidden behind edge
        private int _peekVisibleX;     // X position when fully visible
        private int _peekHiddenY;      // Y position when hidden behind edge
        private int _peekVisibleY;     // Y position when fully visible

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
        /// Raised when the user changes the overlay scale from the context menu.
        /// </summary>
        public event Action<int>? ScaleChanged;

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
            ApplyScale(SettingsManager.Current.OverlayScalePercent);
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
            Size = new Size(BaseSize, BaseSize);
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
            var oldImage = _iconBox.Image;
            var icon = Width >= SizeLarge ? _overlayIcon256 : _overlayIcon128;
            _iconBox.Image = icon?.ToBitmap();
            oldImage?.Dispose();
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

            var monitorMenu = new ToolStripMenuItem("Monitor");
            monitorMenu.DropDownOpening += (s, e) => PopulateMonitorMenu(monitorMenu);
            _overlayMenu.Items.Add(monitorMenu);

            var scaleMenu = new ToolStripMenuItem("Scale");
            scaleMenu.DropDownOpening += (s, e) => PopulateScaleMenu(scaleMenu);
            _overlayMenu.Items.Add(scaleMenu);
        }

        private void PopulateMonitorMenu(ToolStripMenuItem monitorMenu)
        {
            monitorMenu.DropDownItems.Clear();
            var screens = Screen.AllScreens;
            var currentScreen = Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2));
            int currentScreenIndex = Array.IndexOf(screens, currentScreen);

            for (int i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                string name = $"Monitor {i + 1}" + (screen.Primary ? " (Primary)" : "");
                int index = i;
                var item = new ToolStripMenuItem(name, null, (_, _) =>
                {
                    MoveToMonitor(index);
                });
                item.Checked = (index == currentScreenIndex);
                monitorMenu.DropDownItems.Add(item);
            }
        }

        private void PopulateScaleMenu(ToolStripMenuItem scaleMenu)
        {
            scaleMenu.DropDownItems.Clear();
            int currentScale = SettingsManager.Current.OverlayScalePercent;

            int[] presets = { 50, 75, 100, 150, 200 };
            foreach (int preset in presets)
            {
                string label = $"{preset}%";
                if (preset == 50) label += " (Small)";
                else if (preset == 100) label += " (Medium)";
                else if (preset == 200) label += " (Large)";

                var item = new ToolStripMenuItem(label, null, (_, _) =>
                {
                    SettingsManager.Current.OverlayScalePercent = preset;
                    ApplyScale(preset);
                    SettingsManager.Save();
                    ScaleChanged?.Invoke(preset);
                });
                item.Checked = (currentScale == preset);
                scaleMenu.DropDownItems.Add(item);
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
        /// positions at the right edge of the configured screen, vertically centered.
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
        /// configured monitor, vertically centered, in peek mode.
        /// </summary>
        public void MoveToDefaultPosition()
        {
            var screens = Screen.AllScreens;
            int index = SettingsManager.Current.OverlayMonitorIndex;
            if (index < 0 || index >= screens.Length)
            {
                index = 0;
            }
            var targetScreen = screens[index];
            var workArea = targetScreen.WorkingArea;
            int x = workArea.Right - (int)(Width * 0.3); // 30% visible
            int y = workArea.Top + (workArea.Height - Height) / 2;
            Location = new Point(x, y);
            SavePosition();
            CheckPeekMode();
        }

        /// <summary>
        /// Repositions the overlay to the selected monitor index,
        /// preserving its relative vertical and horizontal position.
        /// </summary>
        public void MoveToMonitor(int index)
        {
            var screens = Screen.AllScreens;
            if (index < 0 || index >= screens.Length)
            {
                index = 0;
            }
            var targetScreen = screens[index];
            var currentScreen = Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2));

            double relX = (double)(Left + Width / 2 - currentScreen.WorkingArea.Left) / currentScreen.WorkingArea.Width;
            double relY = (double)(Top + Height / 2 - currentScreen.WorkingArea.Top) / currentScreen.WorkingArea.Height;

            int newCenterX = targetScreen.WorkingArea.Left + (int)(relX * targetScreen.WorkingArea.Width);
            int newCenterY = targetScreen.WorkingArea.Top + (int)(relY * targetScreen.WorkingArea.Height);

            Location = new Point(newCenterX - Width / 2, newCenterY - Height / 2);
            SettingsManager.Current.OverlayMonitorIndex = index;
            SavePosition();
            CheckPeekMode();
        }

        /// <summary>
        /// Applies a scale percentage.
        /// Preserves the overlay's anchor position so it stays visually
        /// in the same spot after resizing.
        /// </summary>
        public void ApplyScale(int scalePercent)
        {
            int formSize = (BaseSize * scalePercent) / 100;
            formSize = Math.Clamp(formSize, 32, 512);

            // Preserve anchor point before resizing
            int oldWidth = Width;
            int oldHeight = Height;
            bool hadSize = oldWidth > 0 && oldHeight > 0;
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
                if (_currentPeekEdge == PeekEdge.Left || _currentPeekEdge == PeekEdge.Right)
                {
                    SlideToPosition(_peekVisibleX);
                }
                else if (_currentPeekEdge == PeekEdge.Top || _currentPeekEdge == PeekEdge.Bottom)
                {
                    SlideToPosition(_peekVisibleY);
                }
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
                if (_currentPeekEdge == PeekEdge.Left || _currentPeekEdge == PeekEdge.Right)
                {
                    SlideToPosition(_peekHiddenX);
                }
                else if (_currentPeekEdge == PeekEdge.Top || _currentPeekEdge == PeekEdge.Bottom)
                {
                    SlideToPosition(_peekHiddenY);
                }
            }
        }

        private void SlideToPosition(int target)
        {
            _peekTarget = target;
            int current = (_currentPeekEdge == PeekEdge.Left || _currentPeekEdge == PeekEdge.Right) ? Left : Top;
            int distance = Math.Abs(target - current);
            int steps = Math.Max(1, AnimationTotalMs / AnimationStepMs);
            _peekStep = Math.Max(1, distance / steps);

            _peekTimer.Start();
        }

        private void OnPeekTimerTick(object? sender, EventArgs e)
        {
            if (_currentPeekEdge == PeekEdge.Left || _currentPeekEdge == PeekEdge.Right)
            {
                if (Math.Abs(Left - _peekTarget) <= _peekStep)
                {
                    Left = _peekTarget;
                    _peekTimer.Stop();
                    return;
                }
                Left += Left < _peekTarget ? _peekStep : -_peekStep;
            }
            else if (_currentPeekEdge == PeekEdge.Top || _currentPeekEdge == PeekEdge.Bottom)
            {
                if (Math.Abs(Top - _peekTarget) <= _peekStep)
                {
                    Top = _peekTarget;
                    _peekTimer.Stop();
                    return;
                }
                Top += Top < _peekTarget ? _peekStep : -_peekStep;
            }
        }

        /// <summary>
        /// Checks if the overlay is at a screen edge and activates peek mode.
        /// In peek mode, only ~30% of the icon is visible; the rest is
        /// hidden behind the edge.
        /// </summary>
        private void CheckPeekMode()
        {
            _isPeeking = false;
            _currentPeekEdge = PeekEdge.None;

            var screen = Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2));
            var area = screen.WorkingArea;

            // Right edge
            if (Math.Abs(Right - area.Right) < EdgeThreshold || Left + Width > area.Right)
            {
                _currentPeekEdge = PeekEdge.Right;
                _peekVisibleX = area.Right - Width;
                _peekHiddenX = area.Right - (int)(Width * 0.3);
                _isPeeking = true;
                Left = _peekHiddenX;
                return;
            }

            // Left edge
            if (Math.Abs(Left - area.Left) < EdgeThreshold || Left < area.Left)
            {
                _currentPeekEdge = PeekEdge.Left;
                _peekVisibleX = area.Left;
                _peekHiddenX = area.Left - (int)(Width * 0.7);
                _isPeeking = true;
                Left = _peekHiddenX;
                return;
            }

            // Top edge
            if (Math.Abs(Top - area.Top) < EdgeThreshold || Top < area.Top)
            {
                _currentPeekEdge = PeekEdge.Top;
                _peekVisibleY = area.Top;
                _peekHiddenY = area.Top - (int)(Height * 0.7);
                _isPeeking = true;
                Top = _peekHiddenY;
                return;
            }

            // Bottom edge
            if (Math.Abs(Bottom - area.Bottom) < EdgeThreshold || Top + Height > area.Bottom)
            {
                _currentPeekEdge = PeekEdge.Bottom;
                _peekVisibleY = area.Bottom - Height;
                _peekHiddenY = area.Bottom - (int)(Height * 0.3);
                _isPeeking = true;
                Top = _peekHiddenY;
                return;
            }
        }

        /// <summary>
        /// Snaps the overlay to a screen edge if it's close enough (within
        /// <see cref="SnapThreshold"/> pixels).
        /// </summary>
        private void SnapToEdgeIfClose()
        {
            var screen = Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2));
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
                Top = area.Top - (int)(Height * 0.7);
                return;
            }

            // Bottom edge snap
            if (Math.Abs(Bottom - area.Bottom) < SnapThreshold)
            {
                Top = area.Bottom - (int)(Height * 0.3);
                return;
            }
        }

        // ── Helpers ─────────────────────────────────────────────────

        private void SavePosition()
        {
            SettingsManager.Current.OverlayX = Left;
            SettingsManager.Current.OverlayY = Top;

            // Sync current monitor index based on center position
            var currentScreen = Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2));
            int index = Array.IndexOf(Screen.AllScreens, currentScreen);
            if (index >= 0)
            {
                SettingsManager.Current.OverlayMonitorIndex = index;
            }
            SettingsManager.Save();
        }

        /// <summary>
        /// Updates the form's clipping region to prevent bleeding onto adjacent monitors.
        /// </summary>
        private void UpdateClippingRegion()
        {
            if (!_isPeeking)
            {
                Region = null;
                return;
            }

            var screen = Screen.FromPoint(new Point(Left + Width / 2, Top + Height / 2));
            var area = screen.WorkingArea;

            Rectangle formBounds = Bounds;
            Rectangle intersection = Rectangle.Intersect(formBounds, area);

            if (intersection.Width <= 0 || intersection.Height <= 0)
            {
                Region = new Region(Rectangle.Empty);
            }
            else if (intersection == formBounds)
            {
                Region = null;
            }
            else
            {
                int localX = intersection.Left - Left;
                int localY = intersection.Top - Top;
                Region = new Region(new Rectangle(localX, localY, intersection.Width, intersection.Height));
            }

            // Re-apply TopMost because changing the Region can reset it
            TopMost = true;
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            UpdateClippingRegion();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateClippingRegion();
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
                cp.ExStyle |= 0x00000008;       // WS_EX_TOPMOST
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
