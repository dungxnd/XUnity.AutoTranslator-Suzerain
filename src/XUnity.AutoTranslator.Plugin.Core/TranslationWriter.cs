using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.Common.Extensions;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core
{
   internal static class TranslationWriter
   {
      private const int FileBufferSize = 64 * 1024;

      private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding( false );
      private static readonly UTF8Encoding Utf8WithBom = new UTF8Encoding( true );

      internal static void Append( string path, IEnumerable<KeyValuePair<string, string>> translations )
      {
         var directory = Path.GetDirectoryName( path );
         if( !string.IsNullOrEmpty( directory ) )
         {
            Directory.CreateDirectory( directory );
         }

         var needsLeadingNewLine = NeedsLeadingNewLine( path );

         using( var stream = new FileStream( path, FileMode.Append, FileAccess.Write, FileShare.Read, FileBufferSize ) )
         using( var writer = new StreamWriter( stream, Utf8WithoutBom ) )
         {
            if( needsLeadingNewLine )
            {
               XuaLogger.AutoTranslator.Warn( "The translation file did not end with a newline. Inserting one before appending." );
               writer.WriteLine();
            }

            foreach( var translation in translations )
            {
               writer.WriteLine( TextHelper.Encode( translation.Key ) + '=' + TextHelper.Encode( translation.Value ) );
            }

            writer.Flush();
         }
      }

      /// <summary>
      /// Rewrites the file through a temporary file in the same directory and replaces
      /// the original with <see cref="File.Replace(string, string, string, bool)"/>.
      /// The rewrite callback returns true when the file actually changed.
      /// </summary>
      internal static bool RewriteAtomic( string path, Func<TextReader, TextWriter, bool> rewrite )
      {
         var directory = Path.GetDirectoryName( path );
         if( !string.IsNullOrEmpty( directory ) )
         {
            Directory.CreateDirectory( directory );
         }

         var hasBom = HasUtf8Bom( path );
         var newLine = DetectNewLine( path );
         long originalLength;
         DateTime originalLastWriteTimeUtc;
         SaveFileStamp( path, out originalLength, out originalLastWriteTimeUtc );
         var tempPath = path + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";

         try
         {
            bool changed;
            using( var reader = new StreamReader(
               new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.Read, FileBufferSize, FileOptions.SequentialScan ),
               Encoding.UTF8,
               true,
               FileBufferSize ) )
            using( var writer = new StreamWriter(
               new FileStream( tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileBufferSize ),
               hasBom ? Utf8WithBom : Utf8WithoutBom ) )
            {
               writer.NewLine = newLine;
               changed = rewrite( reader, writer );
               writer.Flush();
            }

            if( !changed )
            {
               File.Delete( tempPath );
               return false;
            }

            if( !FileMatches( path, originalLength, originalLastWriteTimeUtc ) )
            {
               XuaLogger.AutoTranslator.Warn( "The translation file changed while it was being rewritten. Aborting the rewrite to avoid overwriting external edits." );
               File.Delete( tempPath );
               return false;
            }

            ReplaceFile( path, tempPath );
            return true;
         }
         catch
         {
            TryDelete( tempPath );
            throw;
         }
      }

      internal static bool FileMatches( string path, long length, DateTime lastWriteTimeUtc )
      {
         var file = new FileInfo( path );
         return file.Exists
            && file.Length == length
            && file.LastWriteTimeUtc == lastWriteTimeUtc;
      }

      internal static void SaveFileStamp( string path, out long length, out DateTime lastWriteTimeUtc )
      {
         var file = new FileInfo( path );
         if( file.Exists )
         {
            length = file.Length;
            lastWriteTimeUtc = file.LastWriteTimeUtc;
         }
         else
         {
            length = 0;
            lastWriteTimeUtc = DateTime.MinValue;
         }
      }

      private static void ReplaceFile( string path, string tempPath )
      {
         var backupPath = path + "." + DateTime.Now.ToString( "yyyyMMddHHmmssfff" ) + ".bak";

         if( File.Exists( path ) )
         {
            try
            {
               File.Replace( tempPath, path, backupPath, true );
               XuaLogger.AutoTranslator.Debug( "Generated backup translation file: " + backupPath );
               PruneBackups( path, 5 );
               return;
            }
            catch( PlatformNotSupportedException )
            {
               // Some Unity/Mono runtimes do not implement File.Replace.
            }
            catch( NotSupportedException )
            {
               // Some Unity/Mono runtimes do not implement File.Replace.
            }

            XuaLogger.AutoTranslator.Warn( "File.Replace is not supported on this runtime. Falling back to a non-atomic replacement." );
            File.Move( path, backupPath );
            File.Move( tempPath, path );
            PruneBackups( path, 5 );
         }
         else
         {
            File.Move( tempPath, path );
         }
      }

      private static void PruneBackups( string path, int keep )
      {
         try
         {
            var directory = Path.GetDirectoryName( path );
            if( string.IsNullOrEmpty( directory ) )
            {
               return;
            }

            var backups = Directory.GetFiles( directory, Path.GetFileName( path ) + ".*.bak" );
            if( backups.Length <= keep )
            {
               return;
            }

            Array.Sort( backups, StringComparer.Ordinal );
            for( int i = 0; i < backups.Length - keep; i++ )
            {
               TryDelete( backups[ i ] );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "An error occurred while pruning old translation backups." );
         }
      }

      private static bool NeedsLeadingNewLine( string path )
      {
         var file = new FileInfo( path );
         if( !file.Exists || file.Length == 0 )
         {
            return false;
         }

         using( var stream = File.OpenRead( path ) )
         {
            stream.Seek( -1, SeekOrigin.End );
            var last = stream.ReadByte();
            return last != '\n' && last != '\r';
         }
      }

      private static bool HasUtf8Bom( string path )
      {
         var file = new FileInfo( path );
         if( !file.Exists || file.Length < 3 )
         {
            return false;
         }

         using( var stream = File.OpenRead( path ) )
         {
            return stream.ReadByte() == 0xEF
               && stream.ReadByte() == 0xBB
               && stream.ReadByte() == 0xBF;
         }
      }

      private static string DetectNewLine( string path )
      {
         var file = new FileInfo( path );
         if( file.Exists )
         {
            using( var stream = File.OpenRead( path ) )
            {
               var buffer = new byte[ 8192 ];
               var read = stream.Read( buffer, 0, buffer.Length );
               for( int i = 0; i < read; i++ )
               {
                  if( buffer[ i ] == '\n' )
                  {
                     return i > 0 && buffer[ i - 1 ] == '\r' ? "\r\n" : "\n";
                  }

                  if( buffer[ i ] == '\r' )
                  {
                     return i + 1 < read && buffer[ i + 1 ] == '\n' ? "\r\n" : "\r";
                  }
               }
            }
         }

         return Environment.NewLine;
      }

      private static void TryDelete( string path )
      {
         try
         {
            if( File.Exists( path ) )
            {
               File.Delete( path );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Warn( e, "An error occurred while deleting temporary translation file: " + path );
         }
      }
   }

   internal static class TranslationPruner
   {
      internal static bool ShouldPruneLine( string line, IList<RegexTranslation> regexes )
      {
         if( line.IsNullOrWhiteSpace() )
         {
            return false;
         }

         var trimmed = line.TrimStart();
         if( trimmed.StartsWith( "//", StringComparison.Ordinal )
            || ( trimmed.Length > 0 && trimmed[ 0 ] == '#' ) )
         {
            return false;
         }

         string[] kvp;
         try
         {
            kvp = TextHelper.ReadTranslationLineAndDecode( line );
         }
         catch( Exception )
         {
            return false;
         }

         if( kvp == null )
         {
            return false;
         }

         var key = kvp[ 0 ];
         if( string.IsNullOrEmpty( key )
            || key.StartsWith( "r:", StringComparison.Ordinal )
            || key.StartsWith( "sr:", StringComparison.Ordinal ) )
         {
            return false;
         }

         for( int i = 0; i < regexes.Count; i++ )
         {
            var regex = regexes[ i ];
            try
            {
               if( regex.CompiledRegex.IsMatch( key ) )
               {
                  return true;
               }
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Error( e, $"Failed while attempting to match regex '{regex.Original}' during pruning." );
            }
         }

         return false;
      }
   }
}
