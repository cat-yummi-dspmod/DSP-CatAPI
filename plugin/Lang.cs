using System.Globalization;

namespace YummiHarness
{
    /// <summary>
    /// 语言选择：游戏语言已加载时用游戏语言；还没加载（mod 刚 Awake 时通常如此）就退回系统语言。
    /// Language choice: use the game language once it is loaded; before that (usually the case right after a mod's Awake) fall back to the OS language.
    /// 因此"补丁时刻"写的那一行日志语言是尽力而为；主菜单弹窗和详细日志一定使用游戏语言。
    /// So the language of the log line written "at patch time" is best effort; the main-menu dialog and detailed log always use the game language.
    /// </summary>
    internal static class Lang
    {
        internal static bool IsChinese
        {
            get
            {
                try
                {
                    if (Localization.Loaded)
                    {
                        return Localization.isZHCN;
                    }
                }
                catch
                {
                    // 游戏类型不可用时退回系统语言
                    // Fall back to the OS language when the game types are unavailable
                }
                return CultureInfo.CurrentUICulture.Name.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static string T(string zh, string en)
        {
            return IsChinese ? zh : en;
        }
    }
}
