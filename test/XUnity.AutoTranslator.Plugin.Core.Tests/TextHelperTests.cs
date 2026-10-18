using Xunit;
using XUnity.AutoTranslator.Plugin.Core.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Tests
{
   public class TextHelperTests
   {
      [Theory( DisplayName = "Encode_And_Decode_Round_Trip" )]
      [InlineData( "http://example.com/a//b" )]
      [InlineData( "key=with=equals" )]
      [InlineData( "back\\slash" )]
      [InlineData( "line1\nline2" )]
      [InlineData( "line1\rline2" )]
      [InlineData( "unicode: 日本語 テスト" )]
      [InlineData( "// leading comment marker" )]
      [InlineData( "plain text" )]
      public void Encode_And_Decode_Round_Trip( string value )
      {
         var encoded = TextHelper.Encode( value );
         var decoded = TextHelper.ReadTranslationLineAndDecode( "key=" + encoded );

         Assert.NotNull( decoded );
         Assert.Equal( value, decoded[ 1 ] );
      }

      [Fact( DisplayName = "Double_Slash_Is_Not_Treated_As_A_Comment_After_Encode" )]
      public void Double_Slash_Is_Not_Treated_As_A_Comment_After_Encode()
      {
         var encoded = TextHelper.Encode( "http://example.com/a//b" );

         Assert.Equal( "http:\\/\\/example.com/a\\/\\/b", encoded );
         Assert.Equal( "http://example.com/a//b", TextHelper.ReadTranslationLineAndDecode( "key=" + encoded )[ 1 ] );
      }

      [Theory( DisplayName = "Malformed_Lines_Are_Rejected" )]
      [InlineData( "" )]
      [InlineData( "no-separator" )]
      [InlineData( "// only a comment" )]
      [InlineData( "key=value=extra" )]
      public void Malformed_Lines_Are_Rejected( string line )
      {
         Assert.Null( TextHelper.ReadTranslationLineAndDecode( line ) );
      }

      [Fact( DisplayName = "Legacy_Percent_3D_Is_Decoded" )]
      public void Legacy_Percent_3D_Is_Decoded()
      {
         // Kept for backwards compatibility with the pre-escape format. This is
         // intentionally lossy for literal '%3D' sequences (F-05).
         Assert.Equal( "a=b", TextHelper.ReadTranslationLineAndDecode( "key=a%3Db" )[ 1 ] );
      }
   }
}
