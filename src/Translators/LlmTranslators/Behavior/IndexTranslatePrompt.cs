using System;
using System.Collections.Generic;
using System.Text;
using SimpleJSON;
using XUnity.AutoTranslator.LlmTranslators.Config;

namespace XUnity.AutoTranslator.LlmTranslators.Behavior;

public static class IndexTranslatePrompt
{
    public const string DefaultTemperature = "0";
    public const string DefaultMaxTokens = "4096";

    private static readonly Dictionary<string, string> LanguageNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "en", "英语" },
            { "zh", "中文" },
            { "zh-tw", "繁体中文" },
            { "zh-hant", "繁体中文" },
            { "zh-hk", "繁体中文" },
            { "de", "德语" },
            { "fr", "法语" },
            { "es", "西班牙语" },
            { "ja", "日语" },
            { "ko", "韩语" },
            { "pt", "葡萄牙语" },
            { "ru", "俄语" },
            { "ar", "阿拉伯语" },
            { "it", "意大利语" },
            { "nl", "荷兰语" },
            { "pl", "波兰语" },
            { "ro", "罗马尼亚语" },
            { "sv", "瑞典语" },
            { "tr", "土耳其语" },
            { "hi", "印地语" },
            { "vi", "越南语" },
            { "th", "泰语" },
            { "id", "印尼语" },
            { "ms", "马来语" },
            { "fil", "菲律宾语" },
            { "ukr_cyrl", "乌克兰语" },
            { "fas_arab", "波斯语" },
            { "ces_latn", "捷克语" },
            { "ell_grek", "希腊语" },
            { "dan_latn", "丹麦语" },
            { "hun_latn", "匈牙利语" },
            { "fin_latn", "芬兰语" },
            { "nob_latn", "书面挪威语" },
            { "slk_latn", "斯洛伐克语" },
            { "bul_cyrl", "保加利亚语" }
        };

    public static string BuildPrompt(string[] chunks, string sourceLanguage, string destinationLanguage, LlmConfig config)
    {
        if (chunks == null || chunks.Length == 0)
            return string.Empty;

        var targetName = GetLanguageName(destinationLanguage);
        var sourceName = GetLanguageName(sourceLanguage);
        var matchingGlossary = BaseEndpointBehavior.GetMatchingGlossaryLines(chunks, config);
        var isBatch = chunks.Length > 1;

        if (!isBatch && matchingGlossary.Count == 0)
        {
            var subject = sourceName.Length > 0 ? sourceName + "文本" : "文本";
            return "请将以下" + subject + "翻译为" + targetName + "，直接输出翻译结果，不要进行任何解释。\n\n"
                + (chunks[0] ?? string.Empty).Trim();
        }

        var constraints = new List<string>();

        if (isBatch)
        {
            constraints.Add(
                "【硬性要求】每个以 <<<n>>> 标记的文本块必须单独翻译，输出相同的标记和对应的译文，保持原来的顺序、块数和换行。" +
                "不要合并、拆分、跳过文本块，也不要添加说明。");
        }

        if (matchingGlossary.Count > 0)
        {
            var terms = new StringBuilder();

            foreach (var line in matchingGlossary)
            {
                if (terms.Length > 0)
                    terms.Append('、');

                terms.Append(line.Raw).Append('→').Append(line.Result);
            }

            constraints.Add("【硬性要求】专名/术语对照: " + terms);
        }

        var headerSubject = sourceName.Length > 0 ? sourceName + "文本" : "文本";
        var sourceText = isBatch
            ? BaseEndpointBehavior.BuildMarkedContent(chunks).Trim()
            : (chunks[0] ?? string.Empty).Trim();

        var builder = new StringBuilder();
        builder.Append("请将以下").Append(headerSubject).Append("翻译成").Append(targetName)
            .Append("，并且严格遵循所有约束要求。\n\n");
        builder.Append("【源文】\n").Append(sourceText).Append("\n\n");
        builder.Append("【约束要求】\n");

        for (var i = 0; i < constraints.Count; i++)
        {
            builder.Append(i + 1).Append(". ").Append(constraints[i]).Append('\n');
        }

        builder.Append("\n只输出译文，不要有任何额外说明。");
        return builder.ToString();
    }

    public static string GetRequestData(LlmConfig config, string prompt)
    {
        var messages = new JSONArray();

        var userMessage = new JSONObject();
        userMessage["role"] = "user";
        userMessage["content"] = prompt;
        messages.Add(userMessage);

        var requestBody = new JSONObject
        {
            ["model"] = config.Model,
            ["stream"] = false,
            ["messages"] = messages,
            ["chat_template_kwargs"] = new JSONObject
            {
                ["enable_thinking"] = false
            }
        };

        var hasTemperature = false;
        var hasMaxTokens = false;

        if (config.ModelParams != null)
        {
            foreach (var parameter in config.ModelParams)
            {
                if (string.Equals(parameter.Key, "temperature", StringComparison.OrdinalIgnoreCase))
                    hasTemperature = true;
                else if (string.Equals(parameter.Key, "max_tokens", StringComparison.OrdinalIgnoreCase))
                    hasMaxTokens = true;

                BaseEndpointBehavior.SetParameter(requestBody, parameter.Key, parameter.Value);
            }
        }

        if (!hasTemperature)
            requestBody["temperature"] = 0.0;

        if (!hasMaxTokens)
            requestBody["max_tokens"] = 4096;

        return requestBody.ToString();
    }

    private static string GetLanguageName(string language)
    {
        if (string.IsNullOrEmpty(language))
            return string.Empty;

        var value = language.Trim();
        if (value.Length == 0 || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        if (LanguageNames.TryGetValue(value, out var name))
            return name;

        var separator = value.IndexOf('-');
        if (separator < 0)
            separator = value.IndexOf('_');

        if (separator > 0)
        {
            var baseCode = value.Substring(0, separator);
            if (LanguageNames.TryGetValue(baseCode, out name))
                return name;
        }

        return value;
    }
}
