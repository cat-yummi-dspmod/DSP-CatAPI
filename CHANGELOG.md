# Changelog

## 0.2.3 - 2026-10-17

- `Harness.PruneConfigKeys` to drop cfg entries no longer bound by the mod.

## 0.2.2 - 2026-10-17

- Notice levels (Error / Warning / Info), `Harness.Report`, and `DialogLevel` config (default `ErrorOnly`); shared-patch warnings demoted to log when below threshold.
- `RegisterCompatibilityAdvisory` optional `NoticeLevel` parameter.

## 0.2.1 - 2026-10-15

- `Harness.RegisterCompatibilityAdvisory`: custom bilingual warnings when selected mods are all loaded (main-menu dialog + log detail).

## 0.2.0 - 2026-10-15

- Fix fingerprinting when CommonAPI Preloader loads `Assembly-CSharp` from in-memory modules (`data-0000…` paths): resolve vanilla DLL via BepInEx `ManagedPath` and signature fallback.
- Thunderstore `manifest.json` for distribution.
- Shared-patch detection: log safe multi-mod Postfix combinations as info; warn only on risky Harmony pairs.

## 0.1.0

- Initial release: `Harness.PatchAll`, `[Guard]`, failure aggregation, conflict registration, bilingual main-menu dialog, `harness` CLI and `get-fingerprint.ps1`.

---

# 更新日志

## 0.2.3 - 2026-10-17

- `Harness.PruneConfigKeys`：Bind 后删除 cfg 里已废弃、未登记的键。

## 0.2.2 - 2026-10-17

- 提示分级（Error / Warning / Info）、`Harness.Report`、`DialogLevel` 配置（默认仅 Error 弹窗）；无害同函数补丁按级别写日志。
- `RegisterCompatibilityAdvisory` 可选 `NoticeLevel`。

## 0.2.1 - 2026-10-15

- `Harness.RegisterCompatibilityAdvisory`：登记自定义中英兼容性告警，指定 mod 全部加载时在主菜单提示，详情写入日志。

## 0.2.0 - 2026-10-15

- 修复 CommonAPI Preloader 将程序集载入内存后指纹路径为 `data-0000…` 的问题：从 BepInEx `ManagedPath` 读原版 DLL，并按签名回退查找方法。
- 增加 Thunderstore 用 `manifest.json`。
- 同函数多 mod：安全 Postfix 组合记 Info，仅对高风险 Harmony 组合告警。

## 0.1.0

- 初版：`Harness.PatchAll`、`[Guard]`、失败汇总、互斥登记、主菜单双语弹窗、`harness` CLI 与 `get-fingerprint.ps1`。
