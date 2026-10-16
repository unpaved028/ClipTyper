using System.Runtime.ExceptionServices;
using ClipTyper;
using Xunit;

namespace ClipTyper.Tests
{
    [Collection("SettingsState")]
    public class TypingModeMenuTest
    {
        [Fact]
        public void VkModeItem_TogglesSetting_AndShowsCheckMark()
        {
            WithTempSettings(() =>
            {
                SettingsManager.Current.EnableVkCompatibilityMode = false;

                RunOnSta(() =>
                {
                    using var menu = new ContextMenuStrip();
                    var quickSwitch = TypingModeMenu.AddTo(menu);
                    Assert.False(quickSwitch.VkModeItem.Checked);

                    quickSwitch.VkModeItem.PerformClick();
                    Assert.True(SettingsManager.Current.EnableVkCompatibilityMode);
                    Assert.True(quickSwitch.VkModeItem.Checked);

                    quickSwitch.VkModeItem.PerformClick();
                    Assert.False(SettingsManager.Current.EnableVkCompatibilityMode);
                    Assert.False(quickSwitch.VkModeItem.Checked);
                });

                Assert.Contains("\"EnableVkCompatibilityMode\": false", File.ReadAllText(SettingsManager.SettingsFile), StringComparison.Ordinal);
            });
        }

        [Fact]
        public void SelectLayout_SpecificLayout_TurnsVkModeOn_AndUpdatesCaption()
        {
            WithTempSettings(() =>
            {
                SettingsManager.Current.EnableVkCompatibilityMode = false;
                SettingsManager.Current.TargetKeyboardLayout = KeyboardLayouts.SameAsThisPc;

                RunOnSta(() =>
                {
                    using var menu = new ContextMenuStrip();
                    var quickSwitch = TypingModeMenu.AddTo(menu);
                    Assert.Equal("Keyboard layout (Same as this PC)", quickSwitch.LayoutItem.Text);

                    quickSwitch.SelectLayout("00000409");

                    Assert.Equal("00000409", SettingsManager.Current.TargetKeyboardLayout);
                    Assert.True(SettingsManager.Current.EnableVkCompatibilityMode);
                    Assert.True(quickSwitch.VkModeItem.Checked);
                    Assert.Equal("Keyboard layout (English (United States))", quickSwitch.LayoutItem.Text);
                });

                string saved = File.ReadAllText(SettingsManager.SettingsFile);
                Assert.Contains("\"TargetKeyboardLayout\": \"00000409\"", saved, StringComparison.Ordinal);
            });
        }

        [Fact]
        public void SelectLayout_SameAsThisPc_LeavesVkModeAsItIs()
        {
            WithTempSettings(() =>
            {
                SettingsManager.Current.EnableVkCompatibilityMode = false;
                SettingsManager.Current.TargetKeyboardLayout = "00000407";

                RunOnSta(() =>
                {
                    using var menu = new ContextMenuStrip();
                    TypingModeMenu.AddTo(menu).SelectLayout(KeyboardLayouts.SameAsThisPc);
                });

                Assert.Equal(KeyboardLayouts.SameAsThisPc, SettingsManager.Current.TargetKeyboardLayout);
                Assert.False(SettingsManager.Current.EnableVkCompatibilityMode);
            });
        }

        [Fact]
        public void LayoutSubmenu_ListsAllLayouts_ChecksCurrent_AndKeepsCustomKlid()
        {
            WithTempSettings(() =>
            {
                SettingsManager.Current.TargetKeyboardLayout = "00001009"; // valid, not in the curated list

                RunOnSta(() =>
                {
                    using var menu = new ContextMenuStrip();
                    var quickSwitch = TypingModeMenu.AddTo(menu);

                    quickSwitch.LayoutItem.ShowDropDown();
                    var items = quickSwitch.LayoutItem.DropDownItems.OfType<ToolStripMenuItem>().ToList();
                    quickSwitch.LayoutItem.HideDropDown();

                    Assert.Equal(KeyboardLayouts.All.Count + 1, items.Count);
                    Assert.Equal("Same as this PC", items[0].Text);
                    var custom = Assert.Single(items, i => i.Checked);
                    Assert.Equal("Keyboard layout 00001009", custom.Text);
                });
            });
        }

        private static void WithTempSettings(Action action)
        {
            var before = SettingsManager.Current.Clone();
            string tempDir = Path.Combine(Path.GetTempPath(), "ClipTyper_Test_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                SettingsManager.SetSettingsFileForTesting(Path.Combine(tempDir, "settings.json"));
                action();
            }
            finally
            {
                SettingsManager.SetSettingsFileForTesting(null);
                SettingsManager.Current.CopyFrom(before);
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static void RunOnSta(Action action)
        {
            Exception? caught = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (caught != null)
            {
                ExceptionDispatchInfo.Capture(caught).Throw();
            }
        }
    }
}
