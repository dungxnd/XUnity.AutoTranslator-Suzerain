using System.Collections.Generic;
using Xunit;
using XUnity.AutoTranslator.Plugin.Core.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Tests
{
   public class TranslationTextWrapperTests
   {
      [Theory( DisplayName = "Safe_Wrappers_Are_Removed_And_Preserved" )]
      [InlineData( "\"Hello\"", "Hello", "\"", "\"" )]
      [InlineData( "1. Hello", "Hello", "1. ", "" )]
      [InlineData( "1. \"Hello\"", "Hello", "1. \"", "\"" )]
      [InlineData( "\"1. Hello\"", "Hello", "\"1. ", "\"" )]
      [InlineData( "  \"Hello\"  ", "Hello", "  \"", "\"  " )]
      [InlineData( "<Hello brother>", "Hello brother", "<", ">" )]
      [InlineData( "[Hello brother]", "Hello brother", "[", "]" )]
      [InlineData( "(sigh)", "sigh", "(", ")" )]
      [InlineData( "「こんにちは」", "こんにちは", "「", "」" )]
      [InlineData( "* Hello", "Hello", "* ", "" )]
      [InlineData( "*Hello*", "Hello", "*", "*" )]
      [InlineData( "**Hello**", "Hello", "**", "**" )]
      [InlineData( "①Hello", "Hello", "①", "" )]
      [InlineData( "①. Hello", "Hello", "①. ", "" )]
      [InlineData( "(1) Hello", "Hello", "(1) ", "" )]
      [InlineData( "[1] Hello", "Hello", "[1] ", "" )]
      [InlineData( "1 - Hello", "Hello", "1 - ", "" )]
      [InlineData( "{{A}}. Hello", "Hello", "{{A}}. ", "" )]
      public void Safe_Wrappers_Are_Removed_And_Preserved( string input, string expectedCore, string expectedPrefix, string expectedSuffix )
      {
         Assert.True( TranslationTextWrapper.TryUnwrapAll( input, out var core, out var prefix, out var suffix ) );
         Assert.Equal( expectedCore, core );
         Assert.Equal( expectedPrefix, prefix );
         Assert.Equal( expectedSuffix, suffix );
      }

      [Theory( DisplayName = "Unsafe_Or_Ambiguous_Text_Is_Not_Unwrapped" )]
      [InlineData( "Hello" )]
      [InlineData( "1.5 apples" )]
      [InlineData( "-50" )]
      [InlineData( "1:30 PM" )]
      [InlineData( "{{A}}" )]
      [InlineData( "[Quest]" )]
      [InlineData( "<Hello>" )]
      [InlineData( "<color=red>Hello</color>" )]
      [InlineData( "\"Hello" )]
      [InlineData( "..." )]
      public void Unsafe_Or_Ambiguous_Text_Is_Not_Unwrapped( string input )
      {
         Assert.False( TranslationTextWrapper.TryUnwrapAll( input, out _, out _, out _ ) );
      }

      [Fact( DisplayName = "Wrap_Restores_Wrappers_And_Avoids_Duplicates" )]
      public void Wrap_Restores_Wrappers_And_Avoids_Duplicates()
      {
         Assert.Equal( "\"Chào\"", Wrap( "\"Hello\"", "Chào" ) );
         Assert.Equal( "\"Chào\"", Wrap( "\"Hello\"", "\"Chào\"" ) );
         Assert.Equal( "1. \"Chào\"", Wrap( "1. \"Hello\"", "Chào" ) );
         Assert.Equal( "\"1. Chào\"", Wrap( "\"1. Hello\"", "Chào" ) );
         Assert.Equal( "1. \"Chào\"", Wrap( "1. \"Hello\"", "1. \"Chào\"" ) );
         Assert.Equal( "1. Chào", Wrap( "1. Hello", "1. Chào" ) );
         Assert.Equal( "<Chào>", Wrap( "<Hello brother>", "Chào" ) );
      }

      private static string Wrap( string input, string translation )
      {
         var layers = new List<TranslationTextWrapper.Layer>();
         var current = input;

         while( TranslationTextWrapper.TryUnwrapOneLayer( current, out var next, out var layer ) )
         {
            layers.Add( layer );
            current = next;
         }

         return TranslationTextWrapper.Wrap( layers, translation );
      }
   }
}
