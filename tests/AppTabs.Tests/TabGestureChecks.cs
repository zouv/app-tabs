using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using AppTabs.App;
using AppTabs.Core;

namespace AppTabs.Tests
{
    internal static partial class Program
    {
        // Synthetic messages on a self-owned, non-activating tool window only. No controller,
        // services, native discovery, attachment, keyboard hooks or user settings are involved.
        private static void TabGestureChecks()
        {
            var sessions = Enumerable.Range(1, 8).Select(GestureSession).ToArray();
            using (var host = new QuietForm { Text = "AppTabs synthetic tab gestures", ShowInTaskbar = false,
                Location = new Point(-24000, -24000), ClientSize = new Size(680, 100) })
            using (var bar = new TabBar { Size = new Size(680, 36) })
            {
                host.Controls.Add(bar); host.Show(); Application.DoEvents();
                bar.Bind(sessions, sessions[0]);
                int selected = 0, detached = 0, moved = 0, index = -1, chrome = 0;
                WindowSession? chosen = null;
                bar.SelectTab += s => { selected++; chosen = s; };
                bar.DetachTab += s => { detached++; chosen = s; };
                bar.ReorderTab += (s, i) => { moved++; chosen = s; index = i; };
                bar.ChromeMouseDown += _ => chrome++;
                var first = GestureRects(bar)[0];
                Point body = new Point(first.Left + 45, first.Top + first.Height / 2);
                Point cross = new Point(first.Right - 15, body.Y);
                TabMouse(bar, "OnMouseDown", MouseButtons.Left, body);
                Check(selected == 0 && detached == 0 && moved == 0, "tab down has no activation or mutation");
                bar.Bind(sessions.ToArray(), sessions[0]);
                Check(bar.Capture && GestureField<WindowSession?>(bar, "pressed") == sessions[0], "periodic same-structure Bind preserves press");
                TabMouse(bar, "OnMouseUp", MouseButtons.Left, body);
                Check(selected == 1 && chosen == sessions[0] && !bar.Capture, "same-body mouse up selects once and releases capture");

                TabMouse(bar, "OnMouseMove", MouseButtons.None, cross);
                Check(GestureField<bool>(bar, "closeHovered"), "cross hover tracks its own hit region");
                TabMouse(bar, "OnMouseDown", MouseButtons.Left, cross);
                Check(detached == 0 && selected == 1, "cross down does not detach or select");
                TabMouse(bar, "OnMouseUp", MouseButtons.Left, body);
                Check(detached == 0 && selected == 1, "cross press released on body cannot become a selection");
                TabMouse(bar, "OnMouseDown", MouseButtons.Left, cross);
                TabMouse(bar, "OnMouseUp", MouseButtons.Left, cross);
                Check(detached == 1 && selected == 1, "cross detaches only on same-cross mouse up");
                TabMouse(bar, "OnMouseDown", MouseButtons.Middle, body);
                Check(detached == 1, "middle down does not detach");
                TabMouse(bar, "OnMouseUp", MouseButtons.Middle, body);
                Check(detached == 2 && selected == 1, "middle same-tab mouse up detaches without selection");
                TabMouse(bar, "OnMouseDown", MouseButtons.Left, body);
                TabMouse(bar, "OnMouseUp", MouseButtons.Left, cross);
                Check(detached == 2 && selected == 1, "body press released on cross does neither action");
                TabMouse(bar, "OnMouseDown", MouseButtons.Left, body);
                TabMouse(bar, "OnMouseUp", MouseButtons.Middle, body);
                Check(selected == 1 && detached == 2 && !bar.Capture, "wrong release button cancels gesture");

                int threshold = Math.Max(1, SystemInformation.DragSize.Width) / 2;
                Point tiny = new Point(body.X + threshold, body.Y);
                TabMouse(bar, "OnMouseDown", MouseButtons.Left, body);
                TabMouse(bar, "OnMouseMove", MouseButtons.Left, tiny);
                Check(!GestureField<bool>(bar, "dragging"), "policy threshold is not a drag until exceeded");
                TabMouse(bar, "OnMouseUp", MouseButtons.Left, tiny);
                Check(selected == 2 && moved == 0, "small movement remains a selection");
                Point drop = new Point(410, body.Y);
                TabMouse(bar, "OnMouseDown", MouseButtons.Left, body);
                TabMouse(bar, "OnMouseMove", MouseButtons.Left, drop);
                Check(GestureField<bool>(bar, "dragging") && selected == 2, "drag starts without activating its source");
                bar.Bind(sessions, sessions[0]);
                int expected = TabManagementPolicy.InsertionIndex(GestureRects(bar).Skip(1).Select(r => r.Left + r.Width / 2), drop.X);
                TabMouse(bar, "OnMouseUp", MouseButtons.Left, drop);
                Check(moved == 1 && index == expected && chosen == sessions[0] && selected == 2, "drag publishes policy insertion index, never activation");
                Check(!GestureField<Timer>(bar, "scroll").Enabled && !bar.Capture, "completed drag stops timer and capture");

                Action beginDrag = () =>
                {
                    bar.Bind(Array.Empty<WindowSession>(), null); bar.Bind(sessions, sessions[0]);
                    var r = GestureRects(bar)[0]; body = new Point(r.Left + 45, r.Top + r.Height / 2);
                    TabMouse(bar, "OnMouseDown", MouseButtons.Left, body);
                    TabMouse(bar, "OnMouseMove", MouseButtons.Left, new Point(body.X + threshold + 10, body.Y));
                };
                beginDrag();
                TabMouse(bar, "OnMouseMove", MouseButtons.Left, new Point(200, -1));
                TabMouse(bar, "OnMouseUp", MouseButtons.Left, body);
                Check(moved == 1 && selected == 2 && !bar.Capture, "leaving bar vertically cancels drag and later release");
                beginDrag();
                TabMouse(bar, "OnMouseMove", MouseButtons.Left, new Point(bar.Width + 1, body.Y));
                TabMouse(bar, "OnMouseUp", MouseButtons.Left, body);
                Check(moved == 1 && selected == 2, "leaving bar horizontally cancels drag");
                beginDrag();
                var add = GestureProperty<Rectangle>(bar, "AddRect");
                TabMouse(bar, "OnMouseUp", MouseButtons.Left, new Point(add.Left + add.Width / 2, body.Y));
                Check(moved == 1 && selected == 2, "dropping over plus is not a reorder target");
                beginDrag();
                var escape = Message.Create(bar.Handle, 0x100, new IntPtr((int)Keys.Escape), IntPtr.Zero);
                Check(bar.PreFilterMessage(ref escape), "gesture-scoped filter consumes Escape without keyboard hooks");
                TabMouse(bar, "OnMouseUp", MouseButtons.Left, body);
                Check(moved == 1 && selected == 2 && !bar.Capture && !GestureField<bool>(bar, "filteringKeys"), "Escape cancels gesture and removes filter");
                beginDrag(); bar.Capture = false;
                TabMouse(bar, "OnMouseUp", MouseButtons.Left, body);
                Check(moved == 1 && selected == 2 && !GestureField<bool>(bar, "dragging"), "capture loss cannot turn into later select or move");
                beginDrag();
                typeof(TabBar).GetMethod("OnMouseLeave", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bar, new object[] { EventArgs.Empty });
                TabMouse(bar, "OnMouseUp", MouseButtons.Left, body);
                Check(moved == 1 && selected == 2, "mouse leave notification cancels pointer state");

                beginDrag();
                string originalTitle = sessions[0].Title;
                sessions[0].Title = "合成标题定时刷新";
                bar.Bind(sessions, sessions[1]);
                Check(bar.Capture && GestureField<bool>(bar, "dragging"), "title and active-state refresh preserve gesture in the same collection");
                sessions[0].Title = originalTitle;
                var titleCancel = Message.Create(bar.Handle, 0x100, new IntPtr((int)Keys.Escape), IntPtr.Zero); bar.PreFilterMessage(ref titleCancel);
                beginDrag();
                var mutable = sessions.ToList();
                bar.Bind(mutable, sessions[0]);
                mutable.Reverse(); bar.Bind(mutable, sessions[0]);
                Check(!bar.Capture && !GestureField<bool>(bar, "dragging"), "in-place reordered bound collection cancels via snapshot comparison");
                TabMouse(bar, "OnMouseUp", MouseButtons.Left, body);
                Check(moved == 1 && selected == 2, "structural cancellation publishes nothing");
                beginDrag();
                typeof(WindowSession).GetProperty("State")!.SetValue(sessions[0], SessionState.Lost, null);
                bar.Bind(sessions, sessions[0]);
                TabMouse(bar, "OnMouseUp", MouseButtons.Left, body);
                Check(moved == 1 && selected == 2 && !bar.Capture, "lost source in same collection cancels on Bind");
                typeof(WindowSession).GetProperty("State")!.SetValue(sessions[0], SessionState.Attached, null);

                beginDrag();
                var viewport = GestureProperty<Rectangle>(bar, "Viewport");
                Point edge = new Point(viewport.Right - 2, body.Y);
                TabMouse(bar, "OnMouseMove", MouseButtons.Left, edge);
                var timer = GestureField<Timer>(bar, "scroll");
                Check(timer.Interval == 60 && timer.Enabled, "drag edge scroll uses 60ms local timer");
                int before = GestureField<int>(bar, "offset");
                GestureTick(bar);
                int after = GestureField<int>(bar, "offset");
                Check(after > before, "right edge tick advances overflow offset");
                bar.Bind(sessions.ToArray(), sessions[0]);
                Check(GestureField<int>(bar, "offset") == after && bar.Capture, "timer Bind neither cancels drag nor reveals old active tab");
                GestureTick(bar);
                Check(GestureField<int>(bar, "offset") > after, "stationary edge pointer continues scrolling on another tick");
                expected = TabManagementPolicy.InsertionIndex(GestureRects(bar).Skip(1).Select(r => r.Left + r.Width / 2), edge.X);
                Check(GestureField<int>(bar, "insertion") == expected, "edge scrolling recalculates insertion using updated geometry");
                int rightOffset = GestureField<int>(bar, "offset");
                TabMouse(bar, "OnMouseMove", MouseButtons.Left, new Point(2, body.Y)); GestureTick(bar);
                Check(GestureField<int>(bar, "offset") < rightOffset, "left edge reverses drag scrolling");
                var cancel = Message.Create(bar.Handle, 0x100, new IntPtr((int)Keys.Escape), IntPtr.Zero); bar.PreFilterMessage(ref cancel);
                Check(!timer.Enabled, "cancel stops edge-scroll timer");

                bar.Bind(Array.Empty<WindowSession>(), null); bar.Bind(sessions, sessions[0]);
                first = GestureRects(bar)[0];
                TabMouse(bar, "OnMouseDown", MouseButtons.Left, new Point(first.Left + 45, 1));
                Check(chrome == 1 && !bar.Capture, "normal top four pixels remain resize chrome");
                host.WindowState = FormWindowState.Maximized; Application.DoEvents();
                TabMouse(bar, "OnMouseDown", MouseButtons.Left, new Point(first.Left + 45, 1));
                Check(chrome == 1 && selected == 2 && bar.Capture, "maximized top edge starts tab press, not chrome or activation");
                TabMouse(bar, "OnMouseUp", MouseButtons.Left, new Point(first.Left + 45, 1));
                Check(selected == 3, "maximized top edge can select on release");
                host.WindowState = FormWindowState.Normal; host.Location = new Point(-24000, -24000);
                bar.Bind(sessions, sessions[sessions.Length - 1]);
                Check(GestureField<int>(bar, "offset") > 0, "external active selection reveals overflowed tab");
                using (var bitmap = new Bitmap(bar.Width, bar.Height))
                {
                    bar.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    add = GestureProperty<Rectangle>(bar, "AddRect");
                    Color plus = bitmap.GetPixel(add.Left + add.Width / 2, add.Top + add.Height / 2);
                    Check(plus.B > plus.R && plus.B > plus.G, "clipped single-line text cannot overwrite overflow plus");
                }
                bar.Bind(Array.Empty<WindowSession>(), null); bar.Bind(sessions, sessions[0]);
                first = GestureRects(bar)[0]; body = new Point(first.Left + 45, first.Top + first.Height / 2);
                SendMessage(bar.Handle, 0x201, new IntPtr(1), PackedPoint(body));
                Check(selected == 3, "native synthetic left down also waits for release");
                SendMessage(bar.Handle, 0x202, IntPtr.Zero, PackedPoint(body));
                Check(selected == 4 && !bar.Capture, "native managed capture release order preserves valid mouse up");
                host.Hide(); host.Close();
            }
        }
        private static WindowSession GestureSession(int number)
        {
            var identity = Identity(600 + number);
            identity.ImagePath = "C:\\AppTabs-synthetic-no-icon\\sample.exe";
            return (WindowSession)typeof(WindowSession).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(WindowSnapshot) }, null)!.Invoke(new object[] { Snapshot(identity) });
        }
        private static Rectangle[] GestureRects(TabBar bar)
        {
            var value = (System.Collections.IEnumerable)typeof(TabBar).GetMethod("Rectangles", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bar, null)!;
            return value.Cast<object>().Select(entry => (Rectangle)entry.GetType().GetField("Item2")!.GetValue(entry)!).ToArray();
        }
        private static T GestureField<T>(TabBar bar, string name) =>
            (T)typeof(TabBar).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bar)!;
        private static T GestureProperty<T>(TabBar bar, string name) =>
            (T)typeof(TabBar).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bar)!;
        private static void GestureTick(TabBar bar) =>
            typeof(TabBar).GetMethod("AutoScrollDrag", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bar, null);
    }
}
