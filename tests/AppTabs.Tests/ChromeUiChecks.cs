using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AppTabs.App;
using AppTabs.Core;

namespace AppTabs.Tests
{
    internal static partial class Program
    {
        private static void ChromeChecks()
        {
            string output = Path.Combine(ProjectRoot(), "artifacts"); Directory.CreateDirectory(output);
            var api = new FakeApi();
            var chromeStore = new StateStore(Path.Combine(testDirectory, "chrome-settings.json"));
            using (var ui = new MainForm(api, new RecoveryJournal(Path.Combine(testDirectory, "chrome-recovery.json")),
                chromeStore, new AppState(), false))
            {
                ui.Location = new Point(-24000, -24000);
                ui.Show(); Application.DoEvents(); var handle = ui.Handle;
                Check((GetWindowLongPtr(handle, -16).ToInt64() & 0x00C00000) == 0, "chrome removes native caption");
                Check((GetWindowLongPtr(handle, -16).ToInt64() & 0x00040000) != 0, "chrome retains native resizing frame");
                int expectedTopBand = (int)Math.Max(1, Math.Round(3 * ui.DeviceDpi / 96.0));
                Check(ui.Padding.Top == expectedTopBand && ui.Padding.Left == 0 && ui.Padding.Right == 0 && ui.Padding.Bottom == 0,
                    "top resize band is a small painted strip");
                Check(ui.Width - ui.ClientSize.Width > 0 && ui.Height - ui.ClientSize.Height > 0, "native side / bottom resize frame remains");
                var bar = ui.Controls.Find("tabs", true).OfType<TabBar>().Single();
                Check(bar.Parent != ui && bar.Dock == DockStyle.Fill, "tabs share title row with caption controls");
                var welcome = ui.Controls.Find("content", true).Single();
                int welcomeInvalidations = 0;
                welcome.Invalidated += (_, __) => welcomeInvalidations++;
                ui.Size = new Size(940, 700); ui.PerformLayout(); Application.DoEvents();
                Check(welcomeInvalidations > 0, "empty welcome content repaints after host resize");
                foreach (int width in new[] { 780, 1120, 1600 })
                {
                    ui.Size = new Size(width, 760); ui.PerformLayout();
                    ui.Location = new Point(-24000, -24000);
                    Check(bar.Parent!.Height < 60, "compact title row at " + width);
                    Check(bar.Left > 0 && bar.Right < bar.Parent.ClientSize.Width, "brand and caption reserve space at " + width);
                    var captions = ui.Controls.Find("captionButtons", true).Single();
                    var brand = ui.Controls.Find("brand", true).Single();
                    Check(bar.Left >= brand.Right && bar.Right <= captions.Left, "tabs do not overlap caption buttons at " + width + ": tabs=" + bar.Bounds + ", brand=" + brand.Bounds + ", captions=" + captions.Bounds);
                    Check(ui.ContentBounds.Width > 0 && ui.ContentBounds.Height > 0, "content remains usable at " + width);
                    var toolbar = ui.Controls.Find("toolbar", true).Single();
                    var status = ui.Controls.Find("status", true).Single();
                    Check(status.Parent == toolbar, "status shares the toolbar row at " + width);
                    Check(welcome.Bottom == ui.ClientSize.Height, "content reaches the bottom without a status strip at " + width);
                    Check(toolbar.Top >= bar.Parent.Bottom && toolbar.Bottom <= ui.Controls.Cast<Control>().Single(c => c.Handle == ui.ContentHandle).Top, "toolbar between title and content at " + width);
                    foreach (var control in Descendants(toolbar).Where(c => c.Width > 0 && c.Height > 0))
                        Check(control.Left >= 0 && control.Right <= control.Parent!.ClientSize.Width && control.Top >= 0 && control.Bottom <= control.Parent.ClientSize.Height,
                            "toolbar child fits: " + control.Name + " at " + width);
                    SavePreview(ui, output, "app-tabs-empty-" + width + ".png");
                    var point = ui.PointToScreen(new Point(bar.Parent.Left + bar.Left + 60, 1));
                    Check(SendMessage(ui.Handle, 0x84, IntPtr.Zero, PackedPoint(point)).ToInt32() == 12, "top frame hit testing at " + width);
                }
                var nativeEdges = new[]
                {
                    (new Point(-1, ui.ClientSize.Height / 2), 10), (new Point(ui.ClientSize.Width + 1, ui.ClientSize.Height / 2), 11),
                    (new Point(ui.ClientSize.Width / 2, ui.ClientSize.Height + 1), 15),
                    (new Point(-1, 1), 13), (new Point(ui.ClientSize.Width + 1, 1), 14),
                    (new Point(-1, ui.ClientSize.Height + 1), 16), (new Point(ui.ClientSize.Width + 1, ui.ClientSize.Height + 1), 17)
                };
                foreach (var edge in nativeEdges)
                    Check(SendMessage(handle, 0x84, IntPtr.Zero, PackedPoint(ui.PointToScreen(edge.Item1))).ToInt32() == edge.Item2,
                        "native side / corner resizing hit " + edge.Item2);
                var controller = new HostController(new FakeApi(), new FakeStore());
                var sessions = Enumerable.Range(1, 12).Select(i => controller.Attach(Identity(i), ui.ContentHandle, HostingMode.Embedded, ui.ContentBounds)).ToArray();
                foreach (var session in sessions) controller.RenameTab(session, "应用 " + session.Identity.Handle + " · 很长的中文窗口标题与工作资料");
                bar.Bind(sessions, sessions[0]);
                ui.Size = new Size(780, 760); ui.PerformLayout();
                SavePreview(ui, output, "app-tabs-overflow.png");
                using (var bitmap = new Bitmap(bar.Width, bar.Height))
                {
                    bar.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    var plus = (Rectangle)typeof(TabBar).GetProperty("AddRect", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bar)!;
                    var center = bitmap.GetPixel(plus.Left + plus.Width / 2, plus.Top + plus.Height / 2);
                    Check(center.B > center.R && center.B > center.G, "overflow plus remains visibly blue after drawing tab text");
                    bitmap.Save(Path.Combine(output, "app-tabs-tabbar-overflow.png"));
                }
                var rect = FirstTabRect(bar); int selected = 0, detached = 0, added = 0, chrome = 0;
                var eventNames = new[] { "SelectTab", "DetachTab", "AddWindow", "ChromeMouseDown" };
                var eventFields = eventNames.Select(name => typeof(TabBar).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!).ToArray();
                var savedHandlers = eventFields.Select(field => field.GetValue(bar)).ToArray();
                foreach (var field in eventFields) field.SetValue(bar, null);
                bar.SelectTab += _ => selected++; bar.DetachTab += _ => detached++; bar.AddWindow += () => added++;
                // Replace native chrome and application handlers for synthetic input tests only.
                bar.ChromeMouseDown += _ => chrome++;
                try
                {
                    TabMouse(bar, "OnMouseDown", MouseButtons.Left, new Point(rect.Left + 45, rect.Top + rect.Height / 2));
                    TabMouse(bar, "OnMouseUp", MouseButtons.Left, new Point(rect.Left + 45, rect.Top + rect.Height / 2));
                    Check(selected == 1 && chrome == 0, "tab selection does not start window drag");
                    TabMouse(bar, "OnMouseDown", MouseButtons.Left, new Point(rect.Right - 15, rect.Top + rect.Height / 2));
                    TabMouse(bar, "OnMouseUp", MouseButtons.Left, new Point(rect.Right - 15, rect.Top + rect.Height / 2));
                    Check(detached == 1 && chrome == 0, "tab cross detaches without window drag");
                    TabMouse(bar, "OnMouseDown", MouseButtons.Middle, new Point(rect.Left + 45, rect.Top + rect.Height / 2));
                    TabMouse(bar, "OnMouseUp", MouseButtons.Middle, new Point(rect.Left + 45, rect.Top + rect.Height / 2));
                    Check(detached == 2 && chrome == 0, "middle click detaches without window drag");
                    var add = (Rectangle)typeof(TabBar).GetProperty("AddRect", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bar)!;
                    Check(add.Right <= bar.Width && add.Bottom <= bar.Height, "plus fits visible tabbar: add=" + add + ", size=" + bar.Size);
                    TabMouse(bar, "OnMouseDown", MouseButtons.Left, new Point(add.Left + add.Width / 2, add.Top + add.Height / 2));
                    Check(added == 1 && chrome == 0, "plus only requests joining existing windows");
                    TabMouse(bar, "OnMouseDown", MouseButtons.Left, new Point(rect.Left + 45, 1));
                    Check(chrome == 1 && selected == 1, "top resize band wins over tab hit");
                    bar.Bind(Array.Empty<WindowSession>(), null);
                    TabMouse(bar, "OnMouseDown", MouseButtons.Left, new Point(bar.Width / 2, bar.Height / 2));
                    Check(chrome == 2, "empty title space dispatches chrome input");
                }
                finally { for (int i = 0; i < eventFields.Length; i++) eventFields[i].SetValue(bar, savedHandlers[i]); }
                bar.Bind(sessions, sessions[sessions.Length - 1]);
                int offset = (int)typeof(TabBar).GetField("offset", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bar)!;
                Check(offset > 0, "selecting overflowed tab scrolls it into view");
                Check(ui.Handle == handle, "chrome layout does not recreate host handle");
                ui.JoinWindow(Identity(902), AttachPreference.Grouped);
                var joined = chromeStore.Read();
                Check(joined.RecentApps.Any(app => app.ImagePath == Identity(902).ImagePath && app.ClassName == Identity(902).ClassName),
                    "join records recent app");
                int moveMutations = api.Mutations;
                ui.Left += 10;
                Check(api.Mutations > moveMutations, "move lays out the active window immediately");
                int layoutMutations = api.Mutations;
                ui.WindowState = FormWindowState.Maximized;
                Application.DoEvents();
                Check(api.Mutations > layoutMutations, "maximize lays out the active window before the coalescing timer");
                var area = Screen.FromHandle(ui.Handle).WorkingArea;
                Check(ui.PointToScreen(Point.Empty) == area.Location && ui.ClientSize == area.Size, "maximized client fits monitor working area: origin=" + ui.PointToScreen(Point.Empty) + ", size=" + ui.ClientSize + ", area=" + area + ", bounds=" + ui.Bounds);
                Check(ui.Controls.Find("chrome", true).Single().PointToScreen(Point.Empty).Y == area.Top, "maximized tabs are not under native top border");
                Check(ui.Padding.Top == 0, "maximized window removes the top resize band");
                int restoreMutations = api.Mutations;
                ui.WindowState = FormWindowState.Normal;
                Application.DoEvents();
                Check(api.Mutations > restoreMutations, "restore lays out the active window before the coalescing timer");
                Check(ui.Padding.Top == expectedTopBand, "restored window restores the top resize band");
                ui.Location = new Point(-24000, -24000); ui.Hide(); Application.DoEvents();
                typeof(MainForm).GetMethod("EnterChromeMenuScope", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ui, null);
                typeof(MainForm).GetField("pendingRaise", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(ui, true);
                typeof(MainForm).GetMethod("LeaveChromeMenuScope", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ui, null);
                Check(!(bool)typeof(MainForm).GetField("pendingRaise", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui)!, "closing background menu does not raise grouped windows");
                Check(ui.Handle == handle, "maximize restore keeps host handle");
                ui.Close();
            }
            IconChecks();
        }
        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        private static Rectangle FirstTabRect(TabBar bar)
        {
            var rectangles = (System.Collections.IEnumerable)typeof(TabBar).GetMethod("Rectangles", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bar, null)!;
            var first = rectangles.Cast<object>().First();
            return (Rectangle)first.GetType().GetField("Item2")!.GetValue(first)!;
        }
        private static void TabMouse(TabBar bar, string method, MouseButtons button, Point point) =>
            typeof(TabBar).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bar, new object[] { new MouseEventArgs(button, 1, point.X, point.Y, 0) });
        private static void SavePreview(Form ui, string directory, string name)
        {
            using (var bitmap = new Bitmap(ui.Width, ui.Height)) { ui.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(directory, name)); }
        }
        private static void IconChecks()
        {
            using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("AppTabs.App.app-tabs.ico"))
            {
                Check(stream != null, "application icon is embedded independently of test executable");
                using (var reader = new BinaryReader(stream!))
                {
                    Check(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1 && reader.ReadUInt16() == 7, "icon has seven frames");
                    foreach (int size in new[] { 16, 24, 32, 48, 64, 128, 256 })
                    {
                        int width = reader.ReadByte(), height = reader.ReadByte(); reader.ReadByte(); reader.ReadByte(); reader.ReadUInt16();
                        int depth = reader.ReadUInt16(); reader.ReadUInt32(); reader.ReadUInt32();
                        Check((width == 0 ? 256 : width) == size && (height == 0 ? 256 : height) == size && depth == 32, "32-bit icon size " + size);
                    }
                }
            }
            foreach (int size in new[] { 16, 24, 32, 48, 64, 128, 256 })
                using (var icon = AppIcon.Load(size))
                using (var bitmap = icon.ToBitmap())
                    Check(bitmap.Width == size && bitmap.Height == size, "embedded icon loads at " + size);
        }
        private static IntPtr PackedPoint(Point point) => new IntPtr(unchecked((int)(((uint)point.Y & 0xffff) << 16 | ((uint)point.X & 0xffff))));
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    }
}
