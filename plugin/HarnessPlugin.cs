using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace YummiHarness
{
    /// <summary>
    /// YummiHarness：独立的"安全注入"前置 mod。
    /// YummiHarness: standalone "safe injection" prerequisite mod.
    /// 其他 mod 用 Harness.PatchAll 代替 harmony.PatchAll 后：
    /// After other mods use Harness.PatchAll instead of harmony.PatchAll:
    ///  - 补丁只在目标方法与原版 IL 一致时才应用，失败只跳过自己
    ///  - a patch is applied only when the target matches vanilla IL; a failure skips only that patch
    ///  - 失败的 mod、互斥的 mod、多个 mod 修改同一函数，都在主菜单一次性弹窗提示，详情写入日志
    ///  - failed mods, conflicting mods and several mods patching one method are all shown in one main-menu dialog; details go to the log
    /// </summary>
    [BepInPlugin(PluginGuid, "YummiHarness", PluginVersion)]
    public class HarnessPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.yummi.dsp.yummiharness";
        public const string PluginVersion = "0.1.0";

        private static readonly Harmony harmony = new Harmony(PluginGuid);
        private static HarnessPlugin instance;

        private ConfigEntry<bool> warnShared;
        private bool menuReady;
        private DateTime preloadDoneUtc;
        private int reportedFailures;
        private int lastFlushedChange = -1;
        private readonly HashSet<string> reportedKeys = new HashSet<string>();

        private void Awake()
        {
            instance = this;
            Harness.ModNames[PluginGuid] = "YummiHarness";
            Harness.LogWarn = msg => Logger.LogWarning(msg);
            Harness.LogInfo = msg => Logger.LogInfo(msg);

            warnShared = Config.Bind("General", "WarnSharedTargets", true,
                "Warn when several mods patch the same game method. | 多个 mod 修改同一函数时是否弹窗警告。");

            try
            {
                harmony.PatchAll(typeof(MenuHooks));
            }
            catch (Exception e)
            {
                // 挂钩失败不致命：Watch 里有按时间兜底的逻辑
                // A failed hook is not fatal: Watch has a timer-based fallback
                Logger.LogWarning("Menu hook failed, using timer fallback: " + e.Message);
            }
            StartCoroutine(Watch());
        }

        private void OnDestroy()
        {
            harmony.UnpatchSelf();
        }

        internal static void MarkMenuReady()
        {
            if (instance != null)
            {
                instance.menuReady = true;
            }
        }

        internal static void StartAfterPreload()
        {
            if (instance != null)
            {
                instance.StartCoroutine(instance.AfterPreload());
            }
        }

        private IEnumerator AfterPreload()
        {
            while (!VFPreload.done)
            {
                yield return null;
            }
            yield return null;
            menuReady = true;
        }

        private IEnumerator Watch()
        {
            while (true)
            {
                yield return new WaitForSeconds(1f);

                if (!menuReady && VFPreload.done)
                {
                    if (preloadDoneUtc == default(DateTime))
                    {
                        preloadDoneUtc = DateTime.UtcNow;
                    }
                    // 兜底：预加载结束 5 秒后若已在主菜单，即使钩子没触发也继续
                    // Fallback: 5 seconds after preload ends, continue if we are in the main menu even if the hooks never fired
                    if ((DateTime.UtcNow - preloadDoneUtc).TotalSeconds > 5 && DSPGame.IsMenuDemo)
                    {
                        menuReady = true;
                    }
                }

                bool quiet = (DateTime.UtcNow - Harness.LastChangeUtc).TotalSeconds >= 2;
                if (menuReady && quiet && lastFlushedChange != Harness.ChangeCount)
                {
                    lastFlushedChange = Harness.ChangeCount;
                    try
                    {
                        Flush();
                    }
                    catch (Exception e)
                    {
                        Logger.LogError("YummiHarness flush failed: " + e);
                    }
                    Harness.ReleaseCache();
                }
            }
        }

        private void Flush()
        {
            var lines = new List<string>();   // 弹窗里的简短行
            var details = new List<string>(); // 日志里的详细行
            bool zh = Lang.IsChinese;

            // 1) patch 失败：按 mod 汇总
            // 1) patch failures: grouped by mod
            List<PatchFailure> fresh = Harness.Failures.Skip(reportedFailures).ToList();
            reportedFailures = Harness.Failures.Count;
            foreach (var group in fresh.GroupBy(f => f.Mod))
            {
                int n = group.Count();
                lines.Add(zh ? $"{group.Key}：部分 patch 失败（{n} 个），相关功能未启用"
                             : $"{group.Key}: some patches failed ({n}); related features are disabled");
                foreach (PatchFailure f in group)
                {
                    details.Add($"  [{f.Mod}] {f.Patch} -> {f.Target}: {f.Reason}");
                }
            }

            // 2) 互斥 mod
            // 2) conflicting mods
            foreach (ConflictRecord c in Harness.Conflicts)
            {
                string key = "conflict:" + c.Mod + "|" + c.OtherGuid;
                if (reportedKeys.Contains(key) || !Harness.IsPluginLoaded(c.OtherGuid))
                {
                    continue;
                }
                reportedKeys.Add(key);
                lines.Add(zh ? $"{c.Mod} 与 {c.OtherName} 冲突，不能同时使用"
                             : $"{c.Mod} conflicts with {c.OtherName}; do not use them together");
                details.Add($"  [{c.Mod}] " + (zh ? $"与 {c.OtherName}（{c.OtherGuid}）冲突" : $"conflicts with {c.OtherName} ({c.OtherGuid})")
                            + (string.IsNullOrEmpty(c.Note) ? "" : "  " + c.Note));
            }

            // 3) 多个 mod 修改同一函数
            // 3) several mods patching the same method
            if (warnShared.Value)
            {
                foreach (SharedTarget s in Harness.ComputeShared())
                {
                    string key = "shared:" + s.Target + "|" + string.Join(",", s.Others);
                    if (!reportedKeys.Add(key))
                    {
                        continue;
                    }
                    string mods = string.Join(zh ? "、" : ", ", s.Others);
                    if (!s.Risky)
                    {
                        // 只有 Postfix 等不会互相干扰的组合：只写日志，不弹窗 / harmless combination: log only, no dialog
                        Logger.LogInfo(zh ? $"多个 mod 修改同一函数 {s.Target}（不会互相干扰）：{mods}"
                                          : $"Several mods patch {s.Target} (harmless combination): {mods}");
                        continue;
                    }
                    lines.Add(zh ? $"多个 mod 修改同一函数 {s.Target}：{mods}"
                                 : $"Several mods patch the same method {s.Target}: {mods}");
                    details.Add($"  [shared] {s.Target}: {mods}");
                }
            }

            if (lines.Count == 0)
            {
                return;
            }

            string header = zh ? "以下 mod 检测到问题，游戏仍可继续运行。详情请看 BepInEx 日志（LogOutput.log）："
                               : "Some mods reported problems; the game can still run. See the BepInEx log (LogOutput.log) for details:";
            Logger.LogWarning(header);
            foreach (string d in details)
            {
                Logger.LogWarning(d);
            }

            var sb = new StringBuilder(header).Append("\n\n");
            int shown = 0;
            foreach (string line in lines)
            {
                if (shown++ >= 8)
                {
                    sb.Append("...\n");
                    break;
                }
                sb.Append("- ").Append(line).Append('\n');
            }
            UIMessageBox.Show(
                zh ? "YummiHarness | 模组提示" : "YummiHarness | Mod notice",
                sb.ToString(),
                "确定".Translate(),
                UIMessageBox.WARNING);
        }
    }

    /// <summary>与 BigTower 相同的"主菜单就绪"判定：预加载闪屏结束后，或主菜单 UIRoot 打开时。
    /// Same "main menu ready" check as BigTower: after the preload splash ends, or when the main-menu UIRoot opens.</summary>
    [HarmonyPatch]
    internal static class MenuHooks
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(VFPreload), nameof(VFPreload.InvokeOnLoadWorkEnded))]
        [HarmonyPriority(Priority.Last)]
        public static void InvokeOnLoadWorkEnded_Postfix()
        {
            HarnessPlugin.StartAfterPreload();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIRoot), "_OnOpen")]
        [HarmonyPriority(Priority.Last)]
        public static void UIRoot_OnOpen_Postfix()
        {
            if (DSPGame.IsMenuDemo)
            {
                HarnessPlugin.StartAfterPreload();
            }
        }
    }
}
