using System;
using AppTabs.Core;

namespace AppTabs.App
{
    public static class SettingsCommit
    {
        // Disk and native hotkey registration cannot form one transaction. Compensate explicitly.
        public static void Save(AppState previous, AppState candidate, Action<AppState> save, Action<int>? applyHotkey, bool hotkeyAvailable = true)
        {
            bool changed = applyHotkey != null && (previous.HotkeyChoice != candidate.HotkeyChoice || (!hotkeyAvailable && candidate.HotkeyChoice != 3));
            try
            {
                if (changed) applyHotkey!(candidate.HotkeyChoice);
                save(candidate);
            }
            catch (Exception error)
            {
                if (changed)
                    try { applyHotkey!(previous.HotkeyChoice); }
                    catch (Exception rollback)
                    {
                        throw new InvalidOperationException("设置未提交，全局快捷键当前不可用。\n" + error.Message + "\n恢复旧快捷键失败：" + rollback.Message,
                            new AggregateException(error, rollback));
                    }
                throw;
            }
        }
    }
}
