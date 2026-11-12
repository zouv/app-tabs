using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using AppTabs.App;
using AppTabs.Core;

namespace AppTabs.Tests
{
    internal static partial class Program
    {
        // The main test entry invokes this. Every dialog uses services=false; picker data is injected.
        private static void DialogUiChecks()
        {
            SettingsUiChecks(); PickerUiChecks(); SmallDialogChecks(); DialogDpiPreviews();
        }
        private static Form NewDialog(string type, params object[] args) => (Form)typeof(WindowPicker).Assembly
            .GetType("AppTabs.App." + type, true)!.GetConstructors().Single(c => c.GetParameters().Length == args.Length).Invoke(args);
        private static void ShowTestDialog(Form dialog)
        {
            dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-24000, -24000);
            IntPtr foreground = NativeUiForeground(); dialog.Show(); Application.DoEvents();
            IntPtr after = NativeUiForeground();
            // The user may legitimately change the foreground while tests pump messages.
            // Never restore that window; verify only that this test-owned dialog did not take it.
            Check(after != dialog.Handle && (after == IntPtr.Zero || !dialog.Contains(Control.FromHandle(after))),
                "test dialog does not activate: " + dialog.Text + ": before=" + foreground + ", after=" + after + ", dialog=" + dialog.Handle);
            Check(dialog.Location.X < -10000, "dialog stays offscreen: " + dialog.Text);
            Check(dialog.FormBorderStyle == FormBorderStyle.None && !dialog.ShowInTaskbar, "dialog has custom chrome without taskbar entry");
            long extendedStyle = GetWindowLongPtr(dialog.Handle, -20).ToInt64();
            Check((extendedStyle & 0x08000080) == 0x08000080, "test dialog native style enforces no activation and tool window");
            Check(dialog.Padding == new Padding(1), "dialog ring is one physical pixel");
            Check(dialog.Region != null && !dialog.Region.IsVisible(0, 0), "outer rounded region clips the corner");
            var title = dialog.Controls.Find("dialogTitleBar", true).Single();
            var body = dialog.Controls.Find("dialogBody", true).Single();
            Check(title.Region != null && body.Region != null, "title and body have inner rounded clips");
            Check(title.Top == 1 && body.Bottom == dialog.ClientSize.Height - 1, "children do not cover solid ring");
            Check(body.Top >= title.Bottom, "dialog title and content do not overlap");
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
            {
                dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                int x = bitmap.Width / 2;
                Check(bitmap.GetPixel(x, 0).ToArgb() == dialog.BackColor.ToArgb(), "solid ring reaches top straight edge");
                Check(bitmap.GetPixel(x, 1).ToArgb() != dialog.BackColor.ToArgb(), "straight border has no second pixel");
            }
        }
        private static void SettingsUiChecks()
        {
            var state = new AppState { Preference = AttachPreference.Grouped, HotkeyChoice = 2, HideToTray = true,
                RecentJoinLimit = 2, BorderlessGrouped = true };
            using (var dialog = NewDialog("SettingsDialog", state, false))
            {
                ShowTestDialog(dialog);
                var preference = dialog.Controls.Find("preference", true).OfType<ComboBox>().Single();
                var hotkey = dialog.Controls.Find("hotkeyChoice", true).OfType<ComboBox>().Single();
                Check(preference.Items.Count == 3 && preference.SelectedIndex == 1, "settings has three existing attach preferences");
                Check(hotkey.Items.Count == 4 && hotkey.SelectedIndex == 2, "settings limits global hotkey to four existing choices");
                var recent = dialog.Controls.Find("recentJoinLimit", true).OfType<NumericUpDown>().Single();
                var borderless = dialog.Controls.Find("borderlessGrouped", true).OfType<CheckBox>().Single();
                Check(recent.Value == 2 && borderless.Checked, "settings exposes recent app limit and borderless grouped");
                Check(dialog.Controls.Find("settingsNavigation", true).Single().Controls.Count == 2, "settings side navigation has exactly two pages");
                Check(dialog.Controls.Find("generalPage", true).Single().Visible && !dialog.Controls.Find("hotkeyPage", true).Single().Visible, "general page opens first");
                Check(dialog.Controls.Find("generalPage", true).Single().Controls.Find("hideToTray", true).Length == 0 &&
                    dialog.Controls.Find("hotkeyPage", true).Single().Controls.Find("hideToTray", true).Length == 1, "tray option belongs to hotkey page");
                Check(Descendants(dialog.Controls.Find("hotkeyPage", true).Single()).Any(c => c.Text.Contains("默认不勾选")), "tray option explains the default minimize behavior");
                var handle = dialog.Handle;
                ((Button)dialog.Controls.Find("hotkeyNav", true).Single()).PerformClick(); Application.DoEvents();
                Check(!dialog.Controls.Find("generalPage", true).Single().Visible && dialog.Controls.Find("hotkeyPage", true).Single().Visible, "hotkey navigation selects second page");
                var table = dialog.Controls.Find("fixedBindings", true).OfType<TableLayoutPanel>().Single();
                string fixedText = string.Join(" ", table.Controls.Cast<Control>().Select(c => c.Text));
                Check(table.RowCount == 6 && fixedText.Contains("PageDown") && fixedText.Contains("PageUp") && fixedText.Contains("Ctrl + W") && fixedText.Contains("Ctrl + F"), "fixed shortcut reference includes KeyboardHook bindings");
                Check(!Descendants(dialog).Any(c => c.Text.Contains("多行")), "settings does not add multiline tab preferences");
                preference.SelectedIndex = 2; hotkey.SelectedIndex = 3;
                ((CheckBox)dialog.Controls.Find("hideToTray", true).Single()).Checked = false;
                recent.Value = 7; borderless.Checked = false;
                var type = dialog.GetType();
                Check((AttachPreference)type.GetProperty("Preference")!.GetValue(dialog)! == AttachPreference.Embedded &&
                    (int)type.GetProperty("HotkeyChoice")!.GetValue(dialog)! == 3 && !(bool)type.GetProperty("HideToTray")!.GetValue(dialog)! &&
                    (int)type.GetProperty("RecentJoinLimit")!.GetValue(dialog)! == 7 &&
                    !(bool)type.GetProperty("BorderlessGrouped")!.GetValue(dialog)!, "settings exposes edits for host-owned save and registration");
                Check(state.Preference == AttachPreference.Grouped && state.HotkeyChoice == 2 && state.HideToTray &&
                    state.RecentJoinLimit == 2 && state.BorderlessGrouped == true, "settings edits do not mutate state before host accepts");
                Check(dialog.Handle == handle, "page changes preserve dialog HWND");
                foreach (Control child in Descendants(table)) Check(child.Right <= child.Parent!.ClientSize.Width && child.Bottom <= child.Parent.ClientSize.Height, "fixed shortcut row fits page");
                dialog.Close();
            }
        }
        private static void PickerUiChecks()
        {
            var first = Identity(701); first.ImagePath = "C:\\sample\\Alpha.exe"; first.Title = "First document";
            var second = Identity(702); second.ImagePath = "C:\\sample\\Beta.exe"; second.Title = "Second document";
            int loads = 0; bool fail = false;
            Func<IReadOnlyList<WindowIdentity>> loader = () => { Interlocked.Increment(ref loads); if (fail) throw new InvalidOperationException("injected read failure"); return new[] { second, first }; };
            using (var dialog = new WindowPicker(Array.Empty<long>(), loader, false))
            {
                ShowTestDialog(dialog);
                var list = dialog.Controls.Find("windowCandidates", true).OfType<ListView>().Single();
                WaitDialog(() => list.Items.Count == 2, "injected picker load");
                Check(loads == 1, "Shown uses injected candidates instead of enumerating user windows");
                Check(list.Columns[0].Text == "软件" && list.Columns[1].Text == "窗口标题", "picker application column comes first");
                Check(list.Items[0].Text == "Alpha" && list.Items[0].SubItems[1].Text == first.Title, "picker row data agrees with reordered columns");
                list.Items[0].Checked = true;
                var query = dialog.Controls.Find("windowSearch", true).OfType<TextBox>().Single();
                query.Text = "Beta"; Check(list.Items.Count == 1, "picker filters application names");
                query.Text = "First"; Check(list.Items.Count == 1 && list.Items[0].Checked, "filter preserves hidden checked candidates");
                query.Text = "";
                var reload = Descendants(dialog).OfType<Button>().Single(b => b.Text == "刷新");
                reload.PerformClick(); WaitDialog(() => loads == 2 && reload.Enabled, "picker asynchronous refresh");
                Check(list.Items[0].Checked, "refresh preserves surviving checked handles");
                fail = true; reload.PerformClick(); WaitDialog(() => loads == 3 && reload.Enabled, "picker query failure");
                Check(list.Items.Count == 2 && list.Items[0].Checked, "query failure retains candidates and checked state as unknown");
                var handle = dialog.Handle;
                dialog.Size = new Size(100, 100); Application.DoEvents();
                Check(dialog.Width >= dialog.MinimumSize.Width && dialog.Height >= dialog.MinimumSize.Height, "picker enforces minimum size");
                dialog.Size = new Size(920, 620); Application.DoEvents();
                Point[] points = { new Point(1, 100), new Point(dialog.ClientSize.Width - 2, 100), new Point(100, 1), new Point(100, dialog.ClientSize.Height - 2),
                    new Point(1, 1), new Point(dialog.ClientSize.Width - 2, 1), new Point(1, dialog.ClientSize.Height - 2), new Point(dialog.ClientSize.Width - 2, dialog.ClientSize.Height - 2) };
                int[] hits = { 10, 11, 12, 15, 13, 14, 16, 17 };
                for (int i = 0; i < points.Length; i++)
                    Check(SendMessage(dialog.Handle, 0x84, IntPtr.Zero, PackedPoint(dialog.PointToScreen(points[i]))).ToInt32() == hits[i], "picker resize edge/corner " + hits[i]);
                var title = dialog.Controls.Find("dialogTitle", true).Single();
                Point edge = title.PointToScreen(new Point(title.Width / 2, 0));
                Check(SendMessage(title.Handle, 0x84, IntPtr.Zero, PackedPoint(edge)).ToInt32() == -1, "child title passes resize band through to dialog");
                Check(dialog.Handle == handle, "picker resize preserves its HWND");
                var join = Descendants(dialog).OfType<Button>().Single(b => b.Text == "加入选中的窗口");
                // Query failure deliberately disables joining; a successful refresh re-enables it.
                fail = false; reload.PerformClick(); WaitDialog(() => loads == 4 && reload.Enabled, "picker recovery refresh");
                join.PerformClick();
                Check(dialog.DialogResult == DialogResult.OK && dialog.SelectedWindows.Count == 1 && dialog.SelectedWindows[0] == first, "picker returns selected identity without native candidate operations");
            }
        }
        private static void SmallDialogChecks()
        {
            using (var dialog = NewDialog("TextDialog", "改名", "输入标签名称", "示例", false)) { ShowTestDialog(dialog); dialog.Close(); }
            using (var dialog = NewDialog("TabSearchDialog", Array.Empty<WindowSession>(), false)) { ShowTestDialog(dialog); dialog.Close(); }
            using (var dialog = NewDialog("AboutDialog", false))
            {
                ShowTestDialog(dialog);
                var logo = dialog.Controls.Find("aboutLogo", true).OfType<PictureBox>().Single();
                Check(logo.Image != null && logo.Image.Width == 64, "about uses owned embedded AppIcon bitmap");
                Check(dialog.Controls.Find("aboutVersion", true).Single().Text.Contains(typeof(WindowPicker).Assembly.GetName().Version!.ToString(3)), "about shows assembly version");
                Check(dialog.Controls.Find("aboutVersion", true).Single().Text.Contains("作者 @zouv"), "about shows author");
                dialog.Close(); dialog.Dispose(); Check(logo.Image == null, "about releases logo resource on disposal");
            }
        }
        private static void DialogDpiPreviews()
        {
            string output = Path.Combine(ProjectRoot(), "artifacts"); Directory.CreateDirectory(output);
            var state = new AppState { Preference = AttachPreference.Auto, HotkeyChoice = 0, HideToTray = true };
            var alpha = Identity(801); alpha.ImagePath = "C:\\sample\\Alpha.exe"; alpha.Title = "项目计划 — 自建候选窗口";
            var beta = Identity(802); beta.ImagePath = "C:\\sample\\Beta.exe"; beta.Title = "文档编辑器 — 自建候选窗口";
            // No monitor moves, WM_DPICHANGED, or real candidate enumeration: replay managed layout
            // at 96/144/192 DPI on independent test-owned dialogs. These are synthetic DPI renders,
            // not evidence of real cross-monitor DPI/native interaction compatibility.
            foreach (int dpi in new[] { 96, 144, 192 })
            {
                using (var settings = NewDialog("SettingsDialog", state, false))
                {
                    PrepareDialogDpiPreview(settings, dpi);
                    SavePreview(settings, output, "app-tabs-settings-general-" + dpi + "dpi.png");
                    ((Button)settings.Controls.Find("hotkeyNav", true).Single()).PerformClick(); Application.DoEvents();
                    SavePreview(settings, output, "app-tabs-settings-hotkeys-" + dpi + "dpi.png"); settings.Close();
                }
                using (var about = NewDialog("AboutDialog", false))
                {
                    PrepareDialogDpiPreview(about, dpi); SavePreview(about, output, "app-tabs-about-" + dpi + "dpi.png"); about.Close();
                }
                using (var picker = new WindowPicker(Array.Empty<long>(), () => new[] { alpha, beta }, false))
                {
                    PrepareDialogDpiPreview(picker, dpi);
                    var list = picker.Controls.Find("windowCandidates", true).OfType<ListView>().Single();
                    WaitDialog(() => list.Items.Count == 2, "DPI preview uses injected candidates at " + dpi);
                    list.Items[0].Checked = true;
                    SavePreview(picker, output, "app-tabs-window-picker-" + dpi + "dpi.png"); picker.Close();
                }
            }
        }
        private static void PrepareDialogDpiPreview(Form dialog, int dpi)
        {
            ShowTestDialog(dialog);
            var controls = new[] { (Control)dialog }.Concat(Descendants(dialog)).ToArray();
            int originalDpi = dialog.DeviceDpi;
            var dpiField = typeof(Control).GetField("deviceDpi", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Framework 4.8 deviceDpi test field is unavailable.");
            // Framework's DeviceDpi getter ignores the per-control field when the hosting process
            // has not enabled the DPI-message quirk (notably PowerShell and this net48 test exe).
            // Scope that getter switch to this independent preview; restore it on dialog disposal.
            var dpiHelper = typeof(Control).Assembly.GetType("System.Windows.Forms.DpiHelper", true)!;
            var perControlDpi = dpiHelper.GetField("enableDpiChangedMessageHandling", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Framework 4.8 per-control DPI test switch is unavailable.");
            object originalSwitch = perControlDpi.GetValue(null)!;
            IntPtr originalContext = SetThreadDpiAwarenessContext(new IntPtr(-4));
            if (originalContext == IntPtr.Zero) throw new InvalidOperationException("Cannot enter test-owned PerMonitorV2 layout scope.");
            perControlDpi.SetValue(null, true);
            dialog.Disposed += (_, __) => { perControlDpi.SetValue(null, originalSwitch); SetThreadDpiAwarenessContext(originalContext); };
            var logicalFonts = controls.ToDictionary(c => c, c => c.Font);
            var ownedFonts = new List<Font>();
            dialog.SuspendLayout();
            try
            {
                foreach (Control control in controls) dpiField.SetValue(control, dpi);
                float scale = dpi / (float)originalDpi;
                dialog.Scale(new SizeF(scale, scale));
                foreach (Control control in controls)
                {
                    var font = logicalFonts[control];
                    var previewFont = new Font(font.FontFamily, font.SizeInPoints * dpi / 72F, font.Style, GraphicsUnit.Pixel);
                    ownedFonts.Add(previewFont); control.Font = previewFont;
                }
                // ListView columns are native sub-items, not child Controls; scale them explicitly.
                foreach (var list in controls.OfType<ListView>())
                    foreach (ColumnHeader column in list.Columns) column.Width = (int)Math.Round(column.Width * scale);
            }
            finally { dialog.ResumeLayout(true); }
            dialog.Disposed += (_, __) => { foreach (var font in ownedFonts) font.Dispose(); };
            foreach (Control control in controls) control.PerformLayout();
            dialog.PerformLayout(); Application.DoEvents();
            dialog.Location = new Point(-24000, -24000);
            var title = dialog.Controls.Find("dialogTitleBar", true).Single();
            var close = dialog.Controls.Find("dialogClose", true).Single();
            Check(dialog.Padding == new Padding(1) && title.Height == (int)Math.Round(38 * dpi / 96F), "DPI render retains physical ring and scaled title at " + dpi + ": dpi=" + dialog.DeviceDpi + ", titleDpi=" + title.DeviceDpi + ", padding=" + dialog.Padding + ", titleHeight=" + title.Height);
            Check(close.Width == (int)Math.Round(44 * dpi / 96F), "DPI render scales close target at " + dpi);
            Check(title.Region != null && dialog.Controls.Find("dialogBody", true).Single().Region != null, "DPI render retains child corner clips at " + dpi);
        }
        private static void WaitDialog(Func<bool> ready, string message)
        {
            var timer = Stopwatch.StartNew();
            while (!ready() && timer.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(10); }
            Check(ready(), message);
        }
        private static IntPtr NativeUiForeground() => GetDialogForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
        private static extern IntPtr GetDialogForegroundWindow();
    }
}
