using System;
using System.Net;
using XUnity.AutoTranslator.LlmTranslators.Behavior;
using XUnity.AutoTranslator.LlmTranslators.Config;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using XUnity.AutoTranslator.Plugin.Core.Endpoints.Http;
using XUnity.AutoTranslator.Plugin.Core.Web;

public class IndexTranslateTranslatorEndpoint : HttpEndpoint, ITranslateEndpointSettingsEditor, ITranslationBatchLimiter
{
    public override string Id => "IndexTranslate";
    public override string FriendlyName => "Index-Translate";

    public override int MaxTranslationsPerRequest => Math.Max(1, _configProvider?.Current.MaxTranslationsPerRequest ?? 12);

    // The public Index-Translate API is shared; keep the request rate modest.
    public override int MaxConcurrency => 5;

    public int MaxCharactersPerRequest => BaseEndpointBehavior.GetMaxCharactersPerRequest(_configProvider?.Current);
    public int MaxCharactersPerTranslation => BaseEndpointBehavior.GetMaxCharactersPerTranslation(_configProvider?.Current);

    private LlmConfigProvider? _configProvider;

    public override void Initialize(IInitializationContext context)
    {
        _configProvider = new LlmConfigProvider(context.TranslatorDirectory);

        context.SetTranslationDelay(0.1f);
        context.DisableSpamChecks();

        if (!BaseEndpointBehavior.TryValidateApiKey(_configProvider.Current, out var error))
            throw new Exception(error);
    }

    public override void OnCreateRequest(IHttpRequestCreationContext context)
    {
        var config = _configProvider!.Current;
        if (!BaseEndpointBehavior.TryValidateApiKey(config, out var error))
        {
            context.Fail(error);
            return;
        }

        var plan = BaseEndpointBehavior.CreateRequestPlan(config, context.UntranslatedTexts);
        context.UserState = plan;

        var prompt = IndexTranslatePrompt.BuildPrompt(
            plan.Chunks,
            context.SourceLanguage,
            context.DestinationLanguage,
            config);

        var requestData = IndexTranslatePrompt.GetRequestData(config, prompt);

        var configuredUrl = config.IndexTranslateUrl;
        var url = configuredUrl == null ? IndexTranslatePrompt.PublicUrl : configuredUrl.Trim();

        if (url.Length == 0)
            url = IndexTranslatePrompt.PublicUrl;

        var request = new XUnityWebRequest("POST", url, requestData);
        request.Headers[HttpRequestHeader.ContentType] = "application/json";
        request.Headers["User-Agent"] = "Index-Translate-Client/1.0";

        if (config.ApiKeyRequired)
            request.Headers[HttpRequestHeader.Authorization] = $"Bearer {config.ApiKey}";

        context.Complete(request);
    }

    public override void OnExtractTranslation(IHttpTranslationExtractionContext context)
    {
        if (!BaseEndpointBehavior.TryParseResponse(context.Response.Data, out var jsonResponse, out var error))
        {
            context.Fail(error);
            return;
        }

        var content = BaseEndpointBehavior.StripThinkBlock(
            jsonResponse!["choices"]?[0]?["message"]?["content"]?.ToString() ?? string.Empty);

        var config = _configProvider!.Current;
        var translations = BaseEndpointBehavior.ExtractPlannedTranslations(
            content,
            context.UserState as BaseEndpointBehavior.RequestPlan,
            context.UntranslatedTexts,
            config);

        if (translations == null)
        {
            context.Fail("The model did not return one translation for each input text.");
            return;
        }

        if (translations.Length == 1 && context.UntranslatedTexts.Length == 1)
            context.Complete(translations[0]);
        else
            context.Complete(translations);
    }

    public bool TryGetSetting(string key, out string value)
    {
        var config = _configProvider?.Current;
        if (config == null)
        {
            value = string.Empty;
            return false;
        }

        if (string.Equals(key, "url", StringComparison.OrdinalIgnoreCase))
        {
            var configuredUrl = config.IndexTranslateUrl;
            value = configuredUrl == null ? IndexTranslatePrompt.PublicUrl : configuredUrl.Trim();

            if (value.Length == 0)
                value = IndexTranslatePrompt.PublicUrl;

            return true;
        }

        if (string.Equals(key, "model", StringComparison.OrdinalIgnoreCase))
        {
            var configuredModel = config.IndexTranslateModel;
            value = configuredModel == null ? IndexTranslatePrompt.PublicModel : configuredModel.Trim();

            if (value.Length == 0)
                value = IndexTranslatePrompt.PublicModel;

            return true;
        }

        if (!BaseEndpointBehavior.TryGetSetting(
            config,
            key,
            out value,
            IndexTranslatePrompt.DefaultTemperature,
            IndexTranslatePrompt.DefaultMaxTokens))
        {
            return false;
        }

        // Index-Translate always sends these values, even when modelParams does not list them.
        if (string.Equals(value, "default", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(key, "temperature", StringComparison.OrdinalIgnoreCase))
                value = IndexTranslatePrompt.DefaultTemperature;
            else if (string.Equals(key, "max_tokens", StringComparison.OrdinalIgnoreCase))
                value = IndexTranslatePrompt.DefaultMaxTokens;
        }

        return true;
    }

    public bool TrySetSetting(string key, string value, out string error)
    {
        if (_configProvider == null)
        {
            error = "The endpoint is not initialized.";
            return false;
        }

        if (string.Equals(key, "url", StringComparison.OrdinalIgnoreCase))
            key = "indexTranslateUrl";
        else if (string.Equals(key, "model", StringComparison.OrdinalIgnoreCase))
            key = "indexTranslateModel";

        return _configProvider.TrySetSetting(key, value, out error);
    }
}
