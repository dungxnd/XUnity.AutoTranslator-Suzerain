namespace XUnity.AutoTranslator.Plugin.Core.Endpoints
{
   /// <summary>
   /// Optional interface implemented by translators that expose read-only settings for display in the plugin UI.
   /// </summary>
   public interface ITranslateEndpointSettingsProvider
   {
      /// <summary>
      /// Attempts to get the current value of the specified setting.
      /// </summary>
      /// <param name="key">The setting name.</param>
      /// <param name="value">The setting value when the method returns <c>true</c>.</param>
      /// <returns><c>true</c> if the setting is available; otherwise <c>false</c>.</returns>
      bool TryGetSetting( string key, out string value );
   }
}
