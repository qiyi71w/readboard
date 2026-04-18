using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace readboard
{
    internal sealed class DualFormatAppConfigStore : IAppConfigStore
    {
        private sealed class JsonConfigReadResult
        {
            public JsonConfigReadResult(AppConfig config, bool shouldPersistFallback)
            {
                Config = config;
                ShouldPersistFallback = shouldPersistFallback;
            }

            public AppConfig Config { get; private set; }
            public bool ShouldPersistFallback { get; private set; }
        }

        private enum LegacyMainConfigStatus
        {
            MissingOrInvalid = 0,
            Imported = 1,
            MachineMismatch = 2
        }

        private const string JsonFileName = "config.readboard.json";
        private const string LegacyMainFileName = "config_readboard.txt";
        private const string LegacyOtherFileName = "config_readboard_others.txt";
        private const string JsonCorruptSuffix = ".corrupt.";
        private const string JsonTempSuffix = ".tmp";

        private readonly string baseDirectory;
        private readonly string machineKey;
        private readonly string protocolVersion;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();

        public DualFormatAppConfigStore(string baseDirectory, string machineKey, string protocolVersion)
        {
            this.baseDirectory = baseDirectory;
            this.machineKey = machineKey;
            this.protocolVersion = protocolVersion;
        }

        public AppConfigLoadResult Load()
        {
            JsonConfigReadResult jsonResult = ReadJsonConfig();
            AppConfig config = jsonResult.Config;
            bool hasExistingConfig = config != null;
            if (config == null)
            {
                config = AppConfig.CreateDefault(protocolVersion, machineKey);
                hasExistingConfig = ImportLegacyConfig(config);
                if (hasExistingConfig || jsonResult.ShouldPersistFallback)
                    Save(config);
            }
            EnsureConfigMetadata(config);
            return new AppConfigLoadResult(config, hasExistingConfig);
        }

        public void Save(AppConfig config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            EnsureConfigMetadata(config);
            WriteJsonConfig(config);
            WriteLegacyMainConfig(config);
            WriteLegacyOtherConfig(config);
        }

        private JsonConfigReadResult ReadJsonConfig()
        {
            string path = GetPath(JsonFileName);
            if (!File.Exists(path))
                return new JsonConfigReadResult(null, false);
            string content = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(content))
                return RecoverFromInvalidJson(path);

            try
            {
                AppConfig config = DeserializePartialJsonConfig(content);
                if (!IsOwnedByCurrentMachine(config))
                    return new JsonConfigReadResult(null, false);
                return new JsonConfigReadResult(config, false);
            }
            catch (Exception ex)
            {
                if (!IsInvalidJsonException(ex))
                    throw;
                return RecoverFromInvalidJson(path);
            }
        }

        private AppConfig DeserializePartialJsonConfig(string content)
        {
            IDictionary<string, object> values = serializer.Deserialize<Dictionary<string, object>>(content);
            if (values == null)
                return null;

            AppConfig config = AppConfig.CreateDefault(protocolVersion, machineKey);
            ApplyJsonOverrides(config, values);
            return config;
        }

        private bool ImportLegacyConfig(AppConfig config)
        {
            LegacyMainConfigStatus legacyMainStatus = ApplyLegacyMainConfig(config);
            if (legacyMainStatus == LegacyMainConfigStatus.MachineMismatch)
                return false;

            bool hasLegacyOther = ApplyLegacyOtherConfig(config);
            return legacyMainStatus == LegacyMainConfigStatus.Imported || hasLegacyOther;
        }

        private void ApplyJsonOverrides(AppConfig config, IDictionary<string, object> values)
        {
            config.ProtocolVersion = ReadStringValue(values, "ProtocolVersion", config.ProtocolVersion);
            config.MachineKey = ReadStringValue(values, "MachineKey", config.MachineKey);
            config.BlackOffset = ReadIntValue(values, "BlackOffset", config.BlackOffset);
            config.WhiteOffset = ReadIntValue(values, "WhiteOffset", config.WhiteOffset);
            config.BlackPercent = ReadIntValue(values, "BlackPercent", config.BlackPercent);
            config.WhitePercent = ReadIntValue(values, "WhitePercent", config.WhitePercent);
            config.UseMagnifier = ReadBoolValue(values, "UseMagnifier", config.UseMagnifier);
            config.VerifyMove = ReadBoolValue(values, "VerifyMove", config.VerifyMove);
            config.ShowScaleHint = ReadBoolValue(values, "ShowScaleHint", config.ShowScaleHint);
            config.ShowInBoard = ReadBoolValue(values, "ShowInBoard", config.ShowInBoard);
            config.ShowInBoardHint = ReadBoolValue(values, "ShowInBoardHint", config.ShowInBoardHint);
            config.AutoMinimize = ReadBoolValue(values, "AutoMinimize", config.AutoMinimize);
            config.SyncIntervalMs = ReadIntValue(values, "SyncIntervalMs", config.SyncIntervalMs);
            config.GrayOffset = ReadIntValue(values, "GrayOffset", config.GrayOffset);
            config.UseEnhanceScreen = ReadBoolValue(values, "UseEnhanceScreen", config.UseEnhanceScreen);
            config.PlayPonder = ReadBoolValue(values, "PlayPonder", config.PlayPonder);
            config.DisableShowInBoardShortcut = ReadBoolValue(values, "DisableShowInBoardShortcut", config.DisableShowInBoardShortcut);
            config.UiThemeMode = ReadIntValue(values, "UiThemeMode", config.UiThemeMode);
            config.SyncMode = (SyncMode)ReadIntValue(values, "SyncMode", (int)config.SyncMode);
            config.SyncBoth = ReadBoolValue(values, "SyncBoth", config.SyncBoth);
            config.BoardWidth = ReadIntValue(values, "BoardWidth", config.BoardWidth);
            config.BoardHeight = ReadIntValue(values, "BoardHeight", config.BoardHeight);
            config.CustomBoardWidth = ReadIntValue(values, "CustomBoardWidth", config.CustomBoardWidth);
            config.CustomBoardHeight = ReadIntValue(values, "CustomBoardHeight", config.CustomBoardHeight);
            config.WindowPosX = ReadIntValue(values, "WindowPosX", config.WindowPosX);
            config.WindowPosY = ReadIntValue(values, "WindowPosY", config.WindowPosY);
        }

        private LegacyMainConfigStatus ApplyLegacyMainConfig(AppConfig config)
        {
            string[] parts = ReadLegacyParts(LegacyMainFileName);
            if (parts == null || parts.Length < 12)
                return LegacyMainConfigStatus.MissingOrInvalid;
            if (!string.Equals(parts[10], machineKey, StringComparison.Ordinal))
                return LegacyMainConfigStatus.MachineMismatch;

            config.BlackOffset = ReadInt(parts[0], config.BlackOffset);
            config.BlackPercent = ReadInt(parts[1], config.BlackPercent);
            config.WhiteOffset = ReadInt(parts[2], config.WhiteOffset);
            config.WhitePercent = ReadInt(parts[3], config.WhitePercent);
            config.UseMagnifier = ReadBool(parts[4], config.UseMagnifier);
            config.VerifyMove = ReadBool(parts[5], config.VerifyMove);
            config.ShowScaleHint = ReadBool(parts[6], config.ShowScaleHint);
            config.ShowInBoard = ReadBool(parts[7], config.ShowInBoard);
            config.ShowInBoardHint = ReadBool(parts[8], config.ShowInBoardHint);
            config.AutoMinimize = ReadBool(parts[9], config.AutoMinimize);
            config.SyncMode = (SyncMode)ReadInt(parts[11], (int)config.SyncMode);
            return LegacyMainConfigStatus.Imported;
        }

        private bool ApplyLegacyOtherConfig(AppConfig config)
        {
            string[] parts = ReadLegacyParts(LegacyOtherFileName);
            if (parts == null || parts.Length < 12 || parts[0] != protocolVersion)
                return false;

            config.BoardWidth = ReadInt(parts[1], config.BoardWidth);
            config.BoardHeight = ReadInt(parts[2], config.BoardHeight);
            config.CustomBoardWidth = ReadInt(parts[3], config.CustomBoardWidth);
            config.CustomBoardHeight = ReadInt(parts[4], config.CustomBoardHeight);
            config.SyncIntervalMs = ReadInt(parts[5], config.SyncIntervalMs);
            config.SyncBoth = ReadBool(parts[6], config.SyncBoth);
            config.GrayOffset = ReadInt(parts[7], config.GrayOffset);
            config.WindowPosX = ReadInt(parts[8], config.WindowPosX);
            config.WindowPosY = ReadInt(parts[9], config.WindowPosY);
            config.UseEnhanceScreen = ReadBool(parts[10], config.UseEnhanceScreen);
            config.PlayPonder = ReadBool(parts[11], config.PlayPonder);
            if (parts.Length >= 14)
            {
                config.DisableShowInBoardShortcut = ReadBool(parts[12], config.DisableShowInBoardShortcut);
                config.UiThemeMode = ReadInt(parts[13], config.UiThemeMode);
            }
            else if (parts.Length >= 13)
            {
                config.UiThemeMode = ReadInt(parts[12], config.UiThemeMode);
            }
            return true;
        }

        private string[] ReadLegacyParts(string fileName)
        {
            string path = GetPath(fileName);
            if (!File.Exists(path))
                return null;
            string line = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(line))
                return null;
            string[] lines = line.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
                return null;
            return lines[0].Split('_');
        }

        private void WriteJsonConfig(AppConfig config)
        {
            string path = GetPath(JsonFileName);
            string tempPath = path + JsonTempSuffix;
            string content = serializer.Serialize(config);
            File.WriteAllText(tempPath, content, Encoding.UTF8);
            try
            {
                ReplaceJsonFile(tempPath, path);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private void WriteLegacyMainConfig(AppConfig config)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(config.BlackOffset);
            builder.Append('_').Append(config.BlackPercent);
            builder.Append('_').Append(config.WhiteOffset);
            builder.Append('_').Append(config.WhitePercent);
            builder.Append('_').Append(ToLegacyBool(config.UseMagnifier));
            builder.Append('_').Append(ToLegacyBool(config.VerifyMove));
            builder.Append('_').Append(ToLegacyBool(config.ShowScaleHint));
            builder.Append('_').Append(ToLegacyBool(config.ShowInBoard));
            builder.Append('_').Append(ToLegacyBool(config.ShowInBoardHint));
            builder.Append('_').Append(ToLegacyBool(config.AutoMinimize));
            builder.Append('_').Append(machineKey);
            builder.Append('_').Append((int)config.SyncMode);
            File.WriteAllText(GetPath(LegacyMainFileName), builder.ToString(), Encoding.UTF8);
        }

        private void WriteLegacyOtherConfig(AppConfig config)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(protocolVersion);
            builder.Append('_').Append(config.BoardWidth);
            builder.Append('_').Append(config.BoardHeight);
            builder.Append('_').Append(config.CustomBoardWidth);
            builder.Append('_').Append(config.CustomBoardHeight);
            builder.Append('_').Append(config.SyncIntervalMs);
            builder.Append('_').Append(ToLegacyBool(config.SyncBoth));
            builder.Append('_').Append(config.GrayOffset);
            builder.Append('_').Append(config.WindowPosX);
            builder.Append('_').Append(config.WindowPosY);
            builder.Append('_').Append(ToLegacyBool(config.UseEnhanceScreen));
            builder.Append('_').Append(ToLegacyBool(config.PlayPonder));
            builder.Append('_').Append(ToLegacyBool(config.DisableShowInBoardShortcut));
            builder.Append('_').Append(config.UiThemeMode);
            File.WriteAllText(GetPath(LegacyOtherFileName), builder.ToString(), Encoding.UTF8);
        }

        private void EnsureConfigMetadata(AppConfig config)
        {
            config.ProtocolVersion = protocolVersion;
            config.MachineKey = machineKey;
        }

        private JsonConfigReadResult RecoverFromInvalidJson(string path)
        {
            QuarantineInvalidJson(path);
            return new JsonConfigReadResult(null, true);
        }

        private void QuarantineInvalidJson(string path)
        {
            string corruptPath = path + JsonCorruptSuffix + Guid.NewGuid().ToString("N");
            File.Move(path, corruptPath);
        }

        private string GetPath(string fileName)
        {
            return Path.Combine(baseDirectory, fileName);
        }

        private static void ReplaceJsonFile(string tempPath, string destinationPath)
        {
            if (File.Exists(destinationPath))
            {
                File.Replace(tempPath, destinationPath, null);
                return;
            }
            File.Move(tempPath, destinationPath);
        }

        private bool IsOwnedByCurrentMachine(AppConfig config)
        {
            return config != null
                && !string.IsNullOrWhiteSpace(config.MachineKey)
                && string.Equals(config.MachineKey, machineKey, StringComparison.Ordinal);
        }

        private static bool IsInvalidJsonException(Exception ex)
        {
            return ex is InvalidOperationException
                || ex is ArgumentException
                || string.Equals(ex.GetType().FullName, "System.Text.Json.JsonException", StringComparison.Ordinal);
        }

        private static int ReadInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, out parsed) ? parsed : fallback;
        }

        private static int ReadIntValue(IDictionary<string, object> values, string key, int fallback)
        {
            object value = ReadJsonValue(values, key);
            if (value == null)
                return fallback;

            return ReadInt(value.ToString(), fallback);
        }

        private static bool ReadBool(string value, bool fallback)
        {
            bool parsedBool;
            int parsed;
            if (bool.TryParse(value, out parsedBool))
                return parsedBool;
            return int.TryParse(value, out parsed) ? parsed == 1 : fallback;
        }

        private static bool ReadBoolValue(IDictionary<string, object> values, string key, bool fallback)
        {
            object value = ReadJsonValue(values, key);
            if (value == null)
                return fallback;

            return ReadBool(value.ToString(), fallback);
        }

        private static string ReadStringValue(IDictionary<string, object> values, string key, string fallback)
        {
            object value = ReadJsonValue(values, key);
            if (value == null)
                return fallback;

            string text = value.ToString();
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }

        private static object ReadJsonValue(IDictionary<string, object> values, string key)
        {
            object value;
            return values != null && values.TryGetValue(key, out value) ? value : null;
        }

        private static string ToLegacyBool(bool value)
        {
            return value ? "1" : "0";
        }
    }
}
