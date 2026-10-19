using System.Globalization;
using System.Net;
using System.Reflection;
using SimpleJSON;
using XUnity.AutoTranslator.LlmTranslators.Config;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using XUnity.AutoTranslator.Plugin.Core.Endpoints.Http;
using XUnity.AutoTranslator.Plugin.Core.Web;

namespace XUnity.AutoTranslator.LlmTranslators.Tests;

public class ConfigTests
{
    private const string IndexPublicUrl = "https://index-translate.bilibili.com/v1/chat/completions";
    private const string IndexPublicModel = "Index-Translate-35B-A3B";

    private static string SampleConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "SampleConfig", "LLMConfig.yaml");

    [Fact]
    public void SampleConfigLoads()
    {
        var config = Configuration.GetConfiguration(SampleConfigPath);

        Assert.Equal("http://localhost:1234/v1/chat/completions", config.Url);
        Assert.Equal("model-identifier", config.Model);
        Assert.False(config.ApiKeyRequired);
        Assert.Equal(12, config.MaxTranslationsPerRequest);
        Assert.Equal(4000, config.MaxCharactersPerRequest);
        Assert.Equal(2000, config.MaxCharactersPerTranslation);
        Assert.Contains("Translate", config.SystemPrompt);
    }

    [Fact]
    public void DefaultBatchSizeAmortizesSystemPrompt()
    {
        var config = new LlmConfig();

        Assert.Equal(12, config.MaxTranslationsPerRequest);
        Assert.Equal(4000, config.MaxCharactersPerRequest);
        Assert.Equal(2000, config.MaxCharactersPerTranslation);
    }

    [Fact]
    public void DefaultConfigFileNameIsShared()
    {
        Assert.Equal("LLMConfig.yaml", Configuration.DefaultFileName);
    }

    [Fact]
    public void CalculateConfigFolderUsesBepInExConfigDirectory()
    {
        var gameDirectory = Path.Combine(Path.GetTempPath(), $"한글 게임 폴더-{Guid.NewGuid():N}");
        var translatorDirectory = Path.Combine(
            gameDirectory,
            "BepInEx",
            "plugins",
            "XUnity.AutoTranslator",
            "Translators");

        try
        {
            Directory.CreateDirectory(translatorDirectory);

            var folder = Configuration.CalculateConfigFolder(translatorDirectory);

            Assert.Equal(Path.Combine(gameDirectory, "BepInEx", "config"), folder);
        }
        finally
        {
            if (Directory.Exists(gameDirectory))
                Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void CalculateConfigFolderPreservesReiPatcherLayout()
    {
        var gameDirectory = Path.Combine(Path.GetTempPath(), "ReiPatcher Game");
        var translatorDirectory = Path.Combine(
            gameDirectory,
            "ReiPatcher Game_Data",
            "Managed",
            "Translators");

        var folder = Configuration.CalculateConfigFolder(translatorDirectory);

        Assert.Equal(Path.Combine(gameDirectory, "AutoTranslator"), folder);
    }

    [Fact]
    public void CalculateConfigFolderFindsBepInExSiblingForCustomTranslatorDirectory()
    {
        var gameDirectory = Path.Combine(Path.GetTempPath(), $"LlmConfigSibling-{Guid.NewGuid():N}");
        var translatorDirectory = Path.Combine(gameDirectory, "Translators");

        try
        {
            Directory.CreateDirectory(translatorDirectory);
            Directory.CreateDirectory(Path.Combine(gameDirectory, "BepInEx", "plugins"));

            var folder = Configuration.CalculateConfigFolder(translatorDirectory);

            Assert.Equal(Path.Combine(gameDirectory, "BepInEx", "config"), folder);
        }
        finally
        {
            if (Directory.Exists(gameDirectory))
                Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void CalculateConfigFolderFindsAutoTranslatorSiblingForCustomTranslatorDirectory()
    {
        var gameDirectory = Path.Combine(Path.GetTempPath(), $"LlmConfigSibling-{Guid.NewGuid():N}");
        var translatorDirectory = Path.Combine(gameDirectory, "Translators");

        try
        {
            Directory.CreateDirectory(translatorDirectory);
            Directory.CreateDirectory(Path.Combine(gameDirectory, "AutoTranslator"));

            var folder = Configuration.CalculateConfigFolder(translatorDirectory);

            Assert.Equal(Path.Combine(gameDirectory, "AutoTranslator"), folder);
        }
        finally
        {
            if (Directory.Exists(gameDirectory))
                Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void CalculateConfigFolderAcceptsTranslatorFilePath()
    {
        var gameDirectory = Path.Combine(Path.GetTempPath(), $"LlmConfigFile-{Guid.NewGuid():N}");
        var translatorDirectory = Path.Combine(
            gameDirectory,
            "BepInEx",
            "plugins",
            "XUnity.AutoTranslator",
            "Translators");
        var translatorFile = Path.Combine(translatorDirectory, "XUnity.AutoTranslator.LlmTranslators.dll");

        try
        {
            Directory.CreateDirectory(translatorDirectory);
            File.WriteAllText(translatorFile, string.Empty);

            var folder = Configuration.CalculateConfigFolder(translatorFile);

            Assert.Equal(Path.Combine(gameDirectory, "BepInEx", "config"), folder);
        }
        finally
        {
            if (Directory.Exists(gameDirectory))
                Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void GetDefaultConfigurationFallsBackToTranslatorDirectory()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out _);

        try
        {
            File.WriteAllText(
                Path.Combine(translatorDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: translator-fallback\n");

            var config = Configuration.GetDefaultConfiguration(translatorDirectory);

            Assert.Equal("translator-fallback", config.Model);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void GetDefaultConfigurationPrefersConfigFolderYaml()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: config-folder\n");
            File.WriteAllText(
                Path.Combine(translatorDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: translator-folder\n");

            var config = Configuration.GetDefaultConfiguration(translatorDirectory);

            Assert.Equal("config-folder", config.Model);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void GetDefaultConfigurationMatchesYamlFileNameCaseInsensitively()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, "llmconfig.yaml"),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: case-insensitive\n");

            var config = Configuration.GetDefaultConfiguration(translatorDirectory);

            Assert.Equal("case-insensitive", config.Model);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void GetDefaultConfigurationFindsOverrideInConfigFolderWhenYamlIsInTranslatorDirectory()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(translatorDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: override-test\n");
            File.WriteAllText(
                Path.Combine(configDirectory, "LLMConfig-SystemPrompt.txt"),
                "Config folder prompt");

            var config = Configuration.GetDefaultConfiguration(translatorDirectory);

            Assert.Equal("Config folder prompt", config.SystemPrompt);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void GetDefaultConfigurationHandlesRelativeFileNameWithoutDirectory()
    {
        var originalCurrentDirectory = Directory.GetCurrentDirectory();
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"LlmConfigRelative-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(workingDirectory);
            Directory.SetCurrentDirectory(workingDirectory);
            File.WriteAllText(
                Path.Combine(workingDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: relative-path\n");
            File.WriteAllText(
                Path.Combine(workingDirectory, "LLMConfig-SystemPrompt.txt"),
                "Relative prompt");

            var config = Configuration.GetConfiguration(Configuration.DefaultFileName);

            Assert.Equal("relative-path", config.Model);
            Assert.Equal("Relative prompt", config.SystemPrompt);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCurrentDirectory);
            if (Directory.Exists(workingDirectory))
                Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public void GetDefaultConfigurationReportsSearchedPaths()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            var exception = Assert.Throws<FileNotFoundException>(
                () => Configuration.GetDefaultConfiguration(translatorDirectory));

            Assert.Contains(Path.Combine(configDirectory, Configuration.DefaultFileName), exception.Message);
            Assert.Contains(Path.Combine(translatorDirectory, Configuration.DefaultFileName), exception.Message);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void LoadApiKeyTrimsTrailingWhitespace()
    {
        var file = Path.GetTempFileName();

        try
        {
            File.WriteAllText(file, "  secret-key\r\n");
            var config = new LlmConfig();

            Configuration.LoadApiKey(config, file);

            Assert.Equal("secret-key", config.ApiKey);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void LoadGlossaryPromptOverride()
    {
        var config = new LlmConfig { SystemPrompt = "System prompt", GlossaryPrompt = "Glossary prompt" };
        var file = Path.GetTempFileName();

        try
        {
            File.WriteAllText(file, "Override glossary prompt");

            Configuration.LoadGlossaryPrompt(config, file);

            Assert.Equal("System prompt", config.SystemPrompt);
            Assert.Equal("Override glossary prompt", config.GlossaryPrompt);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void AllEndpointsUseSharedConfig()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:9999/v1/chat/completions\nmodel: shared-model\nmaxTranslationsPerRequest: 7\n");

            var endpoints = new HttpEndpoint[]
            {
                new OpenAiTranslatorEndpoint(),
                new OllamaTranslatorEndpoint(),
                new LmStudioTranslatorEndpoint(),
                new IndexTranslateTranslatorEndpoint()
            };

            foreach (var endpoint in endpoints)
            {
                endpoint.Initialize(new TestInitializationContext(translatorDirectory));

                var config = GetConfig(endpoint);
                Assert.Equal("shared-model", config.Model);
                Assert.Equal("http://localhost:9999/v1/chat/completions", config.Url);
                Assert.Equal(7, config.MaxTranslationsPerRequest);
            }
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void OpenAiEndpointAllowsKeylessLocalServer()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:8000/v1/chat/completions\nmodel: local-openai-compatible\n");

            var endpoint = new OpenAiTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));

            var config = GetConfig(endpoint);
            Assert.False(config.ApiKeyRequired);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void OpenAiEndpointRequiresKeyWhenConfigured()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: true\nurl: https://api.openai.com/v1/chat/completions\nmodel: gpt-4o-mini\n");

            var endpoint = new OpenAiTranslatorEndpoint();

            Assert.Throws<Exception>(() => endpoint.Initialize(new TestInitializationContext(translatorDirectory)));
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void OpenAiEndpointSendsAuthorizationOnlyWhenRequired()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKey: secret\napiKeyRequired: true\nurl: https://api.openai.com/v1/chat/completions\nmodel: gpt-4o-mini\n");

            var endpoint = new OpenAiTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));

            var context = new TestRequestCreationContext(new[] { "hello" });
            endpoint.OnCreateRequest(context);

            Assert.Equal("Bearer secret", context.Request!.Headers[HttpRequestHeader.Authorization]);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void OpenAiEndpointOmitsAuthorizationWhenNotRequired()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:8000/v1/chat/completions\nmodel: local-openai-compatible\n");

            var endpoint = new OpenAiTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));

            var context = new TestRequestCreationContext(new[] { "hello" });
            endpoint.OnCreateRequest(context);

            Assert.Null(context.Request!.Headers[HttpRequestHeader.Authorization]);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void EndpointLoadsSharedOverridesAndGlossary()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: path-test-model\n");
            File.WriteAllText(
                Path.Combine(configDirectory, "LLMConfig-SystemPrompt.txt"),
                "Adjacent system prompt");
            File.WriteAllText(
                Path.Combine(configDirectory, "LLMConfig-Glossary.yaml"),
                "- raw: source\n  result: target\n");

            var endpoint = new LmStudioTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));

            var config = GetConfig(endpoint);
            Assert.Equal("path-test-model", config.Model);
            Assert.Equal("Adjacent system prompt", config.SystemPrompt);
            Assert.Single(config.GlossaryLines);
            Assert.Equal("target", config.GlossaryLines[0].Result);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void LmStudioEndpointBuildsBatchRequestFromAllTexts()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: batch-test\nmaxTranslationsPerRequest: 4\nsystemPrompt: Translate.\n");

            var endpoint = new LmStudioTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));

            var context = new TestRequestCreationContext(new[] { "first", "second", "third" });
            endpoint.OnCreateRequest(context);

            Assert.NotNull(context.Request);
            var payload = JSON.Parse(context.Request!.Data);
            var user = payload["messages"][1]["content"].Value;

            Assert.Contains("<<<1>>>", user);
            Assert.Contains("first", user);
            Assert.Contains("<<<2>>>", user);
            Assert.Contains("second", user);
            Assert.Contains("<<<3>>>", user);
            Assert.Contains("third", user);
            Assert.Equal(4, endpoint.MaxTranslationsPerRequest);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void EndpointExposesBatchLimits()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: limits\nmaxTranslationsPerRequest: 7\nmaxCharactersPerRequest: 1234\nmaxCharactersPerTranslation: 567\n");

            var endpoint = new LmStudioTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));

            var limiter = Assert.IsAssignableFrom<ITranslationBatchLimiter>(endpoint);
            Assert.Equal(1234, limiter.MaxCharactersPerRequest);
            Assert.Equal(567, limiter.MaxCharactersPerTranslation);
            Assert.Equal(7, endpoint.MaxTranslationsPerRequest);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void EndpointExposesTemperatureForUi()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: temperature-test\nmodelParams:\n  temperature: 0.35\n");

            var endpoint = new LmStudioTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));

            var settings = Assert.IsAssignableFrom<ITranslateEndpointSettingsProvider>(endpoint);
            Assert.True(settings.TryGetSetting("Temperature", out var temperature));
            Assert.Equal("0.35", temperature);
            Assert.False(settings.TryGetSetting("Unknown", out _));
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void EndpointReportsDefaultTemperatureWhenModelParamsAreEmpty()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: temperature-default\n");

            var endpoint = new LmStudioTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));

            var settings = Assert.IsAssignableFrom<ITranslateEndpointSettingsProvider>(endpoint);
            Assert.True(settings.TryGetSetting("Temperature", out var temperature));
            Assert.Equal("0.2", temperature);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void EndpointReportsDefaultWhenTemperatureIsNotConfigured()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: temperature-default\nmodelParams:\n  top_p: 0.9\n");

            var endpoint = new LmStudioTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));

            var settings = Assert.IsAssignableFrom<ITranslateEndpointSettingsProvider>(endpoint);
            Assert.True(settings.TryGetSetting("Temperature", out var temperature));
            Assert.Equal("default", temperature);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void EndpointReloadsConfigAfterFileChanges()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);
        var configFile = Path.Combine(configDirectory, Configuration.DefaultFileName);

        try
        {
            File.WriteAllText(
                configFile,
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: first-model\nmaxTranslationsPerRequest: 3\nmodelParams:\n  temperature: 0.1\n");

            var endpoint = new LmStudioTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));

            Assert.Equal("first-model", GetConfig(endpoint).Model);
            Assert.Equal(3, endpoint.MaxTranslationsPerRequest);

            File.WriteAllText(
                configFile,
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: second-model\nmaxTranslationsPerRequest: 9\nmodelParams:\n  temperature: 0.8\n");
            File.SetLastWriteTimeUtc(configFile, DateTime.UtcNow.AddSeconds(2));

            Thread.Sleep(600);

            Assert.Equal("second-model", GetConfig(endpoint).Model);
            Assert.Equal(9, endpoint.MaxTranslationsPerRequest);

            var settings = Assert.IsAssignableFrom<ITranslateEndpointSettingsProvider>(endpoint);
            Assert.True(settings.TryGetSetting("Temperature", out var temperature));
            Assert.Equal("0.8", temperature);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void EndpointReloadsRequestPayloadAfterConfigChange()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);
        var configFile = Path.Combine(configDirectory, Configuration.DefaultFileName);

        try
        {
            File.WriteAllText(
                configFile,
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: first-model\nsystemPrompt: First prompt.\n");

            var endpoint = new LmStudioTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));

            var firstContext = new TestRequestCreationContext(new[] { "hello" });
            endpoint.OnCreateRequest(firstContext);

            var firstPayload = JSON.Parse(firstContext.Request!.Data);
            Assert.Equal("first-model", firstPayload["model"].Value);
            Assert.Contains("First prompt.", firstPayload["messages"][0]["content"].Value);

            File.WriteAllText(
                configFile,
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: second-model\nsystemPrompt: Second prompt.\n");
            File.SetLastWriteTimeUtc(configFile, DateTime.UtcNow.AddSeconds(2));

            Thread.Sleep(600);

            var secondContext = new TestRequestCreationContext(new[] { "hello" });
            endpoint.OnCreateRequest(secondContext);

            var secondPayload = JSON.Parse(secondContext.Request!.Data);
            Assert.Equal("second-model", secondPayload["model"].Value);
            Assert.Contains("Second prompt.", secondPayload["messages"][0]["content"].Value);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void EndpointExposesAdvancedSettings()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: get-model\nmaxTranslationsPerRequest: 7\nmaxCharactersPerRequest: 1234\nmaxCharactersPerTranslation: 567\nmodelParams:\n  temperature: 0.35\n  max_tokens: 2048\n  top_p: 0.8\n");

            var endpoint = new LmStudioTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));
            var settings = Assert.IsAssignableFrom<ITranslateEndpointSettingsEditor>(endpoint);

            Assert.True(settings.TryGetSetting("temperature", out var temperature));
            Assert.Equal("0.35", temperature);
            Assert.True(settings.TryGetSetting("max_tokens", out var maxTokens));
            Assert.Equal("2048", maxTokens);
            Assert.True(settings.TryGetSetting("top_p", out var topP));
            Assert.Equal("0.8", topP);
            Assert.True(settings.TryGetSetting("model", out var model));
            Assert.Equal("get-model", model);
            Assert.True(settings.TryGetSetting("url", out var url));
            Assert.Equal("http://localhost:1234/v1/chat/completions", url);
            Assert.True(settings.TryGetSetting("maxTranslationsPerRequest", out var maxTranslations));
            Assert.Equal("7", maxTranslations);
            Assert.True(settings.TryGetSetting("maxCharactersPerRequest", out var maxCharacters));
            Assert.Equal("1234", maxCharacters);
            Assert.True(settings.TryGetSetting("maxCharactersPerTranslation", out var maxCharactersPerTranslation));
            Assert.Equal("567", maxCharactersPerTranslation);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void EndpointUpdatesMainConfigurationAndReloads()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);
        var configFile = Path.Combine(configDirectory, Configuration.DefaultFileName);

        try
        {
            File.WriteAllText(
                configFile,
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: base-model\nmaxTranslationsPerRequest: 5\nmaxCharactersPerRequest: 4000\nmaxCharactersPerTranslation: 2000\nmodelParams:\n  temperature: 0.2\n");

            var endpoint = new LmStudioTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));
            var settings = Assert.IsAssignableFrom<ITranslateEndpointSettingsEditor>(endpoint);

            Assert.True(settings.TrySetSetting("temperature", "0.7", out var error), error);
            Assert.True(settings.TryGetSetting("temperature", out var temperature));
            Assert.Equal("0.7", temperature);
            Assert.Contains("temperature: 0.7", File.ReadAllText(configFile));

            Assert.True(settings.TrySetSetting("model", "ui-model", out error), error);
            Assert.Equal("ui-model", GetConfig(endpoint).Model);
            Assert.Contains("model: \"ui-model\"", File.ReadAllText(configFile));

            Assert.True(settings.TrySetSetting("maxTranslationsPerRequest", "8", out error), error);
            Assert.Equal(8, endpoint.MaxTranslationsPerRequest);
            Assert.Contains("maxTranslationsPerRequest: 8", File.ReadAllText(configFile));

            Assert.True(settings.TrySetSetting("maxCharactersPerRequest", "1234", out error), error);
            var limiter = Assert.IsAssignableFrom<ITranslationBatchLimiter>(endpoint);
            Assert.Equal(1234, limiter.MaxCharactersPerRequest);
            Assert.Contains("maxCharactersPerRequest: 1234", File.ReadAllText(configFile));
            Assert.Contains("url: http://localhost:1234/v1/chat/completions", File.ReadAllText(configFile));
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void EndpointClearsModelParameterWithDefault()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);
        var configFile = Path.Combine(configDirectory, Configuration.DefaultFileName);

        try
        {
            File.WriteAllText(
                configFile,
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: base-model\nmodelParams:\n  temperature: 0.2\n");

            var endpoint = new LmStudioTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));
            var settings = Assert.IsAssignableFrom<ITranslateEndpointSettingsEditor>(endpoint);

            Assert.True(settings.TrySetSetting("temperature", "0.7", out var error), error);
            Assert.True(settings.TryGetSetting("temperature", out var temperature));
            Assert.Equal("0.7", temperature);

            Assert.True(settings.TrySetSetting("temperature", "default", out error), error);
            Assert.True(settings.TryGetSetting("temperature", out temperature));
            Assert.Equal("0.2", temperature);
            Assert.DoesNotContain("temperature:", File.ReadAllText(configFile));
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void EndpointRejectsInvalidAdvancedSettings()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: base-model\nmodelParams:\n  temperature: 0.2\n");

            var endpoint = new LmStudioTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));
            var settings = Assert.IsAssignableFrom<ITranslateEndpointSettingsEditor>(endpoint);

            Assert.False(settings.TrySetSetting("temperature", "abc", out var error));
            Assert.Contains("number", error);
            Assert.False(settings.TrySetSetting("max_tokens", "0", out error));
            Assert.False(settings.TrySetSetting("top_p", "2", out error));
            Assert.False(settings.TrySetSetting("model", " ", out error));
            Assert.False(settings.TrySetSetting("url", "not a url", out error));
            Assert.False(settings.TrySetSetting("maxTranslationsPerRequest", "-1", out error));
            Assert.False(settings.TrySetSetting("maxCharactersPerRequest", "x", out error));
            Assert.Equal("base-model", GetConfig(endpoint).Model);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void ConfigurationValueInsertionAddsMissingKeys()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);
        var configFile = Path.Combine(configDirectory, Configuration.DefaultFileName);

        try
        {
            File.WriteAllText(
                configFile,
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: base-model\nmodelParams:\n  temperature: 0.2\n");

            var endpoint = new LmStudioTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));
            var settings = Assert.IsAssignableFrom<ITranslateEndpointSettingsEditor>(endpoint);

            Assert.True(settings.TrySetSetting("max_tokens", "2048", out var error), error);
            Assert.True(settings.TrySetSetting("maxCharactersPerRequest", "1234", out error), error);

            var text = File.ReadAllText(configFile);
            Assert.Contains("  max_tokens: 2048", text);
            Assert.Contains("maxCharactersPerRequest: 1234", text);

            Assert.True(settings.TryGetSetting("max_tokens", out var maxTokens));
            Assert.Equal("2048", maxTokens);
            var limiter = Assert.IsAssignableFrom<ITranslationBatchLimiter>(endpoint);
            Assert.Equal(1234, limiter.MaxCharactersPerRequest);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void MainConfigurationLoadsGlossary()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: base-model\nglossary:\n  - raw: term\n    result: translation\n");

            var config = Configuration.GetConfiguration(translatorDirectory, Configuration.DefaultFileName);

            Assert.Single(config.GlossaryLines);
            Assert.Equal("term", config.GlossaryLines[0].Raw);
            Assert.Equal("translation", config.GlossaryLines[0].Result);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void IndexTranslateEndpointUsesNativePromptAndRecommendedDefaults()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: model-identifier\n");

            var endpoint = new IndexTranslateTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));

            var context = new TestRequestCreationContext(new[] { "hello" });
            endpoint.OnCreateRequest(context);

            Assert.NotNull(context.Request);
            Assert.Equal(IndexPublicUrl, context.Request!.Address.ToString());
            Assert.Equal("Index-Translate-Client/1.0", context.Request.Headers["User-Agent"]);

            var payload = JSON.Parse(context.Request.Data);
            Assert.Equal(IndexPublicModel, payload["model"].Value);
            Assert.Equal(0.0, payload["temperature"].AsDouble);
            Assert.Equal(4096, payload["max_tokens"].AsInt);
            Assert.False(payload["chat_template_kwargs"]["enable_thinking"].AsBool);

            var messages = payload["messages"].AsArray;
            Assert.Equal(1, messages.Count);
            Assert.Equal("user", messages[0]["role"].Value);
            Assert.Contains("请将以下中文文本翻译为英语", messages[0]["content"].Value);
            Assert.Contains("hello", messages[0]["content"].Value);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void IndexTranslateEndpointUsesConfiguredModelParams()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: https://index-translate.bilibili.com/v1/chat/completions\nmodel: Index-Translate-35B-A3B\nmodelParams:\n  temperature: 0.3\n  max_tokens: 2048\n");

            var endpoint = new IndexTranslateTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));

            var context = new TestRequestCreationContext(new[] { "hello" });
            endpoint.OnCreateRequest(context);

            var payload = JSON.Parse(context.Request!.Data);
            Assert.Equal(0.3, payload["temperature"].AsDouble, 3);
            Assert.Equal(2048, payload["max_tokens"].AsInt);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void IndexTranslateEndpointBatchesWithCanonicalConstraints()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: https://index-translate.bilibili.com/v1/chat/completions\nmodel: Index-Translate-35B-A3B\nmaxTranslationsPerRequest: 4\nglossary:\n  - raw: 碳纤维\n    result: carbon fiber\n");

            var endpoint = new IndexTranslateTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));

            var context = new TestRequestCreationContext(new[] { "碳纤维很好", "第二行" });
            endpoint.OnCreateRequest(context);

            var content = JSON.Parse(context.Request!.Data)["messages"][0]["content"].Value;

            Assert.Contains("【源文】", content);
            Assert.Contains("<<<1>>>", content);
            Assert.Contains("<<<2>>>", content);
            Assert.Contains("【约束要求】", content);
            Assert.Contains("专名/术语对照: 碳纤维→carbon fiber", content);
            Assert.Contains("保持原来的顺序、块数和换行", content);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void IndexTranslateEndpointExposesEffectiveDefaults()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: model-identifier\n");

            var endpoint = new IndexTranslateTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));
            var settings = Assert.IsAssignableFrom<ITranslateEndpointSettingsProvider>(endpoint);

            Assert.True(settings.TryGetSetting("Temperature", out var temperature));
            Assert.Equal("0", temperature);
            Assert.True(settings.TryGetSetting("max_tokens", out var maxTokens));
            Assert.Equal("4096", maxTokens);
            Assert.True(settings.TryGetSetting("url", out var url));
            Assert.Equal(IndexPublicUrl, url);
            Assert.True(settings.TryGetSetting("model", out var model));
            Assert.Equal(IndexPublicModel, model);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void IndexTranslateEndpointUsesAutoSourceAndMappedTarget()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: https://index-translate.bilibili.com/v1/chat/completions\nmodel: Index-Translate-35B-A3B\n");

            var endpoint = new IndexTranslateTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));

            var context = new TestRequestCreationContext(new[] { "hello" }, "auto", "ko");
            endpoint.OnCreateRequest(context);

            var content = JSON.Parse(context.Request!.Data)["messages"][0]["content"].Value;
            Assert.Contains("请将以下文本翻译为韩语", content);
            Assert.DoesNotContain("auto", content);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void IndexTranslateEndpointUsesSelfHostedOverrides()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(configDirectory, Configuration.DefaultFileName),
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: model-identifier\nindexTranslateUrl: \"http://127.0.0.1:8000/v1/chat/completions\"\nindexTranslateModel: \"IndexTeam/Index-Translate-9B\"\n");

            var endpoint = new IndexTranslateTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));

            var context = new TestRequestCreationContext(new[] { "hello" });
            endpoint.OnCreateRequest(context);

            Assert.Equal("http://127.0.0.1:8000/v1/chat/completions", context.Request!.Address.ToString());
            var payload = JSON.Parse(context.Request.Data);
            Assert.Equal("IndexTeam/Index-Translate-9B", payload["model"].Value);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    [Fact]
    public void IndexTranslateEndpointWritesDedicatedOverrides()
    {
        var gameDirectory = CreateGameDirectory(out var translatorDirectory, out var configDirectory);
        var configFile = Path.Combine(configDirectory, Configuration.DefaultFileName);

        try
        {
            File.WriteAllText(
                configFile,
                "apiKeyRequired: false\nurl: http://localhost:1234/v1/chat/completions\nmodel: model-identifier\n");

            var endpoint = new IndexTranslateTranslatorEndpoint();
            endpoint.Initialize(new TestInitializationContext(translatorDirectory));
            var settings = Assert.IsAssignableFrom<ITranslateEndpointSettingsEditor>(endpoint);

            Assert.True(settings.TrySetSetting("url", "http://127.0.0.1:8000/v1/chat/completions", out var error), error);
            Assert.True(settings.TrySetSetting("model", "IndexTeam/Index-Translate-9B", out error), error);

            var text = File.ReadAllText(configFile);
            Assert.Contains("indexTranslateUrl: \"http://127.0.0.1:8000/v1/chat/completions\"", text);
            Assert.Contains("indexTranslateModel: \"IndexTeam/Index-Translate-9B\"", text);

            Assert.True(settings.TryGetSetting("url", out var url));
            Assert.Equal("http://127.0.0.1:8000/v1/chat/completions", url);
            Assert.True(settings.TryGetSetting("model", out var model));
            Assert.Equal("IndexTeam/Index-Translate-9B", model);
        }
        finally
        {
            Directory.Delete(gameDirectory, recursive: true);
        }
    }

    private static string CreateGameDirectory(out string translatorDirectory, out string configDirectory)
    {
        var gameDirectory = Path.Combine(Path.GetTempPath(), $"LlmTranslatorTests-{Guid.NewGuid():N}");
        translatorDirectory = Path.Combine(
            gameDirectory,
            "BepInEx",
            "plugins",
            "XUnity.AutoTranslator",
            "Translators");
        configDirectory = Path.Combine(gameDirectory, "BepInEx", "config");

        Directory.CreateDirectory(translatorDirectory);
        Directory.CreateDirectory(configDirectory);

        return gameDirectory;
    }

    private static LlmConfig GetConfig(object endpoint)
    {
        var providerField = endpoint.GetType().GetField(
            "_configProvider",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(providerField);
        var provider = Assert.IsType<LlmConfigProvider>(providerField!.GetValue(endpoint));
        return provider.Current;
    }

    private sealed class TestInitializationContext : IInitializationContext
    {
        public TestInitializationContext(string translatorDirectory)
        {
            TranslatorDirectory = translatorDirectory;
        }

        public string TranslatorDirectory { get; }
        public string SourceLanguage => "ja";
        public string DestinationLanguage => "ko";

        public T GetOrCreateSetting<T>(string section, string key, T defaultValue) => defaultValue;
        public T GetOrCreateSetting<T>(string section, string key) => default!;
        public void SetSetting<T>(string section, string key, T value) { }
        public void DisableCertificateChecksFor(params string[] hosts) { }
        public void DisableSpamChecks() { }
        public void SetTranslationDelay(float delayInSeconds) { }
    }

    private sealed class TestRequestCreationContext : IHttpRequestCreationContext
    {
        public TestRequestCreationContext(string[] texts, string sourceLanguage = "zh", string destinationLanguage = "en")
        {
            UntranslatedTexts = texts;
            SourceLanguage = sourceLanguage;
            DestinationLanguage = destinationLanguage;
        }

        public string UntranslatedText => UntranslatedTexts[0];
        public string[] UntranslatedTexts { get; }
        public UntranslatedTextInfo UntranslatedTextInfo => null!;
        public UntranslatedTextInfo[] UntranslatedTextInfos => null!;
        public string SourceLanguage { get; }
        public string DestinationLanguage { get; }
        public object UserState { get; set; } = null!;
        public XUnityWebRequest? Request { get; private set; }

        public void Complete(XUnityWebRequest request) => Request = request;
        public void Fail(string reason, Exception exception) => throw new InvalidOperationException(reason, exception);
        public void Fail(string reason) => throw new InvalidOperationException(reason);
    }
}
