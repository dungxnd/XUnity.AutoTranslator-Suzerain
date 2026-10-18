using System;
using System.Collections.Generic;

namespace XUnity.AutoTranslator.Plugin.Core.Utilities
{
   internal static class TranslationTextWrapper
   {
      internal enum LayerKind
      {
         Paired,
         ListMarker
      }

      internal sealed class Layer
      {
         public Layer( string prefix, string suffix, LayerKind kind )
         {
            Prefix = prefix;
            Suffix = suffix;
            Kind = kind;
         }

         public string Prefix { get; }

         public string Suffix { get; }

         public LayerKind Kind { get; }
      }

      private enum PairKind
      {
         Quote,
         Bracket,
         TextBracket,
         Angle,
         Markdown
      }

      private sealed class Pair
      {
         public Pair( string open, string close, PairKind kind )
         {
            Open = open;
            Close = close;
            Kind = kind;
         }

         public string Open { get; }

         public string Close { get; }

         public PairKind Kind { get; }
      }

      private static readonly Pair[] Pairs = new Pair[]
      {
         new Pair( "**", "**", PairKind.Markdown ),
         new Pair( "__", "__", PairKind.Markdown ),
         new Pair( "~~", "~~", PairKind.Markdown ),
         new Pair( "\"", "\"", PairKind.Quote ),
         new Pair( "'", "'", PairKind.Quote ),
         new Pair( "`", "`", PairKind.Quote ),
         new Pair( "“", "”", PairKind.Quote ),
         new Pair( "”", "”", PairKind.Quote ),
         new Pair( "‘", "’", PairKind.Quote ),
         new Pair( "’", "’", PairKind.Quote ),
         new Pair( "„", "“", PairKind.Quote ),
         new Pair( "‚", "‘", PairKind.Quote ),
         new Pair( "«", "»", PairKind.Quote ),
         new Pair( "‹", "›", PairKind.Quote ),
         new Pair( "〝", "〞", PairKind.Quote ),
         new Pair( "〟", "〞", PairKind.Quote ),
         new Pair( "＂", "＂", PairKind.Quote ),
         new Pair( "＇", "＇", PairKind.Quote ),
         new Pair( "(", ")", PairKind.Bracket ),
         new Pair( "（", "）", PairKind.Bracket ),
         new Pair( "「", "」", PairKind.Bracket ),
         new Pair( "『", "』", PairKind.Bracket ),
         new Pair( "【", "】", PairKind.Bracket ),
         new Pair( "《", "》", PairKind.Bracket ),
         new Pair( "〈", "〉", PairKind.Bracket ),
         new Pair( "〔", "〕", PairKind.Bracket ),
         new Pair( "〖", "〗", PairKind.Bracket ),
         new Pair( "〘", "〙", PairKind.Bracket ),
         new Pair( "〚", "〛", PairKind.Bracket ),
         new Pair( "﹁", "﹂", PairKind.Bracket ),
         new Pair( "﹃", "﹄", PairKind.Bracket ),
         new Pair( "︵", "︶", PairKind.Bracket ),
         new Pair( "︷", "︸", PairKind.Bracket ),
         new Pair( "︹", "︺", PairKind.Bracket ),
         new Pair( "︻", "︼", PairKind.Bracket ),
         new Pair( "︽", "︾", PairKind.Bracket ),
         new Pair( "︿", "﹀", PairKind.Bracket ),
         new Pair( "﹇", "﹈", PairKind.Bracket ),
         new Pair( "﹙", "﹚", PairKind.Bracket ),
         new Pair( "﹛", "﹜", PairKind.Bracket ),
         new Pair( "﹝", "﹞", PairKind.Bracket ),
         new Pair( "⟨", "⟩", PairKind.Bracket ),
         new Pair( "⟪", "⟫", PairKind.Bracket ),
         new Pair( "⟦", "⟧", PairKind.Bracket ),
         new Pair( "[", "]", PairKind.TextBracket ),
         new Pair( "［", "］", PairKind.TextBracket ),
         new Pair( "{", "}", PairKind.TextBracket ),
         new Pair( "｛", "｝", PairKind.TextBracket ),
         new Pair( "<", ">", PairKind.Angle ),
         new Pair( "＜", "＞", PairKind.Angle ),
         new Pair( "*", "*", PairKind.Markdown ),
         new Pair( "_", "_", PairKind.Markdown ),
         new Pair( "~", "~", PairKind.Markdown ),
         new Pair( "|", "|", PairKind.Markdown ),
         new Pair( "‖", "‖", PairKind.Markdown )
      };

