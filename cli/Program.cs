using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using YummiHarness;

namespace YummiHarness.Cli
{
    internal static class Program
    {
        private const string Usage =
@"harness - YummiHarness 补丁指纹工具 / patch fingerprint tool

<game> = 游戏目录(含 DSPGAME.exe) 或 Assembly-CSharp.dll 的路径 / game folder or path to Assembly-CSharp.dll

  harness scan <Mod.dll> <game>
      中: 列出 mod 的每个 Harmony 补丁及其目标方法的指纹；与 [Guard] 对比。有变化时退出码为 1。
          别人的 mod 没有 [Guard] 也能用：输出的就是该 mod 补丁目标在当前游戏里的指纹。
      EN: List every Harmony patch in the mod with the fingerprint of its target method and compare it
          with [Guard]. Exit code 1 when something changed. Works on mods you did not write.
  harness dump <game> <Type::Method>
      中: 打印方法的规范化 IL 与指纹（选择器为子串匹配）。
      EN: Print the normalized IL and fingerprint of a method (selector is a substring match).
  harness diff <old game> <new game> <Type::Method>
      中: 比较同一方法在两个游戏版本中的 IL 差异。
      EN: Show the IL difference of one method between two game versions.";

        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            try
            {
                if (args.Length >= 3 && args[0] == "dump") return Dump(Game(args[1]), args[2]);
                if (args.Length >= 3 && args[0] == "scan") return Scan(args[1], Game(args[2]));
                if (args.Length >= 4 && args[0] == "diff") return Diff(Game(args[1]), Game(args[2]), args[3]);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("错误 / Error: " + e.Message);
                return 2;
            }
            Console.WriteLine(Usage);
            return 2;
        }

        private static string Game(string path)
        {
            if (System.IO.File.Exists(path))
            {
                return path;
            }
            foreach (string rel in new[] { @"DSPGAME_Data\Managed\Assembly-CSharp.dll", @"Managed\Assembly-CSharp.dll", "Assembly-CSharp.dll" })
            {
                string p = System.IO.Path.Combine(path, rel);
                if (System.IO.File.Exists(p)) return p;
            }
            throw new Exception("找不到 Assembly-CSharp.dll / Assembly-CSharp.dll not found: " + path);
        }

