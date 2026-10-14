using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using Mono.Cecil;

namespace YummiHarness
{
    /// <summary>
    /// 标在补丁方法上：只有目标方法当前的指纹等于 Hash，这个补丁才会被应用。
    /// Put on a patch method: the patch is applied only if the target method's current fingerprint equals Hash.
    /// 指纹的获取方法见 README（命令行：harness scan / harness dump）。
    /// See the README for how to get fingerprints (CLI: harness scan / harness dump).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class GuardAttribute : Attribute
    {
        public string Hash { get; }

        public GuardAttribute(string hash)
        {
            Hash = hash;
        }
    }

    /// <summary>一次 patch 失败的记录。Reason 会按"读取时"的语言返回（弹窗/详细日志时为游戏语言）。
    /// Record of one failed patch. Reason is returned in the language at read time (game language for the dialog and detailed log).</summary>
    public sealed class PatchFailure
    {
        public string Mod { get; internal set; }
        public string Patch { get; internal set; }
        public string Target { get; internal set; }
        internal string ReasonZh;
        internal string ReasonEn;
        public string Reason => Lang.T(ReasonZh, ReasonEn);
    }

    /// <summary>目标函数已被其他 mod 修改过。
    /// A target method that another mod has already patched.</summary>
    public sealed class SharedTarget
    {
        public string Mod { get; internal set; }
        public string Target { get; internal set; }
        public List<string> Others { get; internal set; }
        /// <summary>
        /// 组合是否可能互相干扰（见 IsRiskyPair）。只有 true 才会弹窗，false 只写日志。
        /// Whether the combination can interfere (see IsRiskyPair). Only true triggers the dialog; false is log-only.
        /// </summary>
        public bool Risky { get; internal set; }
    }

    public sealed class PatchOptions
    {
        /// <summary>显示名；不填则用 Harmony id。
        /// Display name; defaults to the Harmony id.</summary>
        public string ModName;

        /// <summary>
        /// patch 失败时调用。返回 true（默认，包括不设置本回调时）= 记录这个 mod 的失败，主菜单弹窗提醒；
        /// Called when a patch fails. Returning true (the default, also when no callback is set) = record this mod's failure and show it in the main-menu dialog;
        /// 返回 false = 不记录（例如这个补丁只是可选的小功能，不想打扰玩家）。
        /// returning false = do not record (e.g. the patch is an optional minor feature and should not bother the player).
        /// </summary>
        public Func<PatchFailure, bool> OnFailure;

        /// <summary>
        /// 目标函数已被别的 mod 修改时调用（只有后打补丁的一方会收到）。
        /// Called when the target method was already patched by another mod (only the mod that patches later receives it).
        /// 返回 true（默认）= 仍然应用本补丁；返回 false = 跳过本补丁（不算失败）。
        /// Returning true (default) = still apply this patch; returning false = skip this patch (not counted as a failure).
        /// 无论返回什么，多个 mod 修改同一函数都会在弹窗中给出警告（可由 DeclareCompatible 静音）。
        /// Either way, several mods patching one method are warned about in the dialog (silence with DeclareCompatible).
        /// </summary>
        public Func<SharedTarget, bool> OnShared;
    }

    internal sealed class ConflictRecord
    {
        public string Mod;
        public string OtherGuid;
        public string OtherName;
        public string Note;
    }

    /// <summary>
    /// YummiHarness 的公开接口：
    /// Public API of YummiHarness:
    ///   Harness.PatchAll(harmony, typeof(X), options)   代替 harmony.PatchAll
    ///   Harness.PatchAll(harmony, typeof(X), options)   replaces harmony.PatchAll
    ///   Harness.RegisterConflict(...)                    登记互斥 mod
    ///   Harness.RegisterConflict(...)                    register a conflicting mod
    ///   Harness.DeclareCompatible(a, b)                  声明两个 mod 同改一个函数是安全的（静音警告）
    ///   Harness.DeclareCompatible(a, b)                  declare that two mods patching one method is safe (silences the warning)
    /// </summary>
    public static class Harness
    {
        internal static readonly List<PatchFailure> Failures = new List<PatchFailure>();
        internal static readonly List<ConflictRecord> Conflicts = new List<ConflictRecord>();
        internal static readonly List<MethodBase> Targets = new List<MethodBase>();
        internal static readonly Dictionary<string, string> ModNames = new Dictionary<string, string>();
        private static readonly HashSet<string> compatible = new HashSet<string>();
        private static readonly Dictionary<string, ModuleDefinition> modules = new Dictionary<string, ModuleDefinition>();