      private static readonly HashSet<char> PairStartCharacters = CreatePairStartCharacters();

      private static HashSet<char> CreatePairStartCharacters()
      {
         var characters = new HashSet<char>();
         for( int i = 0; i < Pairs.Length; i++ )
         {
            characters.Add( Pairs[ i ].Open[ 0 ] );
         }

         return characters;
      }

      public static bool TryGetUnwrappedCore( string text, out string unwrapped )
      {
         unwrapped = text;
         if( string.IsNullOrEmpty( text ) )
         {
            return false;
         }

         if( !TryFindFinalCoreBounds( text, out var start, out var end ) )
         {
            return false;
         }

         unwrapped = text.Substring( start, end - start );
         return true;
      }

      public static bool TryUnwrapAll( string text, out string unwrapped, out string prefix, out string suffix )
      {
         unwrapped = text;
         prefix = null;
         suffix = null;

         if( string.IsNullOrEmpty( text ) )
         {
            return false;
         }

         if( !TryFindFinalCoreBounds( text, out var start, out var end ) )
         {
            return false;
         }

         unwrapped = text.Substring( start, end - start );
         prefix = text.Substring( 0, start );
         suffix = text.Substring( end );
         return true;
      }

      private static bool TryFindFinalCoreBounds( string text, out int start, out int end )
      {
         start = 0;
         end = text.Length;
         var stripped = false;

         while( TryFindWrapperBounds( text, start, end, out var nextStart, out var nextEnd, out _ ) )
         {
            start = nextStart;
            end = nextEnd;
            stripped = true;
         }

         return stripped;
      }

      public static bool TryUnwrapOneLayer( string text, out string unwrapped, out Layer layer )
      {
         unwrapped = null;
         layer = null;

         if( string.IsNullOrEmpty( text ) )
         {
            return false;
         }

         if( !TryFindWrapperBounds( text, 0, text.Length, out var coreStart, out var coreEnd, out var kind ) )
         {
            return false;
         }

         unwrapped = text.Substring( coreStart, coreEnd - coreStart );
         layer = new Layer( text.Substring( 0, coreStart ), text.Substring( coreEnd ), kind );
         return true;
      }

      private static bool TryFindWrapperBounds( string text, int start, int end, out int coreStart, out int coreEnd, out LayerKind kind )
      {
         coreStart = start;
         coreEnd = end;
         kind = LayerKind.Paired;

         if( end - start < 2 )
         {
            return false;
         }

         var first = text[ start ];
         if( ( first >= 'A' && first <= 'Z' )
            || ( first >= 'a' && first <= 'z' )
            || ( first > 127 && char.IsLetter( first ) ) )
         {
            return false;
         }

         if( !char.IsWhiteSpace( first ) && !PairStartCharacters.Contains( first ) && !CouldStartWithListMarker( first ) )
         {
            return false;
         }

         while( start < end && char.IsWhiteSpace( text[ start ] ) )
         {
            start++;
         }

         while( end > start && char.IsWhiteSpace( text[ end - 1 ] ) )
         {
            end--;
         }

         if( start >= end )
         {
            return false;
         }

         var canStartWithPair = PairStartCharacters.Contains( text[ start ] );
         if( !canStartWithPair && !CouldStartWithListMarker( text[ start ] ) )
         {
            return false;
         }

         if( canStartWithPair && TryFindPair( text, start, end, out var pair, out var innerStart, out var innerEnd ) )
         {
            while( innerStart < innerEnd && char.IsWhiteSpace( text[ innerStart ] ) )
            {
               innerStart++;
            }

            while( innerEnd > innerStart && char.IsWhiteSpace( text[ innerEnd - 1 ] ) )
            {
               innerEnd--;
            }

            if( innerStart >= innerEnd )
            {
               return false;
            }

            coreStart = innerStart;
            coreEnd = innerEnd;
            kind = LayerKind.Paired;
            return true;
         }

         if( TryMatchListMarker( text, start, end, out var markerEnd ) )
         {
            if( markerEnd >= end )
            {
               return false;
            }

            coreStart = markerEnd;
            coreEnd = end;
            kind = LayerKind.ListMarker;
            return true;
         }

         return false;
      }