        // NuGet 里的 GameLibs 方法体被剥成 ldnull;throw，指纹没有意义
        // NuGet GameLibs have method bodies stripped to ldnull;throw, so fingerprints from them are meaningless
        private static void WarnIfStripped(ModuleDefinition game)
        {
            int total = 0, stripped = 0;
            foreach (MethodDefinition m in game.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody).Take(2000))
            {
                total++;
                var ins = m.Body.Instructions;
                if (ins.Count == 2 && ins[0].OpCode.Code == Mono.Cecil.Cil.Code.Ldnull && ins[1].OpCode.Code == Mono.Cecil.Cil.Code.Throw) stripped++;
            }
            if (total > 0 && stripped * 2 > total)
            {
                Console.WriteLine("!! 这个 dll 的方法体已被剥离，指纹无效，请用游戏目录里的原版 / This dll is stripped; fingerprints are meaningless. Use the one in the game folder.");
            }
        }
        private static ModuleDefinition Load(string path)
        {
            // 读取 mod 的特性参数时 Cecil 需要解析 0Harmony.dll（枚举 ArgumentType），
            // Cecil must resolve 0Harmony.dll (the ArgumentType enum) when reading the mod's attribute arguments;
            // 这里把 mod 所在目录、BepInEx 目录、本机 NuGet 缓存里的 HarmonyX 都加入搜索路径。
            // so add the mod's folder, the BepInEx folder and any HarmonyX in the local NuGet cache to the search path.
            var resolver = new DefaultAssemblyResolver();
            string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            resolver.AddSearchDirectory(dir);
            string nuget = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", "harmonyx");
            if (System.IO.Directory.Exists(nuget))
            {
                foreach (string v in System.IO.Directory.GetDirectories(nuget))
                {
                    string lib = System.IO.Path.Combine(v, "lib", "net45");
                    if (System.IO.Directory.Exists(lib)) resolver.AddSearchDirectory(lib);
                }
            }
            string extra = Environment.GetEnvironmentVariable("HARNESS_LIB");
            if (!string.IsNullOrEmpty(extra)) resolver.AddSearchDirectory(extra);
            return ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver });
        }

        private static List<MethodDefinition> Find(ModuleDefinition module, string selector)
        {
            return module.GetTypes()
                .SelectMany(t => t.Methods)
                .Where(m => m.FullName.Contains(selector))
                .ToList();
        }

        private static int Dump(string gameDll, string selector)
        {
            List<MethodDefinition> found = Find(Load(gameDll), selector);
            if (found.Count == 0)
            {
                Console.WriteLine("找不到方法: " + selector);
                return 1;
            }
            if (found.Count > 1)
            {
                Console.WriteLine($"匹配到 {found.Count} 个方法，请把选择器写得更具体:");
                foreach (MethodDefinition m in found)
                {
                    Console.WriteLine($"  {Fingerprint.Hash(m)}  {m.FullName}");
                }
                return 1;
            }
            string text = Fingerprint.Describe(found[0]);
            Console.WriteLine(text);
            Console.WriteLine("hash: " + Fingerprint.HashText(text));
            return 0;
        }

        private static int Diff(string oldDll, string newDll, string selector)
        {
            List<MethodDefinition> a = Find(Load(oldDll), selector);
            List<MethodDefinition> b = Find(Load(newDll), selector);
            if (a.Count != 1 || b.Count != 1)
            {
                Console.WriteLine($"选择器需唯一匹配（旧版 {a.Count} 个，新版 {b.Count} 个）。");
                foreach (MethodDefinition m in a.Concat(b)) Console.WriteLine("  " + m.FullName);
                return 1;
            }
            string ta = Fingerprint.Describe(a[0]);
            string tb = Fingerprint.Describe(b[0]);
            if (ta == tb)
            {
                Console.WriteLine("相同。hash: " + Fingerprint.HashText(ta));
                return 0;
            }
            Console.WriteLine($"不同。旧 {Fingerprint.HashText(ta)}  新 {Fingerprint.HashText(tb)}\n");
            PrintDiff(ta.Split('\n'), tb.Split('\n'));
            return 1;
        }

        // 简单 LCS 行 diff（方法体通常只有几十到几百行，够用）
        // Simple LCS line diff (method bodies are tens to hundreds of lines, which is plenty)
        private static void PrintDiff(string[] a, string[] b)
        {
            int[,] lcs = new int[a.Length + 1, b.Length + 1];
            for (int i = a.Length - 1; i >= 0; i--)
                for (int j = b.Length - 1; j >= 0; j--)
                    lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            int x = 0, y = 0;
            while (x < a.Length && y < b.Length)
            {
                if (a[x] == b[y]) { x++; y++; }
                else if (lcs[x + 1, y] >= lcs[x, y + 1]) Console.WriteLine("- " + a[x++]);
                else Console.WriteLine("+ " + b[y++]);
            }
            while (x < a.Length) Console.WriteLine("- " + a[x++]);
            while (y < b.Length) Console.WriteLine("+ " + b[y++]);
        }

        // ---------------- scan ----------------

        private sealed class Target
        {
            public TypeReference Type;
            public string Name;
            public CustomAttributeArgument[] ArgTypes;
        }

        private static int Scan(string modDll, string gameDll)
        {
            ModuleDefinition mod = Load(modDll);
            ModuleDefinition game = Load(gameDll);
            WarnIfStripped(game);
            int ok = 0, bad = 0, unguarded = 0;

            foreach (TypeDefinition type in mod.GetTypes())
            {
                Target classTarget = ReadTarget(type.CustomAttributes, null);
                foreach (MethodDefinition patch in type.Methods)
                {
                    if (!patch.CustomAttributes.Any(IsHarmonyAttribute)) continue;
                    Target t = ReadTarget(patch.CustomAttributes, classTarget);
                    string label = type.Name + "." + patch.Name;
                    if (t.Type == null || t.Name == null)
                    {
                        Console.WriteLine($"[跳过]     {label}  (目标不是 typeof + 方法名 的形式，需手动检查)");
                        continue;
                    }

                    MethodDefinition target;
                    string err = Resolve(game, t, out target);
                    if (err != null)
                    {
                        Console.WriteLine($"[找不到]   {label} -> {t.Type.Name}.{t.Name}  {err}");
                        bad++;
                        continue;
                    }

                    string actual = Fingerprint.Hash(target);
                    CustomAttribute guard = patch.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "YummiHarness.GuardAttribute");
                    string expected = guard?.ConstructorArguments.Count > 0 ? guard.ConstructorArguments[0].Value as string : null;
                    if (expected == null)
                    {
                        Console.WriteLine($"[无Guard]  {label} -> {target.DeclaringType.Name}.{target.Name}  建议加: [Guard(\"{actual}\")]");
                        unguarded++;
                    }
                    else if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[OK]       {label} -> {target.DeclaringType.Name}.{target.Name}");
                        ok++;
                    }
                    else
                    {
                        Console.WriteLine($"[已变化]   {label} -> {target.DeclaringType.Name}.{target.Name}  期望 {expected}  当前 {actual}");
                        bad++;
                    }
                }
            }
            Console.WriteLine($"\n共 {ok + bad + unguarded} 个补丁: 匹配 {ok}，已变化/找不到 {bad}，未设置 Guard {unguarded}");
            return bad > 0 ? 1 : 0;
        }

        private static bool IsHarmonyAttribute(CustomAttribute a)
        {
            string n = a.AttributeType.FullName;
            return n == "HarmonyLib.HarmonyPatch" || n == "HarmonyLib.HarmonyPrefix" || n == "HarmonyLib.HarmonyPostfix"
                || n == "HarmonyLib.HarmonyTranspiler" || n == "HarmonyLib.HarmonyFinalizer";
        }

        private static Target ReadTarget(IEnumerable<CustomAttribute> attrs, Target baseTarget)
        {
            var t = new Target { Type = baseTarget?.Type, Name = baseTarget?.Name, ArgTypes = baseTarget?.ArgTypes };
            foreach (CustomAttribute a in attrs.Where(x => x.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
            {
                foreach (CustomAttributeArgument arg in a.ConstructorArguments)
                {
                    switch (arg.Type.FullName)
                    {
                        case "System.Type": t.Type = arg.Value as TypeReference; break;
                        case "System.String": t.Name = arg.Value as string; break;
                        case "System.Type[]": t.ArgTypes = arg.Value as CustomAttributeArgument[]; break;
                    }
                }
            }
            return t;
        }

        private static string Resolve(ModuleDefinition game, Target t, out MethodDefinition result)
        {
            result = null;
            TypeDefinition type = game.GetType(t.Type.FullName);
            if (type == null) return "游戏中找不到类型 " + t.Type.FullName;

            List<MethodDefinition> candidates = type.Methods.Where(m => m.Name == t.Name).ToList();
            if (t.ArgTypes != null)
            {
                string[] want = t.ArgTypes.Select(a => ((TypeReference)a.Value).FullName).ToArray();
                candidates = candidates.Where(m =>
                    m.Parameters.Count == want.Length &&
                    m.Parameters.Select((p, i) => p.ParameterType.FullName.TrimEnd('&') == want[i]).All(x => x)).ToList();
            }
            if (candidates.Count == 0) return "游戏中找不到该方法/参数签名";
            if (candidates.Count > 1) return "存在多个重载，请在 HarmonyPatch 里指定参数类型";
            result = candidates[0];
            return null;
        }
    }
}