        internal static Action<string> LogWarn = _ => { };
        internal static Action<string> LogInfo = _ => { };

        /// <summary>每次登记/打补丁都会 +1，插件据此判断是否有新内容要提示。
        /// Incremented on every registration/patch; the plugin uses it to tell whether there is anything new to report.</summary>
        internal static int ChangeCount { get; private set; }
        internal static DateTime LastChangeUtc { get; private set; } = DateTime.UtcNow;

        private static void Changed()
        {
            ChangeCount++;
            LastChangeUtc = DateTime.UtcNow;
        }

        // ---------------- 公开接口 ----------------
        // ---------------- Public API ----------------

        public static void PatchAll(Harmony harmony, Type patchClass, PatchOptions options = null)
        {
            string modId = harmony.Id;
            string modName = options?.ModName ?? modId;
            ModNames[modId] = modName;
            Changed();

            List<HarmonyMethod> classAttrs = HarmonyMethodExtensions.GetFromType(patchClass) ?? new List<HarmonyMethod>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (MethodInfo patch in patchClass.GetMethods(flags))
            {
                List<HarmonyMethod> methodAttrs = HarmonyMethodExtensions.GetFromMethod(patch);
                if (methodAttrs == null || methodAttrs.Count == 0)
                {
                    continue; // 不是补丁方法（普通辅助函数）
                }
                PatchOne(harmony, modName, options, patchClass, patch, classAttrs, methodAttrs);
            }
            Changed();
        }

        /// <summary>
        /// 登记"我和 otherGuid 这个 mod 互斥"。到主菜单时若对方确实已加载，会和 patch 失败一起弹窗提示。
        /// Register "this mod conflicts with otherGuid". If that mod is actually loaded when the main menu appears, it is shown in the same dialog as patch failures.
        /// 登记本身不会停用任何东西；是否自我停用由调用方决定（可用 IsPluginLoaded 判断）。
        /// Registering disables nothing by itself; whether to disable itself is up to the caller (use IsPluginLoaded to check).
        /// </summary>
        public static void RegisterConflict(string modName, string otherGuid, string otherName = null, string note = null)
        {
            if (Conflicts.Any(c => c.Mod == modName && c.OtherGuid == otherGuid))
            {
                return;
            }
            Conflicts.Add(new ConflictRecord { Mod = modName, OtherGuid = otherGuid, OtherName = otherName ?? otherGuid, Note = note });
            Changed();
        }

        public static bool IsPluginLoaded(string guid)
        {
            return Chainloader.PluginInfos.ContainsKey(guid);
        }

        /// <summary>声明两个 mod（Harmony id）同时修改同一函数是安全的，不再为这一对发警告。
        /// Declare that two mods (Harmony ids) patching the same method is safe; no more warnings for this pair.</summary>
        public static void DeclareCompatible(string harmonyIdA, string harmonyIdB)
        {
            compatible.Add(PairKey(harmonyIdA, harmonyIdB));
            Changed();
        }

        /// <summary>手动上报一次失败（不是通过 PatchAll 打的补丁时用）。
        /// Report a failure manually (for patches not applied through PatchAll).</summary>
        public static void ReportFailure(string modName, string what, string reasonZh, string reasonEn)
        {
            Failures.Add(new PatchFailure { Mod = modName, Patch = what, Target = "-", ReasonZh = reasonZh, ReasonEn = reasonEn });
            Changed();
        }

        // ---------------- 内部实现 ----------------
        // ---------------- Internals ----------------