      public static string Wrap( IList<Layer> layers, string translation )
      {
         if( string.IsNullOrEmpty( translation ) || layers == null || layers.Count == 0 )
         {
            return translation;
         }

         var result = translation;
         for( int i = layers.Count - 1; i >= 0; i-- )
         {
            var layer = layers[ i ];

            if( layer.Kind == LayerKind.ListMarker )
            {
               if( HasListMarker( result ) )
               {
                  continue;
               }
            }
            else if( HasOuterPair( result ) )
            {
               continue;
            }

            result = layer.Prefix + result + layer.Suffix;
         }

         return result;
      }

      private static bool TryFindPair( string text, int start, int end, out Pair pair, out int innerStart, out int innerEnd )
      {
         for( int i = 0; i < Pairs.Length; i++ )
         {
            var candidate = Pairs[ i ];
            if( candidate.Open.Length + candidate.Close.Length >= end - start )
            {
               continue;
            }

            if( string.Compare( text, start, candidate.Open, 0, candidate.Open.Length, StringComparison.Ordinal ) != 0 )
            {
               continue;
            }

            if( string.Compare( text, end - candidate.Close.Length, candidate.Close, 0, candidate.Close.Length, StringComparison.Ordinal ) != 0 )
            {
               continue;
            }

            var candidateInnerStart = start + candidate.Open.Length;
            var candidateInnerEnd = end - candidate.Close.Length;
            if( candidateInnerStart >= candidateInnerEnd )
            {
               continue;
            }

            if( !IsSafeInner( text, candidateInnerStart, candidateInnerEnd, candidate ) )
            {
               continue;
            }

            pair = candidate;
            innerStart = candidateInnerStart;
            innerEnd = candidateInnerEnd;
            return true;
         }

         pair = null;
         innerStart = 0;
         innerEnd = 0;
         return false;
      }

      private static bool IsSafeInner( string text, int start, int end, Pair pair )
      {
         switch( pair.Kind )
         {
            case PairKind.Quote:
               return ContainsLetterOrDigit( text, start, end );

            case PairKind.Bracket:
               return ContainsLetter( text, start, end )
                  && !Contains( text, start, end, pair.Open )
                  && !Contains( text, start, end, pair.Close );

            case PairKind.TextBracket:
               return ContainsLetter( text, start, end )
                  && !Contains( text, start, end, pair.Open )
                  && !Contains( text, start, end, pair.Close )
                  && ContainsWhitespaceOrNonAsciiLetter( text, start, end );

            case PairKind.Angle:
               return ContainsLetter( text, start, end )
                  && !Contains( text, start, end, "<" )
                  && !Contains( text, start, end, ">" )
                  && !Contains( text, start, end, "＜" )
                  && !Contains( text, start, end, "＞" )
                  && !Contains( text, start, end, "=" )
                  && text[ start ] != '/'
                  && text[ end - 1 ] != '/'
                  && ContainsWhitespaceOrNonAsciiLetter( text, start, end );

            case PairKind.Markdown:
               return ContainsLetterOrDigit( text, start, end )
                  && !Contains( text, start, end, pair.Open );

            default:
               return false;
         }
      }

