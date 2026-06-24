using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipTyper
{
    public class GlobalHotkey : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public const int WM_HOTKEY = 0x0312;

        [Flags]
        public enum Modifiers
        {
            None = 0x0000,
            Alt = 0x0001,
            Control = 0x0002,
            Shift = 0x0004,
            Win = 0x0008
        }

        private readonly IntPtr _hWnd;
        private readonly int _id;
        private bool _isRegistered;

        /// <summary>
        /// The currently registered modifier combination.
        /// </summary>
        public Modifiers CurrentModifier { get; private set; }

        /// <summary>
        /// The currently registered key.
        /// </summary>
        public Keys CurrentKey { get; private set; }

        public GlobalHotkey(IntPtr hWnd, int id, Modifiers modifier, Keys key)
        {
            _hWnd = hWnd;
            _id = id;
            Register(modifier, key);
        }

        private bool Register(Modifiers modifier, Keys key)
        {
            _isRegistered = RegisterHotKey(_hWnd, _id, (uint)modifier, (uint)key);
            if (_isRegistered)
            {
                CurrentModifier = modifier;
                CurrentKey = key;
            }
            return _isRegistered;
        }

        /// <summary>
        /// Unregisters the current hotkey and registers a new one.
        /// If registration of the new hotkey fails, the old hotkey
        /// is restored automatically.
        /// </summary>
        /// <returns>True if the new hotkey was registered successfully.</returns>
        public bool Reregister(Modifiers newModifier, Keys newKey)
        {
            var oldModifier = CurrentModifier;
            var oldKey = CurrentKey;

            Unregister();

            if (Register(newModifier, newKey))
            {
                return true;
            }

            // Registration failed — restore previous hotkey
            Register(oldModifier, oldKey);
            return false;
        }

        public void Unregister()
        {
            if (_isRegistered)
            {
                UnregisterHotKey(_hWnd, _id);
                _isRegistered = false;
            }
        }

        public void Dispose()
        {
            Unregister();
            GC.SuppressFinalize(this);
        }

        ~GlobalHotkey()
        {
            Unregister();
        }
    }
}
