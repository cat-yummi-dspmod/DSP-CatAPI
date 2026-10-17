using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;

namespace YummiHarness
{
    /// <summary>
    /// BepInEx 不会自动删除或迁移 cfg 里的旧键。在 Bind 之前调用 <see cref="Harness.ApplyConfigSchema"/>：
    /// 用 <c>[_ConfigSchema] Version</c> 标记配置格式版本；可整表重建（WipeIfBelow）或按版本回调迁移（Upgrade）。
    /// </summary>
    public sealed class ConfigMigrationContext
    {
        internal ConfigFile Config;
        internal int FromVersion;
        internal int ToVersion;
        internal readonly Dictionary<(string section, string key), object> Transferred = new Dictionary<(string, string), object>();

        /// <summary>Remove one key from the on-disk config (call before rebinding).</summary>
        public void Remove(string section, string key)
        {
            Config.Remove(new ConfigDefinition(section, key));
        }

        /// <summary>Copy a typed value to <see cref="ApplyTransferred"/> and remove the old key.</summary>
        public bool MigrateKey<T>(string fromSection, string fromKey, string toSection, string toKey)
        {
            var from = new ConfigDefinition(fromSection, fromKey);
            if (!Config.TryGetEntry(from, out ConfigEntry<T> entry))
            {
                return false;
            }
            Transferred[(toSection, toKey)] = entry.Value;
            Config.Remove(from);
            return true;
        }

        /// <summary>After <c>Config.Bind</c>, apply values saved by <see cref="MigrateKey{T}"/>.</summary>
        public void ApplyTransferred<T>(string section, string key, ConfigEntry<T> entry)
        {
            if (Transferred.TryGetValue((section, key), out object value) && value is T typed)
            {
                entry.Value = typed;
            }
        }
    }

    /// <summary>Options for <see cref="Harness.ApplyConfigSchema"/>.</summary>
    public sealed class ConfigSchemaOptions
    {
        /// <summary>
        /// If the stored schema version is strictly less than this value, wipe all keys (except <c>_ConfigSchema.Version</c>) and skip Upgrade.
        /// 若已存版本 &lt; 此值，清空 cfg（保留版本键）并跳过 Upgrade，相当于删了重建。
        /// </summary>
        public int? WipeIfBelow;

        /// <summary>
        /// Called when stored version &lt; currentVersion and wipe did not run. Use Remove / MigrateKey, then ApplyTransferred after Bind.
        /// </summary>
        public Action<ConfigMigrationContext> Upgrade;
    }

    public static partial class Harness
    {
        /// <summary>Written to every mod cfg that uses ApplyConfigSchema.</summary>
        public const string ConfigSchemaSection = "_ConfigSchema";

        /// <summary>Config format version (not the mod assembly version).</summary>
        public const string ConfigSchemaVersionKey = "Version";

        private static string SchemaVersionDesc =>
            Lang.En("Config file format version managed by YummiHarness.ApplyConfigSchema. Do not edit unless you know what you are doing.")
                .ZhCN("由 YummiHarness.ApplyConfigSchema 维护的配置格式版本，不懂请勿手改。");

        /// <summary>
        /// Call once before other <c>Config.Bind</c> calls. Returns a context for ApplyTransferred after Bind (null if nothing to apply).
        /// </summary>
        public static ConfigMigrationContext ApplyConfigSchema(ConfigFile config, int currentVersion, ConfigSchemaOptions options)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            if (currentVersion < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(currentVersion));
            }

            ConfigEntry<int> versionEntry = config.Bind(ConfigSchemaSection, ConfigSchemaVersionKey, 0, SchemaVersionDesc);
            int loaded = versionEntry.Value;

            ConfigMigrationContext ctx = null;
            bool wipe = options?.WipeIfBelow != null && loaded < options.WipeIfBelow.Value;

            if (wipe)
            {
                WipeConfigExceptSchemaVersion(config);
                loaded = 0;
            }
            else if (options?.Upgrade != null && loaded < currentVersion)
            {
                ctx = new ConfigMigrationContext { Config = config, FromVersion = loaded, ToVersion = currentVersion };
                options.Upgrade(ctx);
            }

            if (loaded != currentVersion)
            {
                versionEntry.Value = currentVersion;
            }

            return ctx;
        }

        private static void WipeConfigExceptSchemaVersion(ConfigFile config)
        {
            foreach (ConfigDefinition def in config.Keys.ToList())
            {
                if (def.Section == ConfigSchemaSection && def.Key == ConfigSchemaVersionKey)
                {
                    continue;
                }
                config.Remove(def);
            }
        }

        /// <summary>
        /// Remove every cfg key not listed in <paramref name="retain"/> (e.g. after Bind, drop renamed/removed keys).
        /// 删除未在 retain 列表中的键，避免 BepInEx 永久残留废弃项。
        /// </summary>
        public static void PruneConfigKeys(ConfigFile config, IEnumerable<ConfigDefinition> retain)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            if (retain == null)
            {
                throw new ArgumentNullException(nameof(retain));
            }
            var set = new HashSet<ConfigDefinition>(retain);
            foreach (ConfigDefinition def in config.Keys.ToList())
            {
                if (!set.Contains(def))
                {
                    config.Remove(def);
                }
            }
        }

        public static void PruneConfigKeys(ConfigFile config, params ConfigDefinition[] retain)
        {
            PruneConfigKeys(config, (IEnumerable<ConfigDefinition>)retain);
        }
    }
}
