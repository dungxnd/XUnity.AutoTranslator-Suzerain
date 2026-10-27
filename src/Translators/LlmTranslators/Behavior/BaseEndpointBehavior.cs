using SimpleJSON;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using XUnity.AutoTranslator.LlmTranslators.Config;

namespace XUnity.AutoTranslator.LlmTranslators.Behavior;

public static class BaseEndpointBehavior
{
    private const string BatchInstruction =
        "The user message may contain several blocks marked like <<<1>>>. Each block may contain multiple lines or paragraphs. " +
        "Translate each block separately and preserve its line breaks. Output only the same markers followed by the translation for that block. " +
        "Keep every marker, the original order, and the same number of blocks. Do not repeat the source text, merge blocks, " +
        "split one block into several markers, skip blocks, or add explanations.";

    private static readonly Regex BatchMarkerRegex = new Regex(
        @"^[ \t]*(?:[-*#>]|\d+\.|\*\*)*[ \t]*<{3,}[ \t]*(\d+)[ \t]*>{3,}(?:\*\*)*[:* \t]*(.*?)\r?$",
        RegexOptions.Multiline);

    private static readonly Regex MarkerLikeRegex = new Regex(
        @"^[ \t]*(?:[-*#>]|\d+\.|\*\*)*[ \t]*<{3,}[ \t]*\d+",
        RegexOptions.Multiline);

    private static readonly Regex NumberedLineRegex = new Regex(
        @"^\d+[\.\)\:]\s+",
        RegexOptions.None);

    private static readonly Regex CommonEscapeRegex = new Regex(
        @"\\([nrt\\""'])",
        RegexOptions.None);

    public static string GetRequestData(LlmConfig config, string raw)
    {
        return GetRequestData(config, new[] { raw });
    }

    public static string GetRequestData(LlmConfig config, string[] raws)
    {
        return BuildRequestData(config, raws, false);
    }

    public static string GetOllamaRequestData(LlmConfig config, string[] raws)
    {
        return BuildRequestData(config, raws, true);
    }

    private static string BuildRequestData(LlmConfig config, string[] raws, bool useOllamaOptions)
    {
        var messages = new JSONArray();

        var systemMessage = new JSONObject();
        systemMessage["role"] = "system";
        systemMessage["content"] = BuildSystemPrompt(config, raws);
        messages.Add(systemMessage);

        var userMessage = new JSONObject();
        userMessage["role"] = "user";
        userMessage["content"] = BuildUserContent(raws);
        messages.Add(userMessage);

        var requestBody = new JSONObject
        {
            ["model"] = config.Model,
            ["stream"] = false,
            ["messages"] = messages
        };

        var parameterTarget = useOllamaOptions ? new JSONObject() : requestBody;
        var hasNumPredict = false;

        if (config.ModelParams != null && config.ModelParams.Count > 0)
        {
            foreach (var param in config.ModelParams)
            {
                var key = useOllamaOptions && string.Equals(param.Key, "max_tokens", StringComparison.OrdinalIgnoreCase)
                    ? "num_predict"
                    : param.Key;

                if (useOllamaOptions && string.Equals(key, "num_predict", StringComparison.OrdinalIgnoreCase))
                    hasNumPredict = true;

                SetParameter(parameterTarget, key, param.Value);
            }
        }
        else
        {
            parameterTarget["temperature"] = 0.2;
            parameterTarget["top_p"] = 0.9;
            parameterTarget["frequency_penalty"] = 0;
            parameterTarget["presence_penalty"] = 0;
        }

        if (useOllamaOptions)
        {
            if (!hasNumPredict)
                parameterTarget["num_predict"] = 4096;

            requestBody["options"] = parameterTarget;
        }

        return requestBody.ToString();
    }

