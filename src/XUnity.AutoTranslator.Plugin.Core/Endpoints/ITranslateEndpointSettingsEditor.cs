namespace XUnity.AutoTranslator.Plugin.Core.Endpoints
{
   /// <summary>
   /// Optional interface implemented by translators whose settings can be edited from the plugin UI.
   /// </summary>
   public interface ITranslateEndpointSettingsEditor : ITranslateEndpointSettingsProvider
   {
      /// <summary>
      /// Attempts to update the specified setting.
      /// </summary>
      /// <param name="key">The setting name.</param>
      /// <param name="value">The new setting value.</param>
      /// <param name="error">The validation or write error when the method returns <c>false</c>.</param>
      /// <returns><c>true</c> if the setting was updated; otherwise <c>false</c>.</returns>
      bool TrySetSetting( string key, string value, out string error );
   }
}
