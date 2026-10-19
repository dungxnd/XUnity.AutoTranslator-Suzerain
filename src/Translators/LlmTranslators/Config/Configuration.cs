using System.Globalization;
using System.Reflection;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace XUnity.AutoTranslator.LlmTranslators.Config;

public class LlmConfig
{
    public string? ApiKey { get; set; }
    public bool ApiKeyRequired { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string GlossaryPrompt { get; set; } = string.Empty;
    public int MaxTranslationsPerRequest { get; set; } = 12;
    public int MaxCharactersPerRequest { get; set; } = 4000;
    public int MaxCharactersPerTranslation { get; set; } = 2000;
    public Dictionary<string, object> ModelParams { get; set; } = new Dictionary<string, object>();

    public List<GlossaryLine> Glossary { get; set; } = new List<GlossaryLine>();

    [YamlIgnore]
    public List<GlossaryLine> GlossaryLines { get; set; } = new List<GlossaryLine>();
}

public static class Configuration
{
    public const string DefaultFileName = "LLMConfig.yaml";

    private const int MaxParentSearchDepth = 6;

    private const string DefaultGlossaryPrompt =
        "### Glossary for Consistent Translations\r\n" +
        "Prioritise and use the translation when an exact match with the original text is found.\r\n" +
        "## Terms";

    public static string CalculateConfigFolder()
    {
        var translatorDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (string.IsNullOrEmpty(translatorDirectory))
            throw new InvalidOperationException("Unable to determine the translator directory.");

        return CalculateConfigFolder(translatorDirectory);
    }

    public static string CalculateConfigFolder(string translatorDirectory)
    {
        if (IsNullOrWhiteSpace(translatorDirectory))
            throw new ArgumentException("A translator directory is required.", nameof(translatorDirectory));

        var directory = ToDirectoryInfo(translatorDirectory);
        DirectoryInfo? bepInExDirectory = null;
        DirectoryInfo? unityDataDirectory = null;

        for (var current = directory; current != null; current = current.Parent)
        {
            if (bepInExDirectory == null &&
                string.Equals(current.Name, "BepInEx", StringComparison.OrdinalIgnoreCase))
            {
                bepInExDirectory = current;
            }

            if (unityDataDirectory == null &&
                (current.Name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase) ||
                 current.Name.EndsWith("_ManagedData", StringComparison.OrdinalIgnoreCase)))
            {
                unityDataDirectory = current;
            }

            if (bepInExDirectory != null)
                break;
        }

        if (bepInExDirectory != null)
            return Path.GetFullPath(Path.Combine(bepInExDirectory.FullName, "config"));

        if (unityDataDirectory?.Parent != null)
            return Path.GetFullPath(Path.Combine(unityDataDirectory.Parent.FullName, "AutoTranslator"));

        // Some installs keep the translator DLL outside the game's BepInEx or
        // _Data directory. Look for the conventional sibling folders.
        var searchDepth = 0;
        for (var current = directory; current != null && searchDepth < MaxParentSearchDepth; current = current.Parent, searchDepth++)
        {
            var bepInEx = FindDirectory(current.FullName, "BepInEx");
            if (bepInEx != null)
                return Path.GetFullPath(Path.Combine(bepInEx, "config"));

            var autoTranslator = FindDirectory(current.FullName, "AutoTranslator");
            if (autoTranslator != null)
                return Path.GetFullPath(autoTranslator);
        }

        // Preserve the legacy ReiPatcher layout fallback:
        // <Game>/<Game>_Data/Managed/Translators -> <Game>/AutoTranslator.
        var legacyGameRoot = Path.Combine(Path.Combine(Path.Combine(directory.FullName, ".."), ".."), "..");
        return Path.GetFullPath(Path.Combine(legacyGameRoot, "AutoTranslator"));
    }

