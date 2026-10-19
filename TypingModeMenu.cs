using System;
using System.Linq;
using System.Windows.Forms;

namespace ClipTyper
{
    /// <summary>
    /// Quick switch for VK Compatibility Mode and the target keyboard layout, shared by the tray
    /// menu and the overlay menu (the overlay is the only menu reachable in fullscreen RDP / KVM).
    /// Changes are saved immediately and apply to the next typing run.
    /// </summary>
    internal sealed class TypingModeMenu
    {
        public ToolStripMenuItem VkModeItem { get; }
        public ToolStripMenuItem LayoutItem { get; }

        private TypingModeMenu()
        {
            VkModeItem = new ToolStripMenuItem("VK Compatibility Mode");
            VkModeItem.Click += (_, _) =>
            {
                var settings = SettingsManager.Current;
                settings.EnableVkCompatibilityMode = !settings.EnableVkCompatibilityMode;
                SettingsManager.Save();
                Refresh();
            };

            LayoutItem = new ToolStripMenuItem("Keyboard layout");
            // Dummy child so the submenu arrow is shown before the first population
            LayoutItem.DropDownItems.Add(new ToolStripMenuItem("..."));
            LayoutItem.DropDownOpening += (_, _) => PopulateLayouts();
        }

        /// <summary>
        /// Adds the two items to <paramref name="menu"/> and keeps their check marks current whenever it opens.
        /// </summary>
        public static TypingModeMenu AddTo(ContextMenuStrip menu)
        {
            var quickSwitch = new TypingModeMenu();
            menu.Items.Add(quickSwitch.VkModeItem);
            menu.Items.Add(quickSwitch.LayoutItem);
            menu.Opening += (_, _) => quickSwitch.Refresh();
            quickSwitch.Refresh();
            return quickSwitch;
        }

        /// <summary>
        /// Updates the check mark and shows the active layout in the submenu caption.
        /// </summary>
        public void Refresh()
        {
            var settings = SettingsManager.Current;
            VkModeItem.Checked = settings.EnableVkCompatibilityMode;
            LayoutItem.Text = $"Keyboard layout: {KeyboardLayouts.GetDisplayName(settings.TargetKeyboardLayout)}";
        }

        private void PopulateLayouts()
        {
            string current = SettingsManager.Current.TargetKeyboardLayout;
            LayoutItem.DropDownItems.Clear();

            var options = KeyboardLayouts.All.ToList();
            if (!options.Any(o => string.Equals(o.Klid, current, StringComparison.OrdinalIgnoreCase)))
            {
                // Valid KLID outside the curated list (edited in settings.json): keep it visible
                options.Add(new KeyboardLayoutOption(current, KeyboardLayouts.GetDisplayName(current)));
            }

            foreach (var option in options)
            {
                var item = new ToolStripMenuItem(option.DisplayName)
                {
                    Checked = string.Equals(option.Klid, current, StringComparison.OrdinalIgnoreCase)
                };
                string klid = option.Klid;
                item.Click += (_, _) => SelectLayout(klid);
                LayoutItem.DropDownItems.Add(item);

                if (option.Klid == KeyboardLayouts.SameAsThisPc)
                {
                    LayoutItem.DropDownItems.Add(new ToolStripSeparator());
                }
            }
        }

        /// <summary>
        /// Selects a target layout. A specific layout only has an effect in VK mode, so choosing one
        /// also turns VK Compatibility Mode on; "Same as this PC" leaves the mode as it is.
        /// </summary>
        internal void SelectLayout(string klid)
        {
            var settings = SettingsManager.Current;
            settings.TargetKeyboardLayout = klid;
            if (klid != KeyboardLayouts.SameAsThisPc)
            {
                settings.EnableVkCompatibilityMode = true;
            }
            SettingsManager.Save();
            Refresh();
        }
    }
}
