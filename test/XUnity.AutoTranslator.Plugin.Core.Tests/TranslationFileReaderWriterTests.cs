using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using XUnity.AutoTranslator.Plugin.Core;

namespace XUnity.AutoTranslator.Plugin.Core.Tests
{
   public class TranslationFileReaderWriterTests
   {
      [Fact( DisplayName = "Reader_Parses_Directives_Regexes_And_Escaped_Equals" )]
      public void Reader_Parses_Directives_Regexes_And_Escaped_Equals()
      {
         var directory = CreateTemporaryDirectory();
         try
         {
            var path = Path.Combine( directory, "translations.txt" );
            File.WriteAllText(
               path,
               "// comment\r\n" +
               "#set level 1,2\r\n" +
               "key1=value1\r\n" +
               "#enable fallback\r\n" +
               "key\\=with=equals\r\n" +
               "r:\"^a$\"=A\r\n" +
               "sr:\"x\"=y\r\n" +
               "broken-line\r\n",
               new UTF8Encoding( true ) );

            var result = TranslationFileReader.ReadFile( path, false );
            var lines = result.Set.Sections.Single().Lines;

            Assert.Equal( 6, lines.Count );
            Assert.Equal( TranslationFileLineKind.Directive, lines[ 0 ].Kind );
            Assert.Equal( TranslationFileLineKind.Translation, lines[ 1 ].Kind );
            Assert.Equal( TranslationFileLineKind.Directive, lines[ 2 ].Kind );
            Assert.Equal( TranslationFileLineKind.Translation, lines[ 3 ].Kind );
            Assert.Equal( "key=with", lines[ 3 ].Key );
            Assert.Equal( "equals", lines[ 3 ].Value );
            Assert.Equal( TranslationFileLineKind.Regex, lines[ 4 ].Kind );
            Assert.Equal( TranslationFileLineKind.SplitterRegex, lines[ 5 ].Kind );
            Assert.Equal( 1, result.SkippedLines );
         }
         finally
         {
            Directory.Delete( directory, true );
         }
      }

      [Fact( DisplayName = "Append_Adds_A_Line_Break_When_The_File_Does_Not_End_With_One" )]
      public void Append_Adds_A_Line_Break_When_The_File_Does_Not_End_With_One()
      {
         var directory = CreateTemporaryDirectory();
         try
         {
            var path = Path.Combine( directory, "translations.txt" );
            File.WriteAllText( path, "a=b", new UTF8Encoding( false ) );

            TranslationWriter.Append( path, new[]
            {
               new KeyValuePair<string, string>( "c", "d" ),
               new KeyValuePair<string, string>( "e", "f" )
            } );

            Assert.Equal( new[] { "a=b", "c=d", "e=f" }, File.ReadAllLines( path ) );
         }
         finally
         {
            Directory.Delete( directory, true );
         }
      }

      [Fact( DisplayName = "Atomic_Rewrite_Preserves_Comments_Directives_And_Regex_Lines" )]
      public void Atomic_Rewrite_Preserves_Comments_Directives_And_Regex_Lines()
      {
         var directory = CreateTemporaryDirectory();
         try
         {
            var path = Path.Combine( directory, "translations.txt" );
            var original = "// comment\r\n#set level 1\r\nr:\"^pattern$\"=translated\r\nkeep=me\r\nhttp:\\/\\/a\\/\\/b=url\r\n";
            File.WriteAllText( path, original, new UTF8Encoding( true ) );

            var changed = TranslationWriter.RewriteAtomic( path, ( reader, writer ) =>
            {
               var removed = false;
               foreach( var line in TranslationFileReader.ReadLines( reader ) )
               {
                  if( line == "keep=me" )
                  {
                     removed = true;
                     continue;
                  }

                  writer.WriteLine( line );
               }

               return removed;
            } );

            Assert.True( changed );

            var content = File.ReadAllText( path );
            Assert.DoesNotContain( "keep=me", content );
            Assert.Contains( "// comment\r\n", content );
            Assert.Contains( "#set level 1\r\n", content );
            Assert.Contains( "r:\"^pattern$\"=translated\r\n", content );
            Assert.Contains( "http:\\/\\/a\\/\\/b=url", content );
            Assert.Single( Directory.GetFiles( directory, "*.bak" ) );
         }
         finally
         {
            Directory.Delete( directory, true );
         }
      }

      [Fact( DisplayName = "Pruner_Only_Removes_Plain_Lines_Covered_By_A_Regex" )]
      public void Pruner_Only_Removes_Plain_Lines_Covered_By_A_Regex()
      {
         var regexes = new List<RegexTranslation> { new RegexTranslation( "r:\"^drop$\"", "x" ) };

         Assert.True( TranslationPruner.ShouldPruneLine( "drop=value", regexes ) );
         Assert.False( TranslationPruner.ShouldPruneLine( "keep=value", regexes ) );
         Assert.False( TranslationPruner.ShouldPruneLine( "// drop comment", regexes ) );
         Assert.False( TranslationPruner.ShouldPruneLine( "#set level 1", regexes ) );
         Assert.False( TranslationPruner.ShouldPruneLine( "r:\"^drop$\"=x", regexes ) );
         Assert.False( TranslationPruner.ShouldPruneLine( "sr:\"drop\"=x", regexes ) );
      }

      private static string CreateTemporaryDirectory()
      {
         var directory = Path.Combine( Path.GetTempPath(), "xuat-tests-" + Guid.NewGuid().ToString( "N" ) );
         Directory.CreateDirectory( directory );
         return directory;
      }
   }
}
