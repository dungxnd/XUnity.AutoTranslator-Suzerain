namespace XUnity.AutoTranslator.Plugin.Core.Endpoints
{
   /// <summary>
   /// Optional interface implemented by translators that need to limit a batch by input size.
   /// </summary>
   public interface ITranslationBatchLimiter
   {
      /// <summary>
      /// Gets the maximum number of characters that may be sent in one request.
      /// A value less than or equal to zero means no limit.
      /// </summary>
      int MaxCharactersPerRequest { get; }

      /// <summary>
      /// Gets the maximum number of characters for a text that may be batched with other texts.
      /// Longer texts are sent on their own. Translators implementing this interface are
      /// expected to accept texts longer than this value, for example by splitting them.
      /// A value less than or equal to zero means no limit.
      /// </summary>
      int MaxCharactersPerTranslation { get; }
   }
}
