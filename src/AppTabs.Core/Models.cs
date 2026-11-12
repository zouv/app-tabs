using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Serialization;

namespace AppTabs.Core
{
    public enum HostingMode { Grouped, Embedded }
    public enum SessionState { Attached, RecoveryRequired, RestoredPendingJournal, Lost }

    [DataContract]
    public sealed class WindowIdentity
    {
        [DataMember] public long Handle { get; set; }
        [DataMember] public int ProcessId { get; set; }
        [DataMember] public uint ThreadId { get; set; }
        [DataMember] public long ProcessStartedUtcTicks { get; set; }
        [DataMember] public string ImagePath { get; set; } = "";
        [DataMember] public string ClassName { get; set; } = "";
        [DataMember] public string Title { get; set; } = "";
        public IntPtr Hwnd => new IntPtr(Handle);
        public override string ToString() => $"{Title}  [PID {ProcessId}, HWND 0x{Handle:X}]";
    }

    [DataContract]
    public struct NativePoint
    {
        [DataMember] public int X;
        [DataMember] public int Y;
    }

    [DataContract]
    public struct NativeRect
    {
        [DataMember] public int Left;
        [DataMember] public int Top;
        [DataMember] public int Right;
        [DataMember] public int Bottom;
        public Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    // WINDOWPLACEMENT uses workspace coordinates. Never reinterpret these as screen coordinates.
    [DataContract]
    public struct WindowPlacement
    {
        [DataMember] public uint Length;
        [DataMember] public uint Flags;
        [DataMember] public uint ShowCommand;
        [DataMember] public NativePoint MinimumPosition;
        [DataMember] public NativePoint MaximumPosition;
        [DataMember] public NativeRect NormalPosition;
    }

    [DataContract]
    public sealed class WindowSnapshot
    {
        [DataMember] public WindowIdentity Identity { get; set; } = new WindowIdentity();
        [DataMember] public long Parent { get; set; }
        [DataMember] public long Owner { get; set; }
        [DataMember] public long Style { get; set; }
        [DataMember] public long ExtendedStyle { get; set; }
        [DataMember] public bool WasVisible { get; set; }
        [DataMember] public WindowPlacement Placement { get; set; }
        [DataMember] public long HostHandle { get; set; }
        [DataMember] public HostingMode Mode { get; set; }
        [DataMember] public bool FrameStripped { get; set; }
    }

    public sealed class WindowSession
    {
        public WindowSnapshot Snapshot { get; }
        public SessionState State { get; internal set; }
        public string LastError { get; internal set; } = "";
        public string Title { get; set; }
        public DateTime? CloseRequestedUtc { get; internal set; }
        // Host-only label. Never changes the native window caption, directory or recovery identity.
        public string? CustomTitle { get; internal set; }
        public string DisplayTitle => CustomTitle ?? Title;
        public WindowIdentity Identity => Snapshot.Identity;
        internal WindowSession(WindowSnapshot snapshot)
        {
            Snapshot = snapshot;
            Title = snapshot.Identity.Title;
            State = SessionState.Attached;
        }
    }

    public interface IWindowApi
    {
        bool Matches(WindowIdentity identity);
        WindowSnapshot Capture(WindowIdentity identity);
        void ValidateAttach(WindowSnapshot snapshot, IntPtr host, HostingMode mode);
        void Attach(WindowSnapshot snapshot, IntPtr host, Rectangle bounds);
        void Layout(WindowSnapshot snapshot, Rectangle bounds);
        void Show(WindowSnapshot snapshot, bool visible, bool raise);
        void Restore(WindowSnapshot snapshot);
    }

    public interface ISnapshotStore
    {
        void Save(IReadOnlyCollection<WindowSnapshot> snapshots);
    }
}
