using System.Net;
using XUnity.AutoTranslator.LlmTranslators.Behavior;
using XUnity.AutoTranslator.LlmTranslators.Config;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using XUnity.AutoTranslator.Plugin.Core.Endpoints.Http;
using XUnity.AutoTranslator.Plugin.Core.Web;

public class OpenAiTranslatorEndpoint : HttpEndpoint, ITranslateEndpointSettingsEditor, ITranslationBatchLimiter
{
    public override string Id => "OpenAiTranslate";
    public override string FriendlyName => "OpenAi Translate";
    public override int MaxTranslationsPerRequest => Math.Max(1, _configProvider?.Current.MaxTranslationsPerRequest ?? 12);
    public override int MaxConcurrency => 15;

    public int MaxCharactersPerRequest => BaseEndpointBehavior.GetMaxCharactersPerRequest(_configProvider?.Current);
    public int MaxCharactersPerTranslation => BaseEndpointBehavior.GetMaxCharactersPerTranslation(_configProvider?.Current);

    private LlmConfigProvider? _configProvider;

    public override void Initialize(IInitializationContext context)
    {
        _configProvider = new LlmConfigProvider(context.TranslatorDirectory);

        // Remove artificial delays
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

        var requestData = BaseEndpointBehavior.GetRequestData(config, plan.Chunks);

        var request = new XUnityWebRequest("POST", config.Url, requestData);
        request.Headers[HttpRequestHeader.ContentType] = "application/json";

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

        var content = jsonResponse!["choices"]?[0]?["message"]?["content"]?.ToString() ?? string.Empty;
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
        return BaseEndpointBehavior.TryGetSetting(_configProvider?.Current, key, out value);
    }

    public bool TrySetSetting(string key, string value, out string error)
    {
        if (_configProvider == null)
        {
            error = "The endpoint is not initialized.";
            return false;
        }

        return _configProvider.TrySetSetting(key, value, out error);
    }
}