    internal static void SetParameter(JSONObject target, string key, object value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var isInt))
            target[key] = isInt;
        else if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var isDecimal))
            target[key] = (double)isDecimal;
        else
            target[key] = text;
    }

    public static bool TryValidateApiKey(LlmConfig config, out string error)
    {
        if (config.ApiKeyRequired && string.IsNullOrEmpty(config.ApiKey))
        {
            error = "The endpoint requires an API key which has not been provided.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryGetSetting(
        LlmConfig? config,
        string key,
        out string value,
        string defaultTemperature = "0.2",
        string defaultMaxTokens = "default")
    {
        value = string.Empty;
        if (config == null)
            return false;

        if (string.Equals(key, "Temperature", StringComparison.OrdinalIgnoreCase))
            return TryGetModelParameter(config, "temperature", defaultTemperature, out value);

        if (string.Equals(key, "max_tokens", StringComparison.OrdinalIgnoreCase))
            return TryGetModelParameter(config, "max_tokens", defaultMaxTokens, out value);

        if (string.Equals(key, "top_p", StringComparison.OrdinalIgnoreCase))
            return TryGetModelParameter(config, "top_p", "0.9", out value);

        if (string.Equals(key, "model", StringComparison.OrdinalIgnoreCase))
        {
            value = config.Model ?? string.Empty;
            return true;
        }

        if (string.Equals(key, "url", StringComparison.OrdinalIgnoreCase))
        {
            value = config.Url ?? string.Empty;
            return true;
        }

        if (string.Equals(key, "maxTranslationsPerRequest", StringComparison.OrdinalIgnoreCase))
        {
            value = config.MaxTranslationsPerRequest.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (string.Equals(key, "maxCharactersPerRequest", StringComparison.OrdinalIgnoreCase))
        {
            value = config.MaxCharactersPerRequest.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (string.Equals(key, "maxCharactersPerTranslation", StringComparison.OrdinalIgnoreCase))
        {
            value = config.MaxCharactersPerTranslation.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static bool TryGetModelParameter(LlmConfig config, string key, string emptyFallback, out string value)
    {
        value = string.Empty;

        if (config.ModelParams != null)
        {
            foreach (var parameter in config.ModelParams)
            {
                if (string.Equals(parameter.Key, key, StringComparison.OrdinalIgnoreCase)
                    && parameter.Value != null)
                {
                    value = Convert.ToString(parameter.Value, CultureInfo.InvariantCulture) ?? string.Empty;
                    return true;
                }
            }
        }

        value = (config.ModelParams == null || config.ModelParams.Count == 0) ? emptyFallback : "default";
        return true;
    }

    public static int GetMaxCharactersPerRequest(LlmConfig? config)
    {
        if (config == null)
            return 4000;

        return config.MaxCharactersPerRequest > 0 ? config.MaxCharactersPerRequest : int.MaxValue;
    }

    public static int GetMaxCharactersPerTranslation(LlmConfig? config)
    {
        if (config == null)
            return 2000;

        return config.MaxCharactersPerTranslation > 0 ? config.MaxCharactersPerTranslation : int.MaxValue;
    }

    public sealed class RequestPlan
    {
        public RequestPlan(string[] originals, string[] chunks, int[] originalChunkCounts, string[] chunkSeparators)
        {
            Originals = originals;
            Chunks = chunks;
            OriginalChunkCounts = originalChunkCounts;
            ChunkSeparators = chunkSeparators;
        }

        public string[] Originals { get; }

        public string[] Chunks { get; }

        public int[] OriginalChunkCounts { get; }

        public string[] ChunkSeparators { get; }
    }

    private sealed class SplitChunk
    {
        public string Text { get; set; } = string.Empty;

        public string Separator { get; set; } = string.Empty;
    }

    public static RequestPlan CreateRequestPlan(LlmConfig config, string[] originals)
    {
        var maxCharacters = config.MaxCharactersPerTranslation;
        var chunks = new List<string>();
        var counts = new int[originals.Length];
        var separators = new List<string>();

        for (var i = 0; i < originals.Length; i++)
        {
            var split = SplitText(originals[i], maxCharacters);
            counts[i] = split.Count;

            for (var c = 0; c < split.Count; c++)
            {
                chunks.Add(split[c].Text);
                separators.Add(split[c].Separator);
            }
        }

        return new RequestPlan(originals, chunks.ToArray(), counts, separators.ToArray());
    }

    private static List<SplitChunk> SplitText(string text, int maxCharacters)
    {
        var result = new List<SplitChunk>();

        if (string.IsNullOrEmpty(text) || maxCharacters <= 0 || text.Length <= maxCharacters)
        {
            result.Add(new SplitChunk { Text = text ?? string.Empty });
            return result;
        }

        var start = 0;
        while (start < text.Length)
        {
            if (text.Length - start <= maxCharacters)
            {
                var tail = text.Substring(start).TrimEnd();
                if (tail.Length > 0)
                    result.Add(new SplitChunk { Text = tail });
                break;
            }

            var limit = start + maxCharacters;
            var split = FindSplitPoint(text, start, limit);
            if (split <= start)
                split = limit;

            var chunkText = text.Substring(start, split - start).TrimEnd();

            var separatorStart = split;
            while (separatorStart < text.Length && char.IsWhiteSpace(text[separatorStart]))
                separatorStart++;

            var separator = text.Substring(split, separatorStart - split);
            if (chunkText.Length > 0)
                result.Add(new SplitChunk { Text = chunkText, Separator = separator });

            start = separatorStart;
        }

        if (result.Count == 0)
            result.Add(new SplitChunk { Text = text });

        return result;
    }

    private static int FindSplitPoint(string text, int start, int limit)
    {
        var paragraph = LastIndexOf(text, "\r\n\r\n", start, limit);
        if (paragraph >= 0) return paragraph;

        paragraph = LastIndexOf(text, "\n\n", start, limit);
        if (paragraph >= 0) return paragraph;

        paragraph = LastIndexOf(text, "\r\r", start, limit);
        if (paragraph >= 0) return paragraph;

        var crlf = LastIndexOf(text, "\r\n", start, limit);
        if (crlf >= 0) return crlf;

        var newline = LastIndexOfCharacter(text, '\n', start, limit);
        if (newline >= 0) return newline;

        newline = LastIndexOfCharacter(text, '\r', start, limit);
        if (newline >= 0) return newline;

        for (var i = limit - 1; i > start; i--)
        {
            var c = text[i];
            if ((c == '.' || c == '!' || c == '?' || c == ';' || c == ':' ||
                 c == '。' || c == '！' || c == '？' || c == '；' || c == '：') &&
                i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]))
            {
                return i + 1;
            }
        }

        for (var i = limit - 1; i > start; i--)
        {
            if (char.IsWhiteSpace(text[i]))
                return i;
        }

        return limit;
    }

    private static int LastIndexOf(string text, string value, int start, int limit)
    {
        var maxStart = limit - value.Length;
        for (var i = maxStart; i >= start; i--)
        {
            var match = true;
            for (var j = 0; j < value.Length; j++)
            {
                if (text[i + j] != value[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return i;
        }

        return -1;
    }

    private static int LastIndexOfCharacter(string text, char value, int start, int limit)
    {
        for (var i = limit - 1; i >= start; i--)
        {
            if (text[i] == value)
                return i;
        }

        return -1;
    }

    private static string BuildSystemPrompt(LlmConfig config, string[] raws)
    {
        var systemPrompt = new StringBuilder(config.SystemPrompt);

        if (raws.Length > 1)
            AppendBlock(systemPrompt, BatchInstruction);

        var glossaryPrompt = ConstructGlossaryPrompt(raws, config);
        if (glossaryPrompt.Length > 0)
            AppendBlock(systemPrompt, glossaryPrompt);

        return systemPrompt.ToString();
    }

    private static void AppendBlock(StringBuilder builder, string block)
    {
        if (builder.Length > 0 && builder[builder.Length - 1] != '\n')
            builder.Append('\n');

        builder.Append(block);
    }

    public static string BuildMarkedContent(string[] raws)
    {
        return BuildUserContent(raws);
    }

    private static string BuildUserContent(string[] raws)
    {
        if (raws.Length == 1)
            return raws[0];

        var marker = CreateMarker(raws);
        var closingMarker = new string('>', marker.Length);
        var content = new StringBuilder();

        for (var i = 0; i < raws.Length; i++)
        {
            if (i > 0)
                content.Append('\n');

            content.Append(marker).Append(i + 1).Append(closingMarker).Append('\n');
            content.Append(raws[i]);

            if (!raws[i].EndsWith("\n", StringComparison.Ordinal))
                content.Append('\n');
        }

        return content.ToString();
    }

    private static string CreateMarker(string[] raws)
    {
        var marker = "<<<";

        while (raws.Any(raw => raw.IndexOf(marker, StringComparison.Ordinal) >= 0))
            marker = "<" + marker;

        return marker;
    }

    public static string ConstructGlossaryPrompt(string raw, LlmConfig config)
    {
        return ConstructGlossaryPrompt(new[] { raw }, config);
    }

    public static List<GlossaryLine> GetMatchingGlossaryLines(string[] raws, LlmConfig config)
    {
        var matchingLines = new List<GlossaryLine>();

        if (config.GlossaryLines == null || raws == null || raws.Length == 0)
            return matchingLines;

        foreach (var line in config.GlossaryLines)
        {
            if (string.IsNullOrEmpty(line.Raw) || string.IsNullOrEmpty(line.Result))
                continue;

            if (raws.Any(raw => raw != null && raw.IndexOf(line.Raw, StringComparison.Ordinal) >= 0))
                matchingLines.Add(line);
        }

        return matchingLines;
    }

    public static string ConstructGlossaryPrompt(string[] raws, LlmConfig config)
    {
        var matchingGlossary = GetMatchingGlossaryLines(raws, config);
        if (matchingGlossary.Count == 0)
            return string.Empty;

        var matchingLines = new StringBuilder();

        foreach (var line in matchingGlossary)
        {
            matchingLines.Append("- ").Append(line.Raw).Append(": ").Append(line.Result).Append('\n');
        }

        var prompt = new StringBuilder(config.GlossaryPrompt);
        if (prompt.Length > 0 && prompt[prompt.Length - 1] != '\n')
            prompt.Append('\n');

        prompt.Append(matchingLines);
        return prompt.ToString();
    }

    public static bool TryParseResponse(string data, out JSONObject? response, out string error)
    {
        response = null;
        error = string.Empty;

        if (IsNullOrWhiteSpace(data))
        {
            error = "The translation service returned an empty response.";
            return false;
        }

        try
        {
            response = JSON.Parse(data) as JSONObject;
        }
        catch (Exception ex)
        {
            error = "The translation service returned invalid JSON: " + ex.Message;
            return false;
        }

        if (response == null)
        {
            error = "The translation service returned an unexpected response.";
            return false;
        }

        var errorNode = response["error"];
        if (errorNode != null)
        {
            var message = errorNode.Value;
            if (string.IsNullOrEmpty(message) && errorNode is JSONObject errorObject)
                message = errorObject["message"]?.Value;

            if (!string.IsNullOrEmpty(message))
            {
                error = "The translation service returned an error: " + message;
                return false;
            }
        }

        return true;
    }

    public static string StripThinkBlock(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        var end = content.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (end >= 0)
            content = content.Substring(end + "</think>".Length);

        content = content.Trim();

        if (content.StartsWith("<think>", StringComparison.OrdinalIgnoreCase))
            content = content.Substring("<think>".Length).Trim();

        return content;
    }

    public static string[]? ExtractTranslations(string content, string[] untranslatedTexts, LlmConfig config)
    {
        if (untranslatedTexts.Length == 0 || IsNullOrWhiteSpace(content))
            return null;

        content = StripCodeFences(content);

        if (untranslatedTexts.Length == 1)
        {
            if (MarkerLikeRegex.IsMatch(content))
            {
                var marked = TryParseMarkers(content, 1);
                if (marked != null)
                    return CleanupTranslations(marked, untranslatedTexts, config);

                content = StripBatchMarkers(content);
            }

            return CleanupTranslations(new[] { content }, untranslatedTexts, config);
        }

        if (MarkerLikeRegex.IsMatch(content))
        {
            var markedBatch = TryParseMarkers(content, untranslatedTexts.Length);
            return markedBatch == null
                ? null
                : CleanupTranslations(markedBatch, untranslatedTexts, config);
        }

        var jsonBatch = TryParseJson(content, untranslatedTexts.Length);
        if (jsonBatch != null)
            return CleanupTranslations(jsonBatch, untranslatedTexts, config);

        var lineBatch = TryParseLines(content, untranslatedTexts.Length);
        if (lineBatch != null)
            return CleanupTranslations(lineBatch, untranslatedTexts, config);

        return null;
    }

    public static string[]? ExtractPlannedTranslations(string content, RequestPlan? plan, string[] originals, LlmConfig config)
    {
        if (plan == null)
            return ExtractTranslations(content, originals, config);

        var chunkTranslations = ExtractTranslations(content, plan.Chunks, config);
        if (chunkTranslations == null)
        {
            if (plan.Originals.Length > 1)
            {
                var originalTranslations = ExtractTranslations(content, plan.Originals, config);
                if (originalTranslations != null)
                    return originalTranslations;
            }
            else if (plan.Originals.Length == 1)
            {
                var single = ExtractTranslations(StripBatchMarkers(content), new[] { plan.Originals[0] }, config);
                if (single != null)
                    return single;
            }

            return null;
        }

        var results = new string[plan.Originals.Length];
        var chunkIndex = 0;

        for (var i = 0; i < plan.Originals.Length; i++)
        {
            var count = plan.OriginalChunkCounts[i];
            var builder = new StringBuilder();

            for (var c = 0; c < count; c++)
            {
                builder.Append(chunkTranslations[chunkIndex]);
                if (c + 1 < count)
                    builder.Append(plan.ChunkSeparators[chunkIndex]);

                chunkIndex++;
            }

            results[i] = builder.ToString();
        }

        return results;
    }

    private static string StripBatchMarkers(string content)
    {
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var builder = new StringBuilder();

        foreach (var line in lines)
        {
            var match = BatchMarkerRegex.Match(line);
            if (!match.Success)
            {
                if (builder.Length > 0) builder.Append('\n');
                builder.Append(line);
                continue;
            }

            var inline = match.Groups[2].Value.Trim();
            if (inline.Length > 0)
            {
                if (builder.Length > 0) builder.Append('\n');
                builder.Append(inline);
            }
        }

        return builder.ToString().Trim();
    }

    private static string[]? CleanupTranslations(string[] translations, string[] raws, LlmConfig config)
    {
        var results = new string[translations.Length];

        for (var i = 0; i < translations.Length; i++)
        {
            var result = ValidateAndCleanupTranslation(raws[i], translations[i], config);
            if (string.IsNullOrEmpty(result))
                return null;

            result = StripBatchMarkers(result);
            if (string.IsNullOrEmpty(result))
                return null;

            results[i] = result;
        }

        return results;
    }

    private static string[]? TryParseMarkers(string content, int expectedCount)
    {
        var matches = BatchMarkerRegex.Matches(content);
        if (matches.Count != expectedCount)
            return null;

        var results = new string[expectedCount];
        var seen = new bool[expectedCount];

        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                return null;

            if (index < 1 || index > expectedCount || seen[index - 1])
                return null;

            var start = match.Index + match.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
            var block = content.Substring(start, end - start).Trim('\r', '\n');
            var inline = match.Groups[2].Value.Trim();

            if (inline.Length > 0)
                block = block.Length > 0 ? inline + "\n" + block : inline;

            results[index - 1] = block.Trim();
            seen[index - 1] = true;
        }

        return results;
    }

    private static string[]? TryParseJson(string content, int expectedCount)
    {
        JSONNode? root;
        try
        {
            root = JSON.Parse(content);
        }
        catch
        {
            return null;
        }

        if (root is JSONArray array)
        {
            if (array.Count != expectedCount)
                return null;

            var results = new string[expectedCount];
            for (var i = 0; i < expectedCount; i++)
            {
                var value = array[i]?.Value;
                if (value == null)
                    return null;
                results[i] = value;
            }

            return results;
        }

        if (root is JSONObject obj)
        {
            var translations = obj["translations"] as JSONArray;
            if (translations != null && translations.Count == expectedCount)
            {
                var results = new string[expectedCount];
                for (var i = 0; i < expectedCount; i++)
                {
                    var value = translations[i]?.Value;
                    if (value == null)
                        return null;
                    results[i] = value;
                }

                return results;
            }

            var keyedResults = new string[expectedCount];
            for (var i = 0; i < expectedCount; i++)
            {
                var value = obj[(i + 1).ToString(CultureInfo.InvariantCulture)]?.Value;
                if (value == null)
                    return null;
                keyedResults[i] = value;
            }

            return keyedResults;
        }

        return null;
    }

    private static string[]? TryParseLines(string content, int expectedCount)
    {
        var lines = content
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        if (lines.Length != expectedCount)
            return null;

        if (lines.All(line => NumberedLineRegex.IsMatch(line)))
            lines = lines.Select(line => NumberedLineRegex.Replace(line, string.Empty, 1)).ToArray();

        return lines;
    }

    private static string StripCodeFences(string content)
    {
        content = content.Trim();
        if (!content.StartsWith("```", StringComparison.Ordinal))
            return content;

        var firstNewLine = content.IndexOf('\n');
        var lastFence = content.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewLine < 0 || lastFence <= firstNewLine)
            return content;

        return content.Substring(firstNewLine + 1, lastFence - firstNewLine - 1).Trim();
    }

    public static string ValidateAndCleanupTranslation(string raw, string result, LlmConfig config)
    {
        if (string.IsNullOrEmpty(result))
            return string.Empty;

        result = result.Trim();
        result = StripCodeFences(result);

        if (result.Length >= 2 &&
            ((result[0] == '"' && result[result.Length - 1] == '"') ||
             (result[0] == '\'' && result[result.Length - 1] == '\'')))
        {
            result = result.Substring(1, result.Length - 2).Trim();
        }

        result = result
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'');

        result = UnescapeCommon(result);

        if (result.Length > 0 && char.IsLower(result[0]) && raw != result)
            result = char.ToUpperInvariant(result[0]) + result.Substring(1);

        return result.Trim();
    }

    private static bool IsNullOrWhiteSpace(string value)
    {
        return string.IsNullOrEmpty(value) || value.Trim().Length == 0;
    }

    private static string UnescapeCommon(string value)
    {
        if (value.IndexOf('\\') < 0)
            return value;

        return CommonEscapeRegex.Replace(value, match =>
        {
            switch (match.Groups[1].Value[0])
            {
                case 'n':
                    return "\n";
                case 'r':
                    return "\r";
                case 't':
                    return "\t";
                case '\\':
                    return "\\";
                case '"':
                    return "\"";
                default:
                    return "'";
            }
        });
    }
}
