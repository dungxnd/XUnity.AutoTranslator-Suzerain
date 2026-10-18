using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.Common.Extensions;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core
{
   internal enum TranslationFileLineKind
   {
      Translation,
      Regex,
      SplitterRegex,
      Directive
   }

   internal readonly struct TranslationFileLine
   {
      public TranslationFileLine(
         TranslationFileLineKind kind,
         string key,
         string value,
         TranslationFileDirective directive )
      {
         Kind = kind;
         Key = key;
         Value = value;
         Directive = directive;
      }

      public TranslationFileLineKind Kind { get; }

      public string Key { get; }

      public string Value { get; }

      public TranslationFileDirective Directive { get; }
   }

   internal sealed class TranslationFileSection
   {
      public TranslationFileSection( string name, List<TranslationFileLine> lines )
      {
         Name = name;
         Lines = lines;
      }

      public string Name { get; }

      public List<TranslationFileLine> Lines { get; }
   }

   internal sealed class TranslationFileSet
   {
      public TranslationFileSet(
         string path,
         bool isOutputFile,
         long length,
         DateTime lastWriteTimeUtc,
         List<TranslationFileSection> sections )
      {
         Path = path;
         IsOutputFile = isOutputFile;
         Length = length;
         LastWriteTimeUtc = lastWriteTimeUtc;
         Sections = sections;
      }

      public string Path { get; }

      public bool IsOutputFile { get; }

      public long Length { get; }

      public DateTime LastWriteTimeUtc { get; }

      public List<TranslationFileSection> Sections { get; }

      public bool Matches( FileInfo file )
      {
         return file.Exists
            && file.Length == Length
            && file.LastWriteTimeUtc == LastWriteTimeUtc;
      }
   }

   internal sealed class TranslationFileReadResult
   {
      public TranslationFileReadResult( TranslationFileSet set, int skippedLines )
      {
         Set = set;
         SkippedLines = skippedLines;
      }

      public TranslationFileSet Set { get; }

      public int SkippedLines { get; }
   }

   /// <summary>
   /// Line-based reader shared by the translation caches. Reading is streamed so
   /// large files do not need a second UTF-16 copy of the whole content.
   /// </summary>
   internal static class TranslationFileReader
   {
      private const int FileBufferSize = 64 * 1024;
      private const int EntryBufferSize = 4 * 1024;

      internal static int TotalFileReads { get; private set; }

      internal static void ResetReadCount()
      {
         TotalFileReads = 0;
      }

      internal static IEnumerable<string> ReadLines( TextReader reader )
      {
         string line;
         while( ( line = reader.ReadLine() ) != null )
         {
            yield return line;
         }
      }

      internal static TranslationFileReadResult ReadFile(
         string path,
         bool isOutputFile,
         Predicate<string> includeEntry = null )
      {
         TotalFileReads++;

         var fileInfo = new FileInfo( path );
         var length = fileInfo.Length;
         var lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
         var sections = new List<TranslationFileSection>();
         var skippedLines = 0;

         using( var stream = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.Read, FileBufferSize, FileOptions.SequentialScan ) )
         {
            if( path.EndsWith( ".zip", StringComparison.OrdinalIgnoreCase ) )
            {
               var zipFile = new ZipFile( stream );
               try
               {
                  var entries = new List<ZipEntry>();
                  foreach( ZipEntry entry in zipFile )
                  {
                     entries.Add( entry );
                  }
                  entries.Sort( ( x, y ) => StringComparer.OrdinalIgnoreCase.Compare( y.Name, x.Name ) );

                  foreach( var entry in entries )
                  {
                     if( !entry.IsFile
                        || !entry.Name.EndsWith( ".txt", StringComparison.OrdinalIgnoreCase )
                        || ( includeEntry != null && !includeEntry( entry.Name ) ) )
                     {
                        continue;
                     }

                     var entryName = path + Path.DirectorySeparatorChar + entry.Name;
                     using( var entryStream = zipFile.GetInputStream( entry ) )
                     using( var reader = new StreamReader( entryStream, Encoding.UTF8, true, EntryBufferSize ) )
                     {
                        var section = ReadSection( reader, entryName, isOutputFile, ref skippedLines );
                        sections.Add( section );
                     }
                  }
               }
               finally
               {
                  zipFile.Close();
               }
            }
            else
            {
               using( var reader = new StreamReader( stream, Encoding.UTF8, true, FileBufferSize ) )
               {
                  sections.Add( ReadSection( reader, path, isOutputFile, ref skippedLines ) );
               }
            }
         }

         var set = new TranslationFileSet(
            path,
            isOutputFile,
            length,
            lastWriteTimeUtc,
            sections );

         return new TranslationFileReadResult( set, skippedLines );
      }

      internal static TranslationFileReadResult ReadStream(
         Stream stream,
         string name,
         bool isOutputFile,
         bool closeStream )
      {
         var skippedLines = 0;
         var reader = new StreamReader( stream, Encoding.UTF8, true, EntryBufferSize );
         try
         {
            var section = ReadSection( reader, name, isOutputFile, ref skippedLines );
            var set = new TranslationFileSet( name, isOutputFile, 0, DateTime.MinValue, new List<TranslationFileSection> { section } );
            return new TranslationFileReadResult( set, skippedLines );
         }
         finally
         {
            if( closeStream )
            {
               reader.Dispose();
            }
         }
      }

      private static TranslationFileSection ReadSection(
         TextReader reader,
         string name,
         bool isOutputFile,
         ref int skippedLines )
      {
         var lines = new List<TranslationFileLine>();

         foreach( var line in ReadLines( reader ) )
         {
            if( line.IsNullOrWhiteSpace() )
            {
               continue;
            }

            var trimmed = line.TrimStart();
            if( trimmed.StartsWith( "//", StringComparison.Ordinal ) )
            {
               continue;
            }

            if( trimmed.Length > 0 && trimmed[ 0 ] == '#' )
            {
               if( !isOutputFile )
               {
                  TranslationFileDirective directive;
                  try
                  {
                     directive = TranslationFileDirective.Create( line );
                  }
                  catch( Exception ex )
                  {
                     skippedLines++;
                     XuaLogger.AutoTranslator.Warn( ex, $"An error occurred while reading directive: '{line}'." );
                     continue;
                  }

                  if( directive != null )
                  {
                     lines.Add( new TranslationFileLine( TranslationFileLineKind.Directive, null, null, directive ) );
                     continue;
                  }
               }

               // A '#' line without '=' is a comment. A line such as '#key=value'
               // falls through and is parsed as a normal translation key.
               if( line.IndexOf( '=' ) < 0 )
               {
                  continue;
               }
            }

            string[] kvp;
            try
            {
               kvp = TextHelper.ReadTranslationLineAndDecode( line );
            }
            catch( Exception ex )
            {
               skippedLines++;
               XuaLogger.AutoTranslator.Warn( ex, $"An error occurred while reading translation: '{line}'." );
               continue;
            }

            if( kvp == null )
            {
               skippedLines++;
               continue;
            }

            var key = kvp[ 0 ];
            var value = kvp[ 1 ];
            if( string.IsNullOrEmpty( key ) || string.IsNullOrEmpty( value ) )
            {
               skippedLines++;
               continue;
            }

            var kind = key.StartsWith( "sr:", StringComparison.Ordinal )
               ? TranslationFileLineKind.SplitterRegex
               : key.StartsWith( "r:", StringComparison.Ordinal )
                  ? TranslationFileLineKind.Regex
                  : TranslationFileLineKind.Translation;

            lines.Add( new TranslationFileLine( kind, key, value, null ) );
         }

         return new TranslationFileSection( name, lines );
      }
   }
}