      private static bool Contains( string text, int start, int end, string value )
      {
         if( value.Length == 0 )
         {
            return false;
         }

         if( value.Length == 1 )
         {
            return text.IndexOf( value[ 0 ], start, end - start ) >= 0;
         }

         return text.IndexOf( value, start, end - start, StringComparison.Ordinal ) >= 0;
      }

      private static bool ContainsLetter( string text, int start, int end )
      {
         for( int i = start; i < end; i++ )
         {
            if( char.IsLetter( text[ i ] ) )
            {
               return true;
            }
         }

         return false;
      }

      private static bool ContainsLetterOrDigit( string text, int start, int end )
      {
         for( int i = start; i < end; i++ )
         {
            if( char.IsLetterOrDigit( text[ i ] ) )
            {
               return true;
            }
         }

         return false;
      }

      private static bool ContainsWhitespaceOrNonAsciiLetter( string text, int start, int end )
      {
         for( int i = start; i < end; i++ )
         {
            var c = text[ i ];
            if( char.IsWhiteSpace( c ) || ( c > 127 && char.IsLetter( c ) ) )
            {
               return true;
            }
         }

         return false;
      }

      private static bool TryMatchListMarker( string text, int start, int end, out int markerEnd )
      {
         markerEnd = start;
         if( start >= end )
         {
            return false;
         }

         var first = text[ start ];

         if( first == '(' || first == '[' || first == '（' || first == '［' )
         {
            var close = first == '(' ? ')' : first == '[' ? ']' : first == '（' ? '）' : '］';
            var index = start + 1;
            var digitStart = index;
            while( index < end && char.IsDigit( text[ index ] ) )
            {
               index++;
            }

            if( index == digitStart || index >= end || text[ index ] != close )
            {
               return false;
            }

            index++;
            return IsValidMarkerEnd( text, index, end, false, out markerEnd );
         }

         if( first == '{' && start + 1 < end && text[ start + 1 ] == '{' )
         {
            var closeIndex = text.IndexOf( "}}", start + 2, StringComparison.Ordinal );
            if( closeIndex > start + 2 && closeIndex < end )
            {
               var separatorIndex = closeIndex + 2;
               while( separatorIndex < end && char.IsWhiteSpace( text[ separatorIndex ] ) )
               {
                  separatorIndex++;
               }

               if( separatorIndex < end && IsListSeparator( text[ separatorIndex ] ) )
               {
                  if( IsValidMarkerEnd( text, separatorIndex + 1, end, true, out markerEnd ) )
                  {
                     return true;
                  }
               }
            }
         }

         var digitEnd = start;
         while( digitEnd < end && char.IsDigit( text[ digitEnd ] ) )
         {
            digitEnd++;
         }

         if( digitEnd > start )
         {
            var separatorIndex = digitEnd;
            while( separatorIndex < end && char.IsWhiteSpace( text[ separatorIndex ] ) )
            {
               separatorIndex++;
            }

            if( separatorIndex < end && IsListSeparator( text[ separatorIndex ] ) )
            {
               if( IsValidMarkerEnd( text, separatorIndex + 1, end, true, out markerEnd ) )
               {
                  return true;
               }
            }
         }

         if( IsCircledNumber( first ) )
         {
            var index = start + 1;
            if( index < end && IsListSeparator( text[ index ] ) )
            {
               index++;
            }

            while( index < end && char.IsWhiteSpace( text[ index ] ) )
            {
               index++;
            }

            if( ContainsLetterOrDigit( text, index, end ) )
            {
               markerEnd = index;
               return true;
            }
         }

         if( IsBulletSymbol( first ) )
         {
            if( IsValidMarkerEnd( text, start + 1, end, IsDashOrAsterisk( first ), out markerEnd ) )
            {
               return true;
            }
         }

         return false;
      }

