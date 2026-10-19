using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.LlmTranslators.Config;

public sealed class LlmConfigProvider
{
    private const int CheckIntervalMilliseconds = 500;

    private readonly string _translatorDirectory;
    private readonly object _sync = new object();

    private LlmConfig _config;
    private long _fingerprint;
    private DateTime _nextCheckUtc;

    public LlmConfigProvider(string translatorDirectory)
    {
        _translatorDirectory = translatorDirectory;
        _config = Configuration.GetDefaultConfiguration(translatorDirectory);
        _fingerprint = Configuration.GetDefaultConfigurationFingerprint(translatorDirectory);
    }

    public LlmConfig Current
    {
        get
        {
            RefreshIfChanged();
            return _config;
        }
    }

    public bool TrySetSetting(string key, string value, out string error)
    {
        if (!Configuration.TrySetConfigurationValue(_translatorDirectory, key, value, out error))
            return false;

        try
        {
            Reload();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Reload()
    {
        lock (_sync)
        {
            _config = Configuration.GetDefaultConfiguration(_translatorDirectory);
            _fingerprint = Configuration.GetDefaultConfigurationFingerprint(_translatorDirectory);
            _nextCheckUtc = DateTime.UtcNow.AddMilliseconds(CheckIntervalMilliseconds);
        }
    }

    public void RefreshIfChanged()
    {
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            if (now < _nextCheckUtc)
                return;

            _nextCheckUtc = now.AddMilliseconds(CheckIntervalMilliseconds);

            long fingerprint;
            try
            {
                fingerprint = Configuration.GetDefaultConfigurationFingerprint(_translatorDirectory);
            }
            catch (FileNotFoundException)
            {
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                XuaLogger.AutoTranslator.Warn("Unable to check the LLM configuration for changes: " + ex.Message);
                return;
            }

            if (fingerprint == _fingerprint)
                return;

            try
            {
                _config = Configuration.GetDefaultConfiguration(_translatorDirectory);
                _fingerprint = fingerprint;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException)
            {
                _fingerprint = fingerprint;
                XuaLogger.AutoTranslator.Warn("Failed to reload the LLM configuration: " + ex.Message);
            }
        }
    }
}