    private static DirectoryInfo ToDirectoryInfo(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (File.Exists(fullPath))
        {
            var parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parent))
                fullPath = parent;
        }

        return new DirectoryInfo(fullPath);
    }

    public static IList<string> GetConfigurationSearchDirectories(string translatorDirectory)
    {
        var configFolder = CalculateConfigFolder(translatorDirectory);
        return NormalizeDirectories(new[] { configFolder, translatorDirectory });
    }

    public static string ResolveConfigurationFile(string fileName, IEnumerable<string> searchDirectories)
    {
        if (IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("A configuration file name is required.", nameof(fileName));

        var directories = NormalizeDirectories(searchDirectories);

        if (Path.IsPathRooted(fileName) || ContainsDirectorySeparator(fileName))
        {
            var fullPath = Path.GetFullPath(fileName);
            var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
            var match = FindFile(directory, Path.GetFileName(fullPath));

            if (match != null)
                return match;

            throw new FileNotFoundException(BuildMissingFileMessage(fullPath, new[] { directory }), fullPath);
        }

        var found = FindFileInDirectories(directories, fileName);
        if (found != null)
            return found;

        throw new FileNotFoundException(BuildMissingFileMessage(fileName, directories), fileName);
    }

    public static LlmConfig GetConfiguration(string file)
    {
        if (IsNullOrWhiteSpace(file))
            throw new ArgumentException("A configuration file is required.", nameof(file));

        var fullPath = Path.GetFullPath(file);
        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        var found = FindFile(directory, Path.GetFileName(fullPath));

        if (found == null)
            throw new FileNotFoundException(BuildMissingFileMessage(fullPath, new[] { directory }), fullPath);

        return LoadConfiguration(found, new[] { directory });
    }

    public static LlmConfig GetDefaultConfiguration(string translatorDirectory)
    {
        return GetConfiguration(translatorDirectory, DefaultFileName);
    }

    public static string GetDefaultConfigurationFile(string translatorDirectory)
    {
        var configFolder = CalculateConfigFolder(translatorDirectory);
        return ResolveConfigurationFile(DefaultFileName, NormalizeDirectories(new[] { configFolder, translatorDirectory }));
    }

    public static long GetDefaultConfigurationFingerprint(string translatorDirectory)
    {
        var file = GetDefaultConfigurationFile(translatorDirectory);
        var configFolder = CalculateConfigFolder(translatorDirectory);
        var yamlDirectory = Path.GetDirectoryName(file) ?? configFolder;
        var prefix = Path.GetFileNameWithoutExtension(file);
        var directories = NormalizeDirectories(new[] { yamlDirectory, configFolder, translatorDirectory });

        long fingerprint = 17;
        fingerprint = CombineFingerprint(fingerprint, file);

        foreach (var directory in directories)
        {
            fingerprint = CombineFingerprint(fingerprint, Path.Combine(directory, prefix + "-SystemPrompt.txt"));
            fingerprint = CombineFingerprint(fingerprint, Path.Combine(directory, prefix + "-GlossaryPrompt.txt"));
            fingerprint = CombineFingerprint(fingerprint, Path.Combine(directory, prefix + "-ApiKey.txt"));
            fingerprint = CombineFingerprint(fingerprint, Path.Combine(directory, prefix + "-Glossary.yaml"));
        }

        return fingerprint;
    }

    private static long CombineFingerprint(long fingerprint, string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            return fingerprint;

        fingerprint = (fingerprint * 31) + info.LastWriteTimeUtc.Ticks;
        fingerprint = (fingerprint * 31) + info.Length;
        return fingerprint;
    }

    public static LlmConfig GetConfiguration(string translatorDirectory, string fileName)
    {
        var configFolder = CalculateConfigFolder(translatorDirectory);
        var searchDirectories = NormalizeDirectories(new[] { configFolder, translatorDirectory });
        var file = ResolveConfigurationFile(fileName, searchDirectories);
        var yamlDirectory = Path.GetDirectoryName(file) ?? configFolder;

        var overrideDirectories = NormalizeDirectories(new[]
        {
            yamlDirectory,
            configFolder,
            translatorDirectory
        });

        return LoadConfiguration(file, overrideDirectories);
    }

    private static LlmConfig LoadConfiguration(string file, IList<string> overrideDirectories)
    {
        var deserializer = CreateDeserializer();
        var text = ReadAllText(file);
        LlmConfig? config;

        try
        {
            config = deserializer.Deserialize<LlmConfig>(text);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to read configuration file '{file}': {ex.Message}",
                ex);
        }

        if (config == null)
            throw new InvalidOperationException($"Configuration file '{file}' is empty or invalid.");

        config.GlossaryLines = config.Glossary ?? new List<GlossaryLine>();

        var prefix = Path.GetFileNameWithoutExtension(file);

        var systemPromptFile = FindFileInDirectories(overrideDirectories, $"{prefix}-SystemPrompt.txt");
        if (systemPromptFile != null)
            LoadSystemPrompt(config, systemPromptFile);

        var glossaryPromptFile = FindFileInDirectories(overrideDirectories, $"{prefix}-GlossaryPrompt.txt");
        if (glossaryPromptFile != null)
            LoadGlossaryPrompt(config, glossaryPromptFile);

        var apiKeyFile = FindFileInDirectories(overrideDirectories, $"{prefix}-ApiKey.txt");
        if (apiKeyFile != null)
            LoadApiKey(config, apiKeyFile);

        if (string.IsNullOrEmpty(config.GlossaryPrompt))
            config.GlossaryPrompt = DefaultGlossaryPrompt;

        var glossaryFile = FindFileInDirectories(overrideDirectories, $"{prefix}-Glossary.yaml");
        if (glossaryFile != null && config.GlossaryLines.Count == 0)
            LoadGlossary(config, glossaryFile);

        if (IsNullOrWhiteSpace(config.Url))
            throw new InvalidOperationException(
                $"Configuration file '{file}' is missing the required 'url' field.");

        if (IsNullOrWhiteSpace(config.Model))
            throw new InvalidOperationException(
                $"Configuration file '{file}' is missing the required 'model' field.");

        return config;
    }

    public static bool TrySetConfigurationValue(string translatorDirectory, string key, string value, out string error)
    {
        error = string.Empty;
        if (IsNullOrWhiteSpace(key))
        {
            error = "A setting name is required.";
            return false;
        }

        try
        {
            var file = GetDefaultConfigurationFile(translatorDirectory);

            if (string.Equals(key, "temperature", StringComparison.OrdinalIgnoreCase))
            {
                if (IsRemoveValue(value))
                    return RemoveModelParameter(file, "temperature", out error);

                if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature))
                {
                    error = "Temperature must be a number, or 'default' to remove it.";
                    return false;
                }

                return SetModelParameter(file, "temperature", temperature.ToString(CultureInfo.InvariantCulture), out error);
            }

            if (string.Equals(key, "max_tokens", StringComparison.OrdinalIgnoreCase))
            {
                if (IsRemoveValue(value))
                    return RemoveModelParameter(file, "max_tokens", out error);

                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxTokens) || maxTokens <= 0)
                {
                    error = "Max tokens must be a positive integer, or 'default' to remove it.";
                    return false;
                }

                return SetModelParameter(file, "max_tokens", maxTokens.ToString(CultureInfo.InvariantCulture), out error);
            }

            if (string.Equals(key, "top_p", StringComparison.OrdinalIgnoreCase))
            {
                if (IsRemoveValue(value))
                    return RemoveModelParameter(file, "top_p", out error);

                if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var topP) || topP < 0 || topP > 1)
                {
                    error = "Top P must be a number between 0 and 1, or 'default' to remove it.";
                    return false;
                }

                return SetModelParameter(file, "top_p", topP.ToString(CultureInfo.InvariantCulture), out error);
            }

            if (string.Equals(key, "model", StringComparison.OrdinalIgnoreCase))
            {
                if (IsNullOrWhiteSpace(value))
                {
                    error = "Model cannot be empty.";
                    return false;
                }

                return SetTopLevelValue(file, "model", QuoteYamlString(value.Trim()), out error);
            }

            if (string.Equals(key, "url", StringComparison.OrdinalIgnoreCase))
            {
                var url = value?.Trim() ?? string.Empty;
                if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    error = "URL must be an absolute URL.";
                    return false;
                }

                return SetTopLevelValue(file, "url", QuoteYamlString(url), out error);
            }

            if (string.Equals(key, "maxTranslationsPerRequest", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxTranslations) || maxTranslations < 0)
                {
                    error = "Max translations per request must be a non-negative integer.";
                    return false;
                }

                return SetTopLevelValue(file, "maxTranslationsPerRequest", maxTranslations.ToString(CultureInfo.InvariantCulture), out error);
            }

            if (string.Equals(key, "maxCharactersPerRequest", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxCharacters) || maxCharacters < 0)
                {
                    error = "Max characters per request must be a non-negative integer.";
                    return false;
                }

                return SetTopLevelValue(file, "maxCharactersPerRequest", maxCharacters.ToString(CultureInfo.InvariantCulture), out error);
            }

            if (string.Equals(key, "maxCharactersPerTranslation", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxCharacters) || maxCharacters < 0)
                {
                    error = "Max characters per translation must be a non-negative integer.";
                    return false;
                }

                return SetTopLevelValue(file, "maxCharactersPerTranslation", maxCharacters.ToString(CultureInfo.InvariantCulture), out error);
            }

            error = "Unknown setting '" + key + "'.";
            return false;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is ArgumentException || ex is NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool IsRemoveValue(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length == 0 || string.Equals(normalized, "default", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SetTopLevelValue(string file, string key, string formattedValue, out string error)
    {
        error = string.Empty;
        var lines = ReadAllLines(file);
        var keyPrefix = key + ":";

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]))
                continue;

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase) &&
                (trimmed.Length == keyPrefix.Length || char.IsWhiteSpace(trimmed[keyPrefix.Length])))
            {
                lines[i] = key + ": " + formattedValue;
                WriteAllLines(file, lines);
                return true;
            }
        }

        var insertIndex = FindTopLevelLine(lines, "modelParams");
        var newLine = key + ": " + formattedValue;
        var newLines = new List<string>(lines.Length + 1);
        if (insertIndex < 0)
        {
            newLines.AddRange(lines);
            if (newLines.Count > 0 && newLines[newLines.Count - 1].Length > 0)
                newLines.Add(string.Empty);
            newLines.Add(newLine);
        }
        else
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (i == insertIndex)
                    newLines.Add(newLine);

                newLines.Add(lines[i]);
            }
        }

        WriteAllLines(file, newLines.ToArray());
        return true;
    }

    private static bool SetModelParameter(string file, string key, string formattedValue, out string error)
    {
        error = string.Empty;
        var lines = ReadAllLines(file);
        var modelParamsIndex = FindTopLevelLine(lines, "modelParams");

        if (modelParamsIndex < 0)
        {
            var appended = new List<string>(lines);
            if (appended.Count > 0 && appended[appended.Count - 1].Length > 0)
                appended.Add(string.Empty);

            appended.Add("modelParams:");
            appended.Add("  " + key + ": " + formattedValue);
            WriteAllLines(file, appended.ToArray());
            return true;
        }

        var modelParamsLine = lines[modelParamsIndex];
        var colonIndex = modelParamsLine.IndexOf(':');
        if (colonIndex >= 0)
        {
            var afterColon = modelParamsLine.Substring(colonIndex + 1).Trim();
            if (afterColon.Length > 0 && !afterColon.StartsWith("#", StringComparison.Ordinal))
            {
                error = "modelParams must use block style (one parameter per line) to be edited from the UI.";
                return false;
            }

            if (afterColon.Length > 0)
                lines[modelParamsIndex] = modelParamsLine.Substring(0, colonIndex + 1);
        }

        var blockEnd = FindBlockEnd(lines, modelParamsIndex);
        var childIndent = FindChildIndent(lines, modelParamsIndex, blockEnd);
        if (childIndent == null)
            childIndent = "  ";

        for (var i = modelParamsIndex + 1; i < blockEnd; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || !char.IsWhiteSpace(line[0]))
                continue;

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase) &&
                (trimmed.Length == key.Length + 1 || char.IsWhiteSpace(trimmed[key.Length + 1])))
            {
                lines[i] = childIndent + key + ": " + formattedValue;
                WriteAllLines(file, lines);
                return true;
            }
        }

        var insertAt = blockEnd;
        while (insertAt > modelParamsIndex + 1 && lines[insertAt - 1].Length == 0)
            insertAt--;

        if (insertAt >= lines.Length)
        {
            var appended = new List<string>(lines);
            appended.Add(childIndent + key + ": " + formattedValue);
            WriteAllLines(file, appended.ToArray());
            return true;
        }

        var updated = new List<string>(lines.Length + 1);
        for (var i = 0; i < lines.Length; i++)
        {
            if (i == insertAt)
                updated.Add(childIndent + key + ": " + formattedValue);

            updated.Add(lines[i]);
        }

        WriteAllLines(file, updated.ToArray());
        return true;
    }

    private static bool RemoveModelParameter(string file, string key, out string error)
    {
        error = string.Empty;
        var lines = ReadAllLines(file);
        var modelParamsIndex = FindTopLevelLine(lines, "modelParams");
        if (modelParamsIndex < 0)
            return true;

        var blockEnd = FindBlockEnd(lines, modelParamsIndex);
        for (var i = modelParamsIndex + 1; i < blockEnd; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || !char.IsWhiteSpace(line[0]))
                continue;

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase) &&
                (trimmed.Length == key.Length + 1 || char.IsWhiteSpace(trimmed[key.Length + 1])))
            {
                var updated = new List<string>(lines.Length - 1);
                for (var j = 0; j < lines.Length; j++)
                {
                    if (j != i)
                        updated.Add(lines[j]);
                }

                WriteAllLines(file, updated.ToArray());
                return true;
            }
        }

        return true;
    }

    private static int FindTopLevelLine(string[] lines, string key)
    {
        var keyPrefix = key + ":";
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]))
                continue;

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase) &&
                (trimmed.Length == keyPrefix.Length || char.IsWhiteSpace(trimmed[keyPrefix.Length])))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindBlockEnd(string[] lines, int blockStart)
    {
        for (var i = blockStart + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
                continue;

            if (line[0] == '#' || !char.IsWhiteSpace(line[0]))
                return i;
        }

        return lines.Length;
    }

    private static string? FindChildIndent(string[] lines, int blockStart, int blockEnd)
    {
        for (var i = blockStart + 1; i < blockEnd; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                continue;

            var trimmed = line.TrimStart();
            if (trimmed.Length == line.Length)
                continue;

            return line.Substring(0, line.Length - trimmed.Length);
        }

        return null;
    }

    private static string QuoteYamlString(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static string[] ReadAllLines(string file)
    {
        var text = ReadAllText(file);
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return normalized.Split('\n');
    }

    private static void WriteAllLines(string file, string[] lines)
    {
        var text = string.Join(Environment.NewLine, lines);
        if (!text.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            text += Environment.NewLine;

        var directory = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));

        try
        {
            if (File.Exists(file))
                File.Replace(temp, file, null);
            else
                File.Move(temp, file);
        }
        catch (Exception ex) when (ex is IOException || ex is PlatformNotSupportedException)
        {
            File.Copy(temp, file, true);
            File.Delete(temp);
        }
    }

    public static void LoadGlossary(LlmConfig config, string file)
    {
        var resolvedFile = ResolveExistingFile(file);
        if (resolvedFile == null)
            return;

        var text = ReadAllText(resolvedFile);

        try
        {
            var yamlDeserializer = CreateDeserializer();
            config.GlossaryLines = yamlDeserializer.Deserialize<List<GlossaryLine>>(text) ?? new List<GlossaryLine>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to read glossary file '{resolvedFile}': {ex.Message}",
                ex);
        }
    }

    public static void LoadSystemPrompt(LlmConfig config, string file)
    {
        var resolvedFile = ResolveExistingFile(file);
        if (resolvedFile != null)
            config.SystemPrompt = ReadAllText(resolvedFile);
    }

    public static void LoadGlossaryPrompt(LlmConfig config, string file)
    {
        var resolvedFile = ResolveExistingFile(file);
        if (resolvedFile != null)
            config.GlossaryPrompt = ReadAllText(resolvedFile);
    }

    public static void LoadApiKey(LlmConfig config, string file)
    {
        // Override with file if it exists (for project specific keys)
        var resolvedFile = ResolveExistingFile(file);
        if (resolvedFile != null)
            config.ApiKey = ReadAllText(resolvedFile).Trim();
    }

    private static Deserializer CreateDeserializer()
    {
        return new DeserializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .Build();
    }

    private static string? ResolveExistingFile(string file)
    {
        if (IsNullOrWhiteSpace(file))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(file);
            if (File.Exists(fullPath))
                return fullPath;

            var directory = Path.GetDirectoryName(fullPath);
            return string.IsNullOrEmpty(directory)
                ? null
                : FindFile(directory, Path.GetFileName(fullPath));
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
        {
            return null;
        }
    }

    private static string? FindFileInDirectories(IEnumerable<string> directories, string fileName)
    {
        foreach (var directory in NormalizeDirectories(directories))
        {
            var match = FindFile(directory, fileName);
            if (match != null)
                return match;
        }

        return null;
    }

    private static string? FindFile(string directory, string fileName)
    {
        if (IsNullOrWhiteSpace(directory) || IsNullOrWhiteSpace(fileName))
            return null;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(directory, fileName));
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
        {
            return null;
        }

        if (File.Exists(fullPath))
            return fullPath;

        if (!Directory.Exists(directory))
            return null;

        try
        {
            foreach (var candidate in Directory.GetFiles(directory))
            {
                if (string.Equals(Path.GetFileName(candidate), fileName, StringComparison.OrdinalIgnoreCase))
                    return Path.GetFullPath(candidate);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static string? FindDirectory(string parent, string name)
    {
        if (IsNullOrWhiteSpace(parent) || IsNullOrWhiteSpace(name))
            return null;

        var exactPath = Path.Combine(parent, name);
        if (Directory.Exists(exactPath))
            return exactPath;

        if (!Directory.Exists(parent))
            return null;

        try
        {
            foreach (var candidate in Directory.GetDirectories(parent))
            {
                if (string.Equals(Path.GetFileName(candidate), name, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static bool IsNullOrWhiteSpace(string? value)
    {
        return string.IsNullOrEmpty(value) || value!.Trim().Length == 0;
    }

    private static IList<string> NormalizeDirectories(IEnumerable<string> directories)
    {
        var result = new List<string>();
        if (directories == null)
            return result;

        foreach (var directory in directories)
        {
            if (IsNullOrWhiteSpace(directory))
                continue;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(directory);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                continue;
            }

            if (!result.Any(existing => string.Equals(existing, fullPath, StringComparison.OrdinalIgnoreCase)))
                result.Add(fullPath);
        }

        return result;
    }

    private static bool ContainsDirectorySeparator(string path)
    {
        return path.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
               path.IndexOf(Path.AltDirectorySeparatorChar) >= 0;
    }

    private static string BuildMissingFileMessage(string fileName, IEnumerable<string> directories)
    {
        var searched = directories
            .Select(directory => Path.Combine(directory, fileName))
            .Select(path => "  - " + path);

        return $"Missing configuration file '{fileName}'. Searched:" +
               Environment.NewLine +
               string.Join(Environment.NewLine, searched.ToArray()) +
               Environment.NewLine +
               "Copy the configuration file into the AutoTranslator config folder or next to the translator DLL.";
    }

    private static string ReadAllText(string file)
    {
        try
        {
            return File.ReadAllText(file, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to read '{file}': {ex.Message}", ex);
        }
    }
}
