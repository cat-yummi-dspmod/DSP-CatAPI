# YummiHarness

https://github.com/cat-yummi-dspmod/DSP-YummiHarness

<details open>
<summary>中文</summary>

**戴森球计划的"安全注入"前置 mod + 命令行工具。** 补丁只在目标方法与原版 IL 完全一致时才会应用；失败只跳过这一个补丁；所有问题在主菜单一次性弹窗，详情写入日志。

## 它做什么

- **指纹校验**：对目标方法的 IL 做规范化后哈希。游戏更新后方法有任何变化，对应补丁自动跳过，不影响 mod 的其他功能。
- **失败记录**：patch 失败时记录该 mod，并在主菜单弹窗："xxx 部分 patch 失败，详情请看日志"。可通过 `PatchOptions.OnFailure` 返回 `false` 关闭记录（默认记录）。
- **互斥登记**：`Harness.RegisterConflict(...)`，对方存在时和 patch 失败合并在同一个弹窗。
- **自定义兼容性告警**：`Harness.RegisterCompatibilityAdvisory(...)`，当指定的几个 mod **都**已加载时，在主菜单弹窗提示（例如「A 与 B 在某功能上兼容性较差，可能引发 xxx」）；详情可写进日志。与互斥不同：不强制停用 mod。
- **同函数警告**：多个 mod 修改同一个游戏函数时给出警告（含没用本 mod 的 mod）。后打补丁的一方可用 `PatchOptions.OnShared` 决定自己是否仍然应用；`Harness.DeclareCompatible(a, b)` 可对已知安全的组合静音；配置项 `WarnSharedTargets` 可整体关闭。
- **语言**：弹窗和详细日志使用游戏语言。补丁发生的那一刻游戏语言通常还没加载，那一行日志退回系统语言。

## mod 作者怎么用

```csharp
[BepInDependency("com.yummi.dsp.yummiharness")]
...
Harness.RegisterConflict("MyMod", "other.guid", "OtherMod");
Harness.RegisterCompatibilityAdvisory(
    "MyMod",
    new[] { "com.other.mod.guid" },
    summaryZh: "MyMod 与 OtherMod 在物流统计上兼容性较差，可能出现产量显示异常",
    summaryEn: "MyMod and OtherMod may disagree on logistics stats; production UI can look wrong",
    detailZh: "建议关闭 OtherMod 的统计增强或只保留一个 mod 的 GameTick 逻辑。",
    detailEn: "Disable the other mod's stat tweak or keep only one GameTick-related feature.",
    advisoryId: "MyMod.WithOtherMod.Stats");
Harness.PatchAll(harmony, typeof(MyPatches), new PatchOptions { ModName = "MyMod" });

// 补丁方法照旧写，另加 [Guard]
[HarmonyPostfix]
[HarmonyPatch(typeof(StationComponent), nameof(StationComponent.AddItem))]
[YummiHarness.Guard("806f415ada529868")]
public static void AddItem_Postfix(...) { }
```

## 怎么获取指纹（不是 mod 作者也可以）

需要 .NET SDK，以及**游戏目录里未剥离的原版** `Assembly-CSharp.dll`（NuGet 的 GameLibs 方法体被剥空，不能用）。

```powershell
# 1. 看一个 mod（任何人写的）补丁了哪些函数、指纹是多少
.\tools\get-fingerprint.ps1 -Mod "C:\path\SomeMod.dll"

# 2. 看某个游戏函数的指纹和规范化 IL
.\tools\get-fingerprint.ps1 -Method "StationComponent::AddItem"

# 3. 游戏更新后，比较同一函数新旧版本的差异
.\tools\get-fingerprint.ps1 -Method "StationComponent::AddItem" -OldGame "D:\old\Dyson Sphere Program"
```

脚本会自动查找 Steam 版游戏目录（或用 `-Game` / 环境变量 `DSP_GAME_DIR` 指定），首次使用自动编译命令行工具。输出中：

- `[OK]` 与 `[Guard]` 一致；`[已变化]` 目标方法被游戏更新改动；`[找不到]` 方法被删除或改签名；`[无Guard]` 该补丁没有设置指纹，行尾给出可直接粘贴的 `[Guard("...")]`。
- 有补丁已变化/找不到时，退出码为 1，可放进构建脚本。

不用脚本也行：`harness scan <Mod.dll> <游戏目录>`、`harness dump <游戏目录> <类::方法>`、`harness diff <旧游戏目录> <新游戏目录> <类::方法>`。

## 构建

```
dotnet build plugin -c Release -o bin     # YummiHarness.dll
dotnet build cli -c Release -o bin        # harness.exe
```

