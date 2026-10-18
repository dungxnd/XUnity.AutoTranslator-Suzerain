using System;
using System.IO;
using Xunit;
using XUnity.AutoTranslator.Plugin.Core;

namespace XUnity.AutoTranslator.Plugin.Core.Tests
{
   public class SimpleTextTranslationCacheTests
   {
      [Fact( DisplayName = "Loads_Plain_And_Regex_Translations" )]
      public void Loads_Plain_And_Regex_Translations()
      {
         var directory = Path.Combine( Path.GetTempPath(), "xuat-cache-" + Guid.NewGuid().ToString( "N" ) );
         Directory.CreateDirectory( directory );

         try
         {
            var path = Path.Combine( directory, "translations.txt" );
            File.WriteAllText( path, "hello=world\r\nr:\"^hi (.+)$\"=Hello $1\r\n" );

            var cache = new SimpleTextTranslationCache( path, true );

            Assert.True( cache.TryGetTranslation( "hello", false, out var value ) );
            Assert.Equal( "world", value );

            Assert.True( cache.TryGetTranslation( "hi bob", true, out var regexValue ) );
            Assert.Equal( "Hello bob", regexValue );
         }
         finally
         {
            Directory.Delete( directory, true );
         }
      }

      [Fact( DisplayName = "Wrapper_Variants_Use_Cached_Translation" )]
      public void Wrapper_Variants_Use_Cached_Translation()
      {
         var directory = Path.Combine( Path.GetTempPath(), "xuat-cache-" + Guid.NewGuid().ToString( "N" ) );
         Directory.CreateDirectory( directory );

         try
         {
            var path = Path.Combine( directory, "translations.txt" );
            File.WriteAllText( path, "Hello brother=Chào người anh em\r\n" );

            var cache = new SimpleTextTranslationCache( path, true );

            Assert.True( cache.TryGetTranslation( "\"Hello brother\"", false, out var quoted ) );
            Assert.Equal( "\"Chào người anh em\"", quoted );

            Assert.True( cache.TryGetTranslation( "<Hello brother>", false, out var angled ) );
            Assert.Equal( "<Chào người anh em>", angled );

            Assert.True( cache.TryGetTranslation( "1. Hello brother", false, out var numbered ) );
            Assert.Equal( "1. Chào người anh em", numbered );
         }
         finally
         {
            Directory.Delete( directory, true );
         }
      }
   }
}