        private static void PatchOne(Harmony harmony, string modName, PatchOptions options, Type patchClass, MethodInfo patch,
            List<HarmonyMethod> classAttrs, List<HarmonyMethod> methodAttrs)
        {
            string patchName = patchClass.Name + "." + patch.Name;
            string targetName = "?";
            try
            {
                HarmonyMethod merged = HarmonyMethod.Merge(classAttrs.Concat(methodAttrs).ToList());
                MethodBase original = ResolveTarget(merged);
                if (original == null)
                {
                    Fail(options, modName, patchName, targetName,
                        "找不到目标方法（可能被改名或签名变化）", "Target method not found (renamed or signature changed)");
                    return;
                }
                targetName = original.DeclaringType?.Name + "." + original.Name;

                var guard = patch.GetCustomAttributes(typeof(GuardAttribute), false).FirstOrDefault() as GuardAttribute;
                if (guard == null)
                {
                    LogWarn($"[YummiHarness] [{modName}] " + Lang.T($"{patchName} 没有 [Guard]，不做校验直接应用", $"{patchName} has no [Guard]; applied without verification"));
                }
                else
                {
                    string actual = ComputeHash(original, out string error);
                    if (actual == null)
                    {
                        Fail(options, modName, patchName, targetName, "无法计算目标方法指纹：" + error, "Cannot compute target fingerprint: " + error);
                        return;
                    }
                    if (!string.Equals(actual, guard.Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        Fail(options, modName, patchName, targetName,
                            $"目标方法与原版不一致（期望 {guard.Hash}，当前 {actual}），已跳过",
                            $"Target differs from vanilla (expected {guard.Hash}, got {actual}); skipped");
                        return;
                    }
                }

                List<string> others = OwnersOf(original).Where(o => o != harmony.Id && !IsCompatible(o, harmony.Id)).ToList();
                if (others.Count > 0 && options?.OnShared != null)
                {
                    var shared = new SharedTarget { Mod = modName, Target = targetName, Others = others.Select(DisplayName).ToList() };
                    bool apply = true;
                    try { apply = options.OnShared(shared); } catch (Exception e) { LogWarn("[YummiHarness] OnShared: " + e.Message); }
                    if (!apply)
                    {
                        LogInfo($"[YummiHarness] [{modName}] " + Lang.T($"{patchName} 因目标已被其他 mod 修改而按调用方要求跳过", $"{patchName} skipped by caller because the target is already patched by another mod"));
                        return;
                    }
                }

                var hm = new HarmonyMethod(patch);
                switch (Kind(patch))
                {
                    case "prefix": harmony.Patch(original, prefix: hm); break;
                    case "postfix": harmony.Patch(original, postfix: hm); break;
                    case "transpiler": harmony.Patch(original, transpiler: hm); break;
                    case "finalizer": harmony.Patch(original, finalizer: hm); break;
                    default:
                        Fail(options, modName, patchName, targetName,
                            "无法判断补丁类型（请加 [HarmonyPrefix] 等特性）", "Cannot tell patch kind (add [HarmonyPrefix] etc.)");
                        return;
                }
                if (!Targets.Contains(original))
                {
                    Targets.Add(original);
                }
            }
            catch (Exception e)
            {
                Fail(options, modName, patchName, targetName, e.GetType().Name + ": " + e.Message, e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Fail(PatchOptions options, string mod, string patch, string target, string zh, string en)
        {
            var failure = new PatchFailure { Mod = mod, Patch = patch, Target = target, ReasonZh = zh, ReasonEn = en };
            bool record = true;
            if (options?.OnFailure != null)
            {
                try { record = options.OnFailure(failure); } catch (Exception e) { LogWarn("[YummiHarness] OnFailure: " + e.Message); }
            }
            if (record)
            {
                Failures.Add(failure);
            }
            // 补丁发生时游戏语言可能还没加载，这行日志的语言是尽力而为；详细日志会在主菜单阶段再按游戏语言输出一遍
            // The game language may not be loaded when a patch runs, so this log line's language is best effort; detailed logs are printed again in the game language at the main-menu stage
            LogWarn($"[YummiHarness] [{mod}] " + Lang.T("patch 未应用 ", "patch not applied ") + $"{patch} -> {target}: {failure.Reason}");
        }

        internal static List<string> OwnersOf(MethodBase method)
        {
            Patches info = Harmony.GetPatchInfo(method);
            if (info == null)
            {
                return new List<string>();
            }
            return info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers)
                .Select(p => p.owner).Distinct().ToList();
        }

        internal static string DisplayName(string harmonyId)
        {
            return ModNames.TryGetValue(harmonyId, out string name) ? name : harmonyId;
        }

        private static string PairKey(string a, string b)
        {
            return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
        }

        private static bool IsCompatible(string a, string b)
        {
            return compatible.Contains(PairKey(a, b));
        }

        /// <summary>找出被多个 mod（含未使用 Harness 的 mod）同时修改的函数。
        /// Find methods patched by several mods at once (including mods that do not use Harness).</summary>
        internal static List<SharedTarget> ComputeShared()
        {
            var result = new List<SharedTarget>();
            foreach (MethodBase target in Targets)
            {
                Patches info = Harmony.GetPatchInfo(target);
                if (info == null)
                {
                    continue;
                }
                // YummiHarness 自己的挂钩不算 / YummiHarness's own hooks do not count
                List<string> owners = OwnersOf(target).Where(o => o != HarnessPlugin.PluginGuid).ToList();
                bool shared = false;
                bool risky = false;
                for (int i = 0; i < owners.Count; i++)
                    for (int j = i + 1; j < owners.Count; j++)
                    {
                        if (IsCompatible(owners[i], owners[j])) continue;
                        shared = true;
                        risky |= IsRiskyPair(info, owners[i], owners[j]);
                    }
                if (shared)
                {
                    result.Add(new SharedTarget
                    {
                        Target = target.DeclaringType?.Name + "." + target.Name,
                        Others = owners.Select(DisplayName).OrderBy(x => x).ToList(),
                        Risky = risky
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// 只有这些组合才可能真正互相干扰（Harmony 本来就允许多个 Postfix / 不跳过原函数的 Prefix 共存）：
        ///  - 任一方使用 Transpiler（直接改 IL）
        ///  - 一方的 Prefix 返回 bool（可以跳过原函数和其他 Prefix），另一方也有 Prefix
        ///  - 双方都有 Finalizer
        /// Only these combinations can really interfere (Harmony is designed to let several Postfixes
        /// and non-skipping Prefixes coexist):
        ///  - either side uses a Transpiler (rewrites IL directly)
        ///  - one side has a bool-returning Prefix (can skip the original and other Prefixes) and the other has a Prefix
        ///  - both sides have a Finalizer
        /// </summary>
        private static bool IsRiskyPair(Patches info, string a, string b)
        {
            bool Has(IEnumerable<Patch> list, string o) => list.Any(p => p.owner == o);
            bool Skips(string o) => info.Prefixes.Any(p => p.owner == o && p.PatchMethod != null && p.PatchMethod.ReturnType == typeof(bool));
            return Has(info.Transpilers, a) || Has(info.Transpilers, b)
                || (Skips(a) && Has(info.Prefixes, b)) || (Skips(b) && Has(info.Prefixes, a))
                || (Has(info.Finalizers, a) && Has(info.Finalizers, b));
        }

        private static MethodBase ResolveTarget(HarmonyMethod m)
        {
            Type type = m.declaringType;
            if (type == null)
            {
                return null;
            }
            switch (m.methodType ?? MethodType.Normal)
            {
                case MethodType.Getter:
                    return AccessTools.DeclaredProperty(type, m.methodName)?.GetGetMethod(true);
                case MethodType.Setter:
                    return AccessTools.DeclaredProperty(type, m.methodName)?.GetSetMethod(true);
                case MethodType.Constructor:
                    return AccessTools.DeclaredConstructor(type, m.argumentTypes);
                case MethodType.StaticConstructor:
                    return type.TypeInitializer;
                default:
                    return m.methodName == null ? null : AccessTools.DeclaredMethod(type, m.methodName, m.argumentTypes);
            }
        }

        private static string Kind(MethodInfo m)
        {
            if (m.IsDefined(typeof(HarmonyPrefix), false)) return "prefix";
            if (m.IsDefined(typeof(HarmonyPostfix), false)) return "postfix";
            if (m.IsDefined(typeof(HarmonyTranspiler), false)) return "transpiler";
            if (m.IsDefined(typeof(HarmonyFinalizer), false)) return "finalizer";
            switch (m.Name)
            {
                case "Prefix": return "prefix";
                case "Postfix": return "postfix";
                case "Transpiler": return "transpiler";
                case "Finalizer": return "finalizer";
                default: return null;
            }
        }

        /// <summary>从磁盘上的原始 dll 读取 IL 计算指纹（不受其他 mod 已打的补丁影响）。
        /// Compute the fingerprint from the original dll on disk (unaffected by patches other mods already applied).</summary>
        private static string ComputeHash(MethodBase method, out string error)
        {
            error = null;
            try
            {
                string path = DllPathOf(method);
                if (!modules.TryGetValue(path, out ModuleDefinition module))
                {
                    module = ModuleDefinition.ReadModule(new MemoryStream(File.ReadAllBytes(path)));
                    modules[path] = module;
                }
                // 先按 token 找并核对名字；对不上（例如方法对象来自被其他 mod 改写过的模块）就按签名找
                // Look up by token and verify the name; if it does not match (e.g. the method object comes
                // from a module another mod rewrote), fall back to a signature search
                var def = module.LookupToken(method.MetadataToken) as MethodDefinition;
                if (def == null || !SameMethod(def, method))
                {
                    def = FindBySignature(module, method);
                }
                if (def == null)
                {
                    error = "dll 中找不到对应方法 / method not found in dll";
                    return null;
                }
                return Fingerprint.Hash(def);
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        /// <summary>
        /// 定位原始 dll。不能只信 Module.FullyQualifiedName：方法被其他 mod 改过后，它可能是 "data-0000…" 这样的内存模块名。
        /// Locate the original dll. Module.FullyQualifiedName cannot be trusted alone: once another mod has
        /// patched the method it can be an in-memory module name such as "data-0000…".
        /// </summary>
        private static string DllPathOf(MethodBase method)
        {
            string path = method.Module.FullyQualifiedName;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                return path;
            }
            string name = (method.DeclaringType ?? method.Module.GetType()).Assembly.GetName().Name + ".dll";
            string managed = Path.Combine(BepInEx.Paths.ManagedPath, name);
            if (File.Exists(managed))
            {
                return managed;
            }
            throw new FileNotFoundException("找不到原始 dll / original dll not found: " + name);
        }

        private static string CecilTypeName(Type t)
        {
            return t.FullName?.Replace('+', '/');
        }

        private static bool SameMethod(MethodDefinition def, MethodBase method)
        {
            return def.Name == method.Name
                && def.DeclaringType.FullName == CecilTypeName(method.DeclaringType)
                && def.Parameters.Count == method.GetParameters().Length;
        }

        private static MethodDefinition FindBySignature(ModuleDefinition module, MethodBase method)
        {
            TypeDefinition type = method.DeclaringType == null ? null : module.GetType(CecilTypeName(method.DeclaringType));
            if (type == null)
            {
                return null;
            }
            ParameterInfo[] ps = method.GetParameters();
            List<MethodDefinition> candidates = type.Methods
                .Where(m => m.Name == method.Name && m.Parameters.Count == ps.Length && m.IsStatic == method.IsStatic).ToList();
            if (candidates.Count > 1)
            {
                candidates = candidates.Where(m => Enumerable.Range(0, ps.Length).All(i =>
                    m.Parameters[i].ParameterType.FullName.Replace('/', '+').TrimEnd('&') == (ps[i].ParameterType.FullName ?? "").TrimEnd('&'))).ToList();
            }
            return candidates.Count == 1 ? candidates[0] : null;
        }

        internal static void ReleaseCache()
        {
            foreach (ModuleDefinition m in modules.Values)
            {
                m.Dispose();
            }
            modules.Clear();
        }
    }
}
