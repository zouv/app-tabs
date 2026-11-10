using System;
using System.Collections.Generic;
using System.IO;
using AppTabs.App;
using AppTabs.Core;

namespace AppTabs.Tests
{
    internal static partial class Program
    {
        private static void SettingsChecks()
        {
            var previous = new AppState { HotkeyChoice = 0, Preference = AttachPreference.Auto };
            var candidate = previous.Copy(); candidate.HotkeyChoice = 1; candidate.Preference = AttachPreference.Grouped;
            var events = new List<string>();
            SettingsCommit.Save(previous, candidate, _ => events.Add("save"), choice => events.Add("key" + choice));
            Check(string.Join(",", events) == "key1,save" && previous.Preference == AttachPreference.Auto, "settings commit publishes only after caller saves");
            events.Clear();
            Throws(() => SettingsCommit.Save(previous, candidate, _ => { events.Add("save"); throw new IOException("disk"); }, choice => events.Add("key" + choice)), "settings disk failure propagates");
            Check(string.Join(",", events) == "key1,save,key0", "settings disk failure restores old hotkey");
            events.Clear();
            Throws(() => SettingsCommit.Save(previous, candidate, _ => events.Add("save"), choice => { events.Add("key" + choice); if (choice == 1) throw new Exception("occupied"); }), "settings new key failure propagates");
            Check(string.Join(",", events) == "key1,key0", "registration failure restores key without saving");
            string message = "";
            try { SettingsCommit.Save(previous, candidate, _ => throw new IOException("disk"), _ => throw new Exception("keys unavailable")); }
            catch (InvalidOperationException error) { message = error.Message; }
            Check(message.Contains("当前不可用") && message.Contains("设置未提交"), "settings rollback failure reports unavailable hotkey honestly");
            candidate.HotkeyChoice = previous.HotkeyChoice; events.Clear();
            SettingsCommit.Save(previous, candidate, _ => events.Add("save"), _ => events.Add("key"));
            Check(string.Join(",", events) == "save", "unchanged hotkey is not unregistered");
            events.Clear();
            SettingsCommit.Save(previous, candidate, _ => events.Add("save"), choice => events.Add("key" + choice), false);
            Check(string.Join(",", events) == "key0,save", "unchanged but unavailable hotkey retries registration");
            candidate.HotkeyChoice = 3; previous.HotkeyChoice = 3; events.Clear();
            SettingsCommit.Save(previous, candidate, _ => events.Add("save"), _ => events.Add("key"), false);
            Check(string.Join(",", events) == "save", "disabled hotkey needs no retry registration");
        }
    }
}