      private static bool IsValidMarkerEnd( string text, int contentStart, int end, bool requireLetterOrWhitespace, out int markerEnd )
      {
         markerEnd = contentStart;
         if( contentStart >= end )
         {
            return false;
         }

         var afterWhitespace = contentStart;
         while( afterWhitespace < end && char.IsWhiteSpace( text[ afterWhitespace ] ) )
         {
            afterWhitespace++;
         }

         if( afterWhitespace >= end )
         {
            return false;
         }

         if( afterWhitespace > contentStart )
         {
            if( !ContainsLetterOrDigit( text, afterWhitespace, end ) )
            {
               return false;
            }
         }
         else if( !char.IsLetter( text[ afterWhitespace ] ) )
         {
            if( requireLetterOrWhitespace || !ContainsLetterOrDigit( text, afterWhitespace, end ) )
            {
               return false;
            }
         }

         markerEnd = afterWhitespace;
         return true;
      }

      private static bool IsListSeparator( char c )
      {
         return c == '.'
            || c == ')'
            || c == ':'
            || c == '-'
            || c == '\u2013'
            || c == '\u2014'
            || c == '．'
            || c == '）'
            || c == '：'
            || c == '－';
      }

      private static bool IsCircledNumber( char c )
      {
         return ( c >= '\u2460' && c <= '\u2473' )
            || ( c >= '\u2474' && c <= '\u2487' )
            || ( c >= '\u2488' && c <= '\u249B' )
            || c == '\u24EA'
            || ( c >= '\u24F5' && c <= '\u24FE' )
            || ( c >= '\u2776' && c <= '\u2793' );
      }

      private static bool IsBulletSymbol( char c )
      {
         switch( c )
         {
            case '•':
            case '·':
            case '▪':
            case '▫':
            case '◦':
            case '‣':
            case '⁃':
            case '∙':
            case '●':
            case '○':
            case '◆':
            case '◇':
            case '◈':
            case '►':
            case '▶':
            case '▸':
            case '▹':
            case '➤':
            case '➢':
            case '→':
            case '⇒':
            case '⇨':
            case '➔':
            case '➜':
            case '-':
            case '*':
            case '\u2013':
            case '\u2014':
               return true;
            default:
               return false;
         }
      }

      private static bool IsDashOrAsterisk( char c )
      {
         return c == '-' || c == '*' || c == '\u2013' || c == '\u2014';
      }

      private static bool CouldStartWithListMarker( char c )
      {
         return c == '('
            || c == '['
            || c == '（'
            || c == '［'
            || c == '{'
            || char.IsDigit( c )
            || IsCircledNumber( c )
            || IsBulletSymbol( c );
      }

      private static bool HasListMarker( string text )
      {
         var start = 0;
         var end = text.Length;
         while( start < end && char.IsWhiteSpace( text[ start ] ) )
         {
            start++;
         }

         while( end > start && char.IsWhiteSpace( text[ end - 1 ] ) )
         {
            end--;
         }

         if( TryFindPair( text, start, end, out var pair, out var innerStart, out var innerEnd ) )
         {
            start = innerStart;
            end = innerEnd;
            while( start < end && char.IsWhiteSpace( text[ start ] ) )
            {
               start++;
            }
         }

         return TryMatchListMarker( text, start, end, out var markerEnd );
      }

      private static bool HasOuterPair( string text )
      {
         var start = 0;
         var end = text.Length;
         while( start < end && char.IsWhiteSpace( text[ start ] ) )
         {
            start++;
         }

         if( TryMatchListMarker( text, start, end, out var markerEnd ) )
         {
            start = markerEnd;
         }

         while( start < end && char.IsWhiteSpace( text[ start ] ) )
         {
            start++;
         }

         while( end > start && char.IsWhiteSpace( text[ end - 1 ] ) )
         {
            end--;
         }

         if( start >= end )
         {
            return false;
         }

         return TryFindPair( text, start, end, out var pair, out var innerStart, out var innerEnd );
      }
   }
}
