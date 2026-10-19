using SimpleJSON;
using XUnity.AutoTranslator.LlmTranslators.Behavior;
using XUnity.AutoTranslator.LlmTranslators.Config;

namespace XUnity.AutoTranslator.LlmTranslators.Tests;

public class BatchBehaviorTests
{
    private static LlmConfig CreateConfig()
    {
        return new LlmConfig
        {
            Model = "test-model",
            SystemPrompt = "Translate.",
            GlossaryPrompt = "GLOSSARY_HEADER",
            MaxTranslationsPerRequest = 5
        };
    }

    [Fact]
    public void DefaultModelParametersAreIncludedWhenNoneAreConfigured()
    {
        var payload = JSON.Parse(BaseEndpointBehavior.GetRequestData(CreateConfig(), new[] { "hello" }));

        Assert.Equal("0.2", payload["temperature"].Value);
        Assert.Equal("0.9", payload["top_p"].Value);
    }

    [Fact]
    public void BatchRequestSendsOneSystemMessage()
    {
        var config = CreateConfig();

        var payload = JSON.Parse(BaseEndpointBehavior.GetRequestData(config, new[] { "甲", "乙", "丙" }));
        var messages = payload["messages"].AsArray;

        Assert.NotNull(messages);
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0]["role"].Value);
        Assert.Equal("user", messages[1]["role"].Value);

        var system = messages[0]["content"].Value;
        Assert.Contains("blocks marked like <<<1>>>", system);

        var user = messages[1]["content"].Value;
        Assert.Contains("<<<1>>>", user);
        Assert.Contains("<<<2>>>", user);
        Assert.Contains("<<<3>>>", user);
    }

    [Fact]
    public void GlossaryIsIncludedOnceWhenAnyBlockMatches()
    {
        var config = CreateConfig();
        config.GlossaryLines = new List<GlossaryLine> { new("foo", "bar") };

        var payload = JSON.Parse(BaseEndpointBehavior.GetRequestData(config, new[] { "foo one", "foo two", "none" }));
        var system = payload["messages"][0]["content"].Value;

        Assert.Contains("GLOSSARY_HEADER", system);
        Assert.Equal(1, CountOccurrences(system, "- foo: bar"));
    }

    [Fact]
    public void GlossaryIsOmittedWhenNoBlockMatches()
    {
        var config = CreateConfig();
        config.GlossaryLines = new List<GlossaryLine> { new("foo", "bar") };

        var payload = JSON.Parse(BaseEndpointBehavior.GetRequestData(config, new[] { "one", "two" }));
        var system = payload["messages"][0]["content"].Value;

        Assert.DoesNotContain("GLOSSARY_HEADER", system);
    }

    [Fact]
    public void ParsesMarkerBlocksInOrder()
    {
        var result = BaseEndpointBehavior.ExtractTranslations(
            "<<<1>>>\nHello\n<<<2>>>\nWorld",
            new[] { "你好", "世界" },
            CreateConfig());

        Assert.Equal(new[] { "Hello", "World" }, result);
    }

    [Fact]
    public void ParsesInlineMarkerBlocks()
    {
        var result = BaseEndpointBehavior.ExtractTranslations(
            "<<<1>>> Hello\n<<<2>>> World",
            new[] { "你好", "世界" },
            CreateConfig());

        Assert.Equal(new[] { "Hello", "World" }, result);
    }

    [Fact]
    public void ParsesJsonArrayFallback()
    {
        var result = BaseEndpointBehavior.ExtractTranslations(
            "[\"Hello\",\"World\"]",
            new[] { "你好", "世界" },
            CreateConfig());

        Assert.Equal(new[] { "Hello", "World" }, result);
    }

    [Fact]
    public void ParsesNumberedLineFallback()
    {
        var result = BaseEndpointBehavior.ExtractTranslations(
            "1. Hello\n2. World",
            new[] { "你好", "世界" },
            CreateConfig());

        Assert.Equal(new[] { "Hello", "World" }, result);
    }

    [Fact]
    public void ParsesSingleMarkerResponse()
    {
        var result = BaseEndpointBehavior.ExtractTranslations(
            "<<<1>>>\nHello",
            new[] { "你好" },
            CreateConfig());

        Assert.Equal(new[] { "Hello" }, result);
    }

    [Fact]
    public void SingleTranslationKeepsMarkerTextInTheMiddle()
    {
        var result = BaseEndpointBehavior.ExtractTranslations(
            "Use <<<1>>> in code",
            new[] { "raw" },
            CreateConfig());

        Assert.Equal(new[] { "Use <<<1>>> in code" }, result);
    }

    [Fact]
    public void RejectsMalformedMarkerBatch()
    {
        var result = BaseEndpointBehavior.ExtractTranslations(
            "<<<1>>>\nHello",
            new[] { "你好", "世界" },
            CreateConfig());

        Assert.Null(result);
    }

    [Fact]
    public void EscalatesMarkersWhenInputContainsMarkerPrefix()
    {
        var config = CreateConfig();
        var raws = new[] { "A <<< B", "C" };

        var payload = JSON.Parse(BaseEndpointBehavior.GetRequestData(config, raws));
        var user = payload["messages"][1]["content"].Value;

        Assert.Contains("<<<<1>>>>", user);
        Assert.Contains("<<<<2>>>>", user);

        var result = BaseEndpointBehavior.ExtractTranslations(
            "<<<<1>>>>\nA\n<<<<2>>>>\nC",
            raws,
            config);

        Assert.Equal(new[] { "A", "C" }, result);
    }

    [Fact]
    public void CleanupPreservesUnknownEscapes()
    {
        var result = BaseEndpointBehavior.ValidateAndCleanupTranslation("raw", "Use \\= here", new LlmConfig());

        Assert.Equal("Use \\= here", result);
    }

    [Fact]
    public void CleanupDoesNotThrowOnUnrecognizedEscapes()
    {
        var result = BaseEndpointBehavior.ValidateAndCleanupTranslation("raw", "Use \\q here", new LlmConfig());

        Assert.Equal("Use \\q here", result);
    }

    [Fact]
    public void CleanupUnescapesCommonSequences()
    {
        var result = BaseEndpointBehavior.ValidateAndCleanupTranslation("raw", "line1\\nline2", new LlmConfig());

        Assert.Equal("Line1\nline2", result);
    }

    [Fact]
    public void CleanupHandlesEmptyResponse()
    {
        var result = BaseEndpointBehavior.ValidateAndCleanupTranslation("raw", string.Empty, new LlmConfig());

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ResponseErrorIsReported()
    {
        var parsed = BaseEndpointBehavior.TryParseResponse(
            "{\"error\":{\"message\":\"bad key\"}}",
            out _,
            out var error);

        Assert.False(parsed);
        Assert.Contains("bad key", error);
    }

    [Fact]
    public void OllamaRequestNestsModelParamsUnderOptionsAndMapsMaxTokens()
    {
        var config = CreateConfig();
        config.ModelParams = new Dictionary<string, object>
        {
            ["temperature"] = 0.2m,
            ["max_tokens"] = 4096
        };

        var payload = Assert.IsType<JSONObject>(JSON.Parse(BaseEndpointBehavior.GetOllamaRequestData(config, new[] { "hello" })));
        var options = payload["options"];

        Assert.NotNull(options);
        Assert.Equal("4096", options["num_predict"].Value);
        Assert.Equal("0.2", options["temperature"].Value);

        var topLevelKeys = new HashSet<string>();
        foreach (var pair in payload)
            topLevelKeys.Add(pair.Key);

        Assert.DoesNotContain("max_tokens", topLevelKeys);
        Assert.DoesNotContain("temperature", topLevelKeys);
    }

    [Fact]
    public void OllamaDefaultRequestSetsNumPredict()
    {
        var config = CreateConfig();

        var payload = Assert.IsType<JSONObject>(JSON.Parse(BaseEndpointBehavior.GetOllamaRequestData(config, new[] { "hello" })));
        var options = payload["options"];

        Assert.Equal("4096", options["num_predict"].Value);
        Assert.Equal("0.2", options["temperature"].Value);
    }

    [Fact]
    public void RequestPlanSplitsLongTextAtParagraphs()
    {
        var config = CreateConfig();
        config.MaxCharactersPerTranslation = 40;

        var text = "First paragraph.\n\nSecond paragraph.\n\nThird paragraph.";
        var plan = BaseEndpointBehavior.CreateRequestPlan(config, new[] { text });

        Assert.True(plan.Chunks.Length > 1);
        Assert.All(plan.Chunks, chunk => Assert.True(chunk.Length <= 40));
        Assert.Equal(text, plan.Originals[0]);
        Assert.Equal(plan.Chunks.Length, plan.OriginalChunkCounts[0]);
    }

    [Fact]
    public void RequestPlanReassemblesChunkTranslationsWithParagraphBreaks()
    {
        var config = CreateConfig();
        config.MaxCharactersPerTranslation = 40;

        var text = "First paragraph.\n\nSecond paragraph.\n\nThird paragraph.";
        var plan = BaseEndpointBehavior.CreateRequestPlan(config, new[] { text });

        var response = new System.Text.StringBuilder();
        for (var i = 0; i < plan.Chunks.Length; i++)
        {
            if (i > 0) response.Append('\n');
            response.Append("<<<").Append(i + 1).Append(">>>\n");
            response.Append("Translated ").Append(i + 1).Append('.');
        }

        var result = BaseEndpointBehavior.ExtractPlannedTranslations(
            response.ToString(),
            plan,
            new[] { text },
            config);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Contains("Translated 1.", result![0]);
        Assert.Contains("Translated 2.", result[0]);
        Assert.Contains("\n\n", result[0]);
    }

    [Fact]
    public void RequestPlanFallsBackWhenModelIgnoresChunkMarkers()
    {
        var config = CreateConfig();
        config.MaxCharactersPerTranslation = 40;

        var text = "First paragraph.\n\nSecond paragraph.\n\nThird paragraph.";
        var plan = BaseEndpointBehavior.CreateRequestPlan(config, new[] { text });

        var result = BaseEndpointBehavior.ExtractPlannedTranslations(
            "Premier paragraphe.\n\nDeuxieme paragraphe.\n\nTroisieme paragraphe.",
            plan,
            new[] { text },
            config);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.StartsWith("Premier", result![0]);
        Assert.Contains("\n\n", result[0]);
    }

    [Fact]
    public void ParsesSingleMarkerResponseWithPreamble()
    {
        var result = BaseEndpointBehavior.ExtractTranslations(
            "Here is the translation:\n<<<1>>>\nHello",
            new[] { "你好" },
            CreateConfig());

        Assert.Equal(new[] { "Hello" }, result);
    }

    [Fact]
    public void BatchLimitsTreatNonPositiveValuesAsUnlimited()
    {
        Assert.Equal(4000, BaseEndpointBehavior.GetMaxCharactersPerRequest(null));
        Assert.Equal(2000, BaseEndpointBehavior.GetMaxCharactersPerTranslation(null));
        Assert.Equal(int.MaxValue, BaseEndpointBehavior.GetMaxCharactersPerRequest(new LlmConfig { MaxCharactersPerRequest = 0 }));
        Assert.Equal(int.MaxValue, BaseEndpointBehavior.GetMaxCharactersPerTranslation(new LlmConfig { MaxCharactersPerTranslation = 0 }));
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var index = 0;

        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