## 局限

- 指纹只覆盖目标方法自身，不覆盖它调用的其他方法。
- 运行时通过 BepInEx 自带的 Mono.Cecil 读取游戏 dll 的 IL。

</details>

<details>
<summary>English</summary>

**A "safe injection" prerequisite mod + CLI for Dyson Sphere Program.** A patch is applied only if its target method's IL is identical to vanilla. A failed patch only skips itself, and every problem is shown in one main-menu dialog with details in the log.

## What it does

- **Fingerprint check**: the target method's IL is normalized and hashed. After a game update, any change to the method makes the matching patch skip itself; the rest of the mod keeps working.
- **Failure recording**: a failed patch records the mod and shows "xxx: some patches failed, see the log" in the main menu. Return `false` from `PatchOptions.OnFailure` to opt out (recording is the default).
- **Conflict registry**: `Harness.RegisterConflict(...)`; when the other mod is present it shares the same dialog as patch failures.
- **Custom compatibility advisories**: `Harness.RegisterCompatibilityAdvisory(...)` shows a main-menu notice when every listed plugin GUID is loaded (e.g. "Mod A and Mod B work poorly together for feature X; may cause Y"). Optional detail goes to the log. Unlike conflicts, nothing is auto-disabled.
- **Shared-target warning**: warns when several mods patch the same game method (including mods that do not use this one). The later mod can decide in `PatchOptions.OnShared` whether to still apply; `Harness.DeclareCompatible(a, b)` silences known-safe pairs; the `WarnSharedTargets` config turns it off.
- **Language**: dialog and detailed log use the game language. When a patch runs the game language is usually not loaded yet, so that single log line falls back to the OS language.

## For mod authors

```csharp
[BepInDependency("com.yummi.dsp.yummiharness")]
...
Harness.RegisterConflict("MyMod", "other.guid", "OtherMod");
Harness.RegisterCompatibilityAdvisory(
    "MyMod",
    new[] { "com.other.mod.guid" },
    summaryZh: "MyMod 与 OtherMod 在物流统计上兼容性较差，可能出现产量显示异常",
    summaryEn: "MyMod and OtherMod may disagree on logistics stats; production UI can look wrong",
    detailZh: "建议关闭 OtherMod 的统计增强或只保留一个 mod 的 GameTick 逻辑。",
    detailEn: "Disable the other mod's stat tweak or keep only one GameTick-related feature.",
    advisoryId: "MyMod.WithOtherMod.Stats");
Harness.PatchAll(harmony, typeof(MyPatches), new PatchOptions { ModName = "MyMod" });

// write patches as usual, add [Guard]
[HarmonyPostfix]
[HarmonyPatch(typeof(StationComponent), nameof(StationComponent.AddItem))]
[YummiHarness.Guard("806f415ada529868")]
public static void AddItem_Postfix(...) { }
```

## How to get fingerprints (you do not need to be the mod author)

You need the .NET SDK and the **unstripped vanilla** `Assembly-CSharp.dll` from the game folder (the NuGet GameLibs package has empty method bodies and cannot be used).

```powershell
# 1. See which methods any mod patches and their fingerprints
.\tools\get-fingerprint.ps1 -Mod "C:\path\SomeMod.dll"

# 2. Fingerprint and normalized IL of one game method
.\tools\get-fingerprint.ps1 -Method "StationComponent::AddItem"

# 3. After a game update, diff one method between two versions
.\tools\get-fingerprint.ps1 -Method "StationComponent::AddItem" -OldGame "D:\old\Dyson Sphere Program"
```

The script finds the Steam install automatically (or use `-Game` / env var `DSP_GAME_DIR`) and builds the CLI on first use. In the output:

- `[OK]` matches `[Guard]`; `[已变化]` (changed) the target was modified by a game update; `[找不到]` (not found) the method was removed or its signature changed; `[无Guard]` (no Guard) the patch has no fingerprint, and a paste-ready `[Guard("...")]` is printed at the end of the line.
- Exit code is 1 when a patch changed or is missing, so it can be used in a build script.

Without the script: `harness scan <Mod.dll> <game folder>`, `harness dump <game folder> <Type::Method>`, `harness diff <old game folder> <new game folder> <Type::Method>`.

## Build

```
dotnet build plugin -c Release -o bin     # YummiHarness.dll
dotnet build cli -c Release -o bin        # harness.exe
```

## Limitations

- The fingerprint covers only the target method itself, not the methods it calls.
- At runtime the game's IL is read through the Mono.Cecil that ships with BepInEx.

</details>
