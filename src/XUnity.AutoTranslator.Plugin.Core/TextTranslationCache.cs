using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.AutoTranslator.Plugin.Core.Parsing;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.AutoTranslator.Plugin.Utilities;
using XUnity.Common.Extensions;
using XUnity.Common.Logging;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core
{
   sealed class TextTranslationCache : IReadOnlyTextTranslationCache, IDisposable
   {
      public event Action TextTranslationFileChanged;

      private Dictionary<IReadOnlyTextTranslationCache, CompositeTextTranslationCache> _compositeCaches = new Dictionary<IReadOnlyTextTranslationCache, CompositeTextTranslationCache>();

      private static readonly List<KeyValuePairTranslationPackage> _kvpPackages = new List<KeyValuePairTranslationPackage>();
      private static readonly List<StreamTranslationPackage> _streamPackages = new List<StreamTranslationPackage>();

      /// <summary>
      /// All the translations are stored in this dictionary.
      /// </summary>
      private Dictionary<string, string> _staticTranslations = new Dictionary<string, string>();

      private Dictionary<string, string> _translations = new Dictionary<string, string>();
      private Dictionary<string, string> _reverseTranslations = new Dictionary<string, string>();
      private Dictionary<string, string> _tokenTranslations = new Dictionary<string, string>();
      private Dictionary<string, string> _reverseTokenTranslations = new Dictionary<string, string>();
      private HashSet<string> _partialTranslations = new HashSet<string>();
      private List<RegexTranslation> _defaultRegexes = new List<RegexTranslation>();
      private HashSet<string> _registeredRegexes = new HashSet<string>();
      private HashSet<string> _failedRegexLookups = new HashSet<string>();
      private HashSet<string> _previousFailedRegexLookups = new HashSet<string>();
      private List<RegexTranslationSplitter> _splitterRegexes = new List<RegexTranslationSplitter>();
      private HashSet<string> _registeredSplitterRegexes = new HashSet<string>();
      private Dictionary<int, TranslationDictionaries> _scopedTranslations = new Dictionary<int, TranslationDictionaries>();

      /// <summary>
      /// These are the new translations that has not yet been persisted to the file system.
      /// </summary>
      private object _writeToFileSync = new object();
      private Dictionary<string, string> _newTranslations = new Dictionary<string, string>();
      private bool disposedValue;
      private readonly DirectoryInfo _pluginDirectory;
      private SafeFileWatcher _fileWatcher;
      private readonly Action _maintenanceAction;

      private readonly Dictionary<string, TranslationFileSet> _fileSets = new Dictionary<string, TranslationFileSet>( StringComparer.OrdinalIgnoreCase );
      private int _translationCapacity;
      private long _mainFileLength;
      private DateTime _mainFileLastWriteUtc;

      public TextTranslationCache()
      {
         AllowGeneratingNewTranslations = true;
         AllowFallback = false;
         DefaultAllowFallback = false;

         LoadStaticTranslations();

         if( Settings.ReloadTranslationsOnFileChange )
         {
            try
            {
               Directory.CreateDirectory( Settings.TranslationsPath );
               _fileWatcher = new SafeFileWatcher( Settings.TranslationsPath, ".txt", ".zip" );
               _fileWatcher.DirectoryUpdated += FileWatcher_DirectoryUpdated;
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Error( e, "An error occurred while initializing translation file watching for text." );
            }
         }

         // start function to write translations to file
         _maintenanceAction = SaveNewTranslationsToDisk;
         MaintenanceHelper.AddMaintenanceFunction( _maintenanceAction, 1 );
      }

      private void FileWatcher_DirectoryUpdated()
      {
         if( disposedValue ) return;

         TextTranslationFileChanged?.Invoke();
      }

      public TextTranslationCache( DirectoryInfo pluginDirectory )
      {
         AllowGeneratingNewTranslations = false;
         AllowFallback = false;
         DefaultAllowFallback = false;

         _pluginDirectory = pluginDirectory;
      }

      public TextTranslationCache( string pluginDirectory )
      {
         AllowGeneratingNewTranslations = false;
         AllowFallback = false;
         DefaultAllowFallback = false;

         _pluginDirectory = new DirectoryInfo( Path.Combine( Path.Combine( Settings.TranslationsPath, "plugins" ), pluginDirectory ) );
      }

      public bool DefaultAllowFallback { get; internal set; }

      public bool AllowFallback { get; internal set; }

      public bool AllowGeneratingNewTranslations { get; private set; }

      public bool HasLoadedInMemoryTranslations => _kvpPackages.Count > 0 || _streamPackages.Count > 0;

      private IEnumerable<string> GetTranslationFiles()
      {
         return Directory.GetFiles( _pluginDirectory?.FullName ?? Settings.TranslationsPath, $"*", SearchOption.AllDirectories )
            .Where( x => x.EndsWith( ".txt", StringComparison.OrdinalIgnoreCase ) || x.EndsWith( ".zip", StringComparison.OrdinalIgnoreCase ) )
            .Where( x => !x.EndsWith( "resizer.txt", StringComparison.OrdinalIgnoreCase ) )
            .Select( x => new FileInfo( x ) )
            .Select( fi => new
            {
               IsZipped = fi.FullName.EndsWith( ".zip", StringComparison.OrdinalIgnoreCase ),
               FileInfo = fi
            } )
            .OrderByDescending( x => x.IsZipped )
            .ThenByDescending( x => x.FileInfo.FullName, StringComparer.OrdinalIgnoreCase )
            .Select( x => x.FileInfo.FullName );
      }

      internal CompositeTextTranslationCache GetOrCreateCompositeCache( IReadOnlyTextTranslationCache primary )
      {
         if( !_compositeCaches.TryGetValue( primary, out var compo ) )
         {
            compo = new CompositeTextTranslationCache( primary, this );
            _compositeCaches[ primary ] = compo;
         }
         return compo;
      }

      public void PruneMainTranslationFile()
      {
         var mainTranslationFile = Settings.AutoTranslationsFilePath;
         if( !File.Exists( mainTranslationFile ) )
         {
            return;
         }

         XuaLogger.AutoTranslator.Debug( "Pruning text translations in main translation file..." );

         var regexes = CollectUnscopedRegexesFromOtherFiles();
         if( regexes.Count == 0 )
         {
            XuaLogger.AutoTranslator.Debug( "No regex translations found. Nothing to prune." );
            return;
         }

         var prunedLines = 0;
         var changes = TranslationWriter.RewriteAtomic( mainTranslationFile, ( reader, writer ) =>
         {
            var changed = false;
            foreach( var line in TranslationFileReader.ReadLines( reader ) )
            {
               if( TranslationPruner.ShouldPruneLine( line, regexes ) )
               {
                  prunedLines++;
                  changed = true;

                  if( prunedLines <= 10 )
                  {
                     XuaLogger.AutoTranslator.Warn( "Pruned translation: " + line );
                  }
               }
               else
               {
                  writer.WriteLine( line );
               }
            }

            return changed;
         } );

         if( changes )
         {
            XuaLogger.AutoTranslator.Info( $"Pruned {prunedLines} translation line(s) from the main translation file." );
            TranslationWriter.SaveFileStamp( mainTranslationFile, out _mainFileLength, out _mainFileLastWriteUtc );
         }
      }

      private List<RegexTranslation> CollectUnscopedRegexesFromOtherFiles()
      {
         var registeredRegexes = new HashSet<string>();
         var regexes = new List<RegexTranslation>();

         var pluginsDir = Path.Combine( Settings.TranslationsPath, "plugins" ) + Path.DirectorySeparatorChar;
         var mainTranslationFile = new FileInfo( Settings.AutoTranslationsFilePath ).FullName;
         var substitutionFile = new FileInfo( Settings.SubstitutionFilePath ).FullName;
         var preprocessorsFile = new FileInfo( Settings.PreprocessorsFilePath ).FullName;
         var postprocessorsFile = new FileInfo( Settings.PostprocessorsFilePath ).FullName;

         foreach( var fullFileName in GetTranslationFiles().Except(
            new[] { mainTranslationFile, substitutionFile, preprocessorsFile, postprocessorsFile },
            StringComparer.OrdinalIgnoreCase ) )
         {
            try
            {
               if( _pluginDirectory == null && fullFileName.StartsWith( pluginsDir, StringComparison.OrdinalIgnoreCase ) )
               {
                  continue;
               }

               var set = GetOrReadFileSet( fullFileName, false, out _ );
               foreach( var section in set.Sections )
               {
                  var context = new TranslationFileLoadingContext();
                  foreach( var line in section.Lines )
                  {
                     if( line.Kind == TranslationFileLineKind.Directive )
                     {
                        context.Apply( line.Directive );
                        continue;
                     }

                     if( line.Kind != TranslationFileLineKind.Regex
                        || !context.IsApplicable()
                        || context.GetLevels().Count != 0
                        || !registeredRegexes.Add( line.Key ) )
                     {
                        continue;
                     }

                     try
                     {
                        regexes.Add( new RegexTranslation( line.Key, line.Value ) );
                     }
                     catch( Exception e )
                     {
                        XuaLogger.AutoTranslator.Warn( e, $"An error occurred while constructing regex translation: '{line.Key}={line.Value}'." );
                     }
                  }
               }
            }
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Error( e, "An error occurred while loading translations in file: " + fullFileName );
            }
         }

         return regexes;
      }

      internal void LoadTranslationFiles()
      {
         try
         {
            AllowFallback = DefaultAllowFallback;

            if( _pluginDirectory != null )
            {
               XuaLogger.AutoTranslator.Debug( $"--- Loading Plugin Translations ({_pluginDirectory.Name}) ---" );
            }
            else
            {
               XuaLogger.AutoTranslator.Debug( $"--- Loading Global Translations ---" );
            }

            var stopwatch = Stopwatch.StartNew();
            var startTime = 0d;
            var skippedLines = 0;

            lock( _writeToFileSync )
            {
               string pluginsDir = Path.Combine( Settings.TranslationsPath, "plugins" );

               Directory.CreateDirectory( Settings.TranslationsPath );
               if( _pluginDirectory != null )
               {
                  Directory.CreateDirectory( pluginsDir );
                  Directory.CreateDirectory( _pluginDirectory.FullName );
               }
               else
               {
                  Directory.CreateDirectory( Path.GetDirectoryName( Settings.AutoTranslationsFilePath ) );
               }

               var filesToLoad = GetTranslationFilesToLoad();
               var fileSets = new List<TranslationFileSet>( filesToLoad.Count );
               foreach( var file in filesToLoad )
               {
                  try
                  {
                     var set = GetOrReadFileSet( file.Key, file.Value, out var fileSkippedLines );
                     fileSets.Add( set );
                     skippedLines += fileSkippedLines;
                  }
                  catch( Exception e )
                  {
                     XuaLogger.AutoTranslator.Error( e, "An error occurred while loading translations in file: " + file.Key );
                  }
               }

               RemoveStaleFileSets( filesToLoad );

               var estimatedEntries = 0;
               foreach( var set in fileSets )
               {
                  foreach( var section in set.Sections )
                  {
                     estimatedEntries += section.Lines.Count;
                  }
               }

               PrepareTranslationState( estimatedEntries );

               foreach( var set in fileSets )
               {
                  MergeFileSet( set, true );
               }

               foreach( var streamPackage in _streamPackages )
               {
                  try
                  {
                     var stream = streamPackage.GetReadableStream();
                     if( stream.CanSeek )
                     {
                        stream.Seek( 0, SeekOrigin.Begin );
                     }

                     // The stream package may hand out the same stream again on reload.
                     var result = TranslationFileReader.ReadStream( stream, streamPackage.Name, false, false );
                     skippedLines += result.SkippedLines;
                     MergeFileSet( result.Set, true );
                  }
                  catch( Exception e )
                  {
                     XuaLogger.AutoTranslator.Error( e, "An error occurred while loading translations in stream translation package: " + streamPackage.Name );
                  }
               }

               foreach( var kvpPackage in _kvpPackages )
               {
                  try
                  {
                     var iterable = kvpPackage.GetIterableEntries();

                     LoadTranslationsInKeyValuePairs( iterable, kvpPackage.Name );
                  }
                  catch( Exception e )
                  {
                     XuaLogger.AutoTranslator.Error( e, "An error occurred while loading translations in KVP translation package: " + kvpPackage.Name );
                  }
               }
            }

            var endTime = stopwatch.Elapsed.TotalSeconds;

            XuaLogger.AutoTranslator.Debug( $"Loaded translation text files (took {Math.Round( endTime - startTime, 2 )} seconds)" );
            if( skippedLines > 0 )
            {
               XuaLogger.AutoTranslator.Warn( $"Skipped {skippedLines} malformed translation line(s) while loading." );
            }

            if( _pluginDirectory == null )
            {
               TranslationWriter.SaveFileStamp( Settings.AutoTranslationsFilePath, out _mainFileLength, out _mainFileLastWriteUtc );
            }

            // generate variations of created translations
            {
               startTime = stopwatch.Elapsed.TotalSeconds;

               foreach( var kvp in _translations.ToList() )
               {
                  AddWrapperVariation( kvp.Key, kvp.Value, TranslationScopes.None );

                  if( !NeedsWhitespaceVariations( kvp.Key ) )
                  {
                     continue;
                  }

                  // also add a modified version of the translation
                  var ukey = new UntranslatedText( kvp.Key, false, true, Settings.FromLanguageUsesWhitespaceBetweenWords, true, Settings.TemplateAllNumberAway );
                  var uvalue = new UntranslatedText( kvp.Value, false, true, Settings.ToLanguageUsesWhitespaceBetweenWords, true, Settings.TemplateAllNumberAway );
                  if( ukey.Original_Text_ExternallyTrimmed != kvp.Key && !HasTranslated( ukey.Original_Text_ExternallyTrimmed, TranslationScopes.None, false ) )
                  {
                     AddTranslation( ukey.Original_Text_ExternallyTrimmed, uvalue.Original_Text_ExternallyTrimmed, TranslationScopes.None );
                  }
                  if( ukey.Original_Text_ExternallyTrimmed != ukey.Original_Text_FullyTrimmed && !HasTranslated( ukey.Original_Text_FullyTrimmed, TranslationScopes.None, false ) )
                  {
                     AddTranslation( ukey.Original_Text_FullyTrimmed, uvalue.Original_Text_FullyTrimmed, TranslationScopes.None );
                  }
               }

               // Generate variations for scoped
               foreach( var scopeKvp in _scopedTranslations.ToList() )
               {
                  var scope = scopeKvp.Key;

                  foreach( var kvp in scopeKvp.Value.Translations.ToList() )
                  {
                     AddWrapperVariation( kvp.Key, kvp.Value, scope );

                     if( !NeedsWhitespaceVariations( kvp.Key ) )
                     {
                        continue;
                     }

                     // also add a modified version of the translation
                     var ukey = new UntranslatedText( kvp.Key, false, true, Settings.FromLanguageUsesWhitespaceBetweenWords, true, Settings.TemplateAllNumberAway );
                     var uvalue = new UntranslatedText( kvp.Value, false, true, Settings.ToLanguageUsesWhitespaceBetweenWords, true, Settings.TemplateAllNumberAway );
                     if( ukey.Original_Text_ExternallyTrimmed != kvp.Key && !HasTranslated( ukey.Original_Text_ExternallyTrimmed, scope, false ) )
                     {
                        AddTranslation( ukey.Original_Text_ExternallyTrimmed, uvalue.Original_Text_ExternallyTrimmed, scope );
                     }
                     if( ukey.Original_Text_ExternallyTrimmed != ukey.Original_Text_FullyTrimmed && !HasTranslated( ukey.Original_Text_FullyTrimmed, scope, false ) )
                     {
                        AddTranslation( ukey.Original_Text_FullyTrimmed, uvalue.Original_Text_FullyTrimmed, scope );
                     }
                  }
               }

               endTime = stopwatch.Elapsed.TotalSeconds;
               XuaLogger.AutoTranslator.Debug( $"Created variation translations (took {Math.Round( endTime - startTime, 2 )} seconds)" );
            }

            // generate token translations, which are online allowed when getting 'token' translations
            if( Settings.GeneratePartialTranslations )
            {
               startTime = stopwatch.Elapsed.TotalSeconds;

               foreach( var kvp in _translations.ToList() )
               {
                  CreatePartialTranslationsFor( kvp.Key, kvp.Value );
               }

               endTime = stopwatch.Elapsed.TotalSeconds;
               XuaLogger.AutoTranslator.Debug( $"Created partial translations (took {Math.Round( endTime - startTime, 2 )} seconds)" );
            }

            var parser = new RichTextParser();
            startTime = stopwatch.Elapsed.TotalSeconds;

            foreach( var kvp in _translations.ToList() )
            {
               if( kvp.Key.IndexOf( '<' ) < 0 && kvp.Value.IndexOf( '<' ) < 0 )
               {
                  continue;
               }

               var untranslatedResult = parser.Parse( kvp.Key, TranslationScopes.None );
               if( untranslatedResult != null )
               {
                  var translatedResult = parser.Parse( kvp.Value, TranslationScopes.None );
                  if( translatedResult != null && untranslatedResult.Arguments.Count == translatedResult.Arguments.Count )
                  {
                     foreach( var ukvp in untranslatedResult.Arguments )
                     {
                        var key = ukvp.Key;
                        var untranslatedToken = ukvp.Info.UntranslatedText;
                        var translatedToken = translatedResult.Arguments.FirstOrDefault( x => x.Key == key );
                        if( translatedToken != null )
                        {
                           AddTokenTranslation( untranslatedToken, translatedToken.Info.UntranslatedText, TranslationScopes.None );
                        }
                     }
                  }
               }
            }

            foreach( var scopedKvp in _scopedTranslations.ToList() )
            {
               var scope = scopedKvp.Key;

               foreach( var kvp in scopedKvp.Value.Translations.ToList() )
               {
                  if( kvp.Key.IndexOf( '<' ) < 0 && kvp.Value.IndexOf( '<' ) < 0 )
                  {
                     continue;
                  }

                  var untranslatedResult = parser.Parse( kvp.Key, scope );
                  if( untranslatedResult != null )
                  {
                     var translatedResult = parser.Parse( kvp.Value, scope );
                     if( translatedResult != null && untranslatedResult.Arguments.Count == translatedResult.Arguments.Count )
                     {
                        foreach( var ukvp in untranslatedResult.Arguments )
                        {
                           var key = ukvp.Key;
                           var untranslatedToken = ukvp.Info.UntranslatedText;
                           var translatedToken = translatedResult.Arguments.FirstOrDefault( x => x.Key == key );
                           if( translatedToken != null )
                           {
                              AddTokenTranslation( untranslatedToken, translatedToken.Info.UntranslatedText, scope );
                           }
                        }
                     }
                  }
               }
            }

            endTime = stopwatch.Elapsed.TotalSeconds;
            XuaLogger.AutoTranslator.Debug( $"Created token translations (took {Math.Round( endTime - startTime, 2 )} seconds)" );

            if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Debug( $"Translations generated: {_translations.Count}" );
            if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Debug( $"Regex translations generated: {_defaultRegexes.Count}" );
            if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Debug( $"Regex splitters generated: {_splitterRegexes.Count}" );
            if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Debug( $"Token translations generated: {_tokenTranslations.Count}" );
            if( Settings.GeneratePartialTranslations )
            {
               XuaLogger.AutoTranslator.Debug( $"Partial translations generated: {_partialTranslations.Count}" );
            }
            foreach( var kvp in _scopedTranslations.OrderBy( x => x.Key ) )
            {
               var scope = kvp.Key;
               var dicts = kvp.Value;

               if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Debug( $"Scene {scope} translations generated: {dicts.Translations.Count}" );
               if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Debug( $"Scene {scope} regex translations generated: {dicts.DefaultRegexes.Count}" );
               if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Debug( $"Scene {scope} regex splitter generated: {dicts.SplitterRegexes.Count}" );
               if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Debug( $"Scene {scope} token translations generated: {dicts.TokenTranslations.Count}" );
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while loading translations." );
         }
      }

      private List<KeyValuePair<string, bool>> GetTranslationFilesToLoad()
      {
         var result = new List<KeyValuePair<string, bool>>();

         var mainTranslationFile = new FileInfo( Settings.AutoTranslationsFilePath ).FullName;
         var substitutionFile = new FileInfo( Settings.SubstitutionFilePath ).FullName;
         var preprocessorsFile = new FileInfo( Settings.PreprocessorsFilePath ).FullName;
         var postprocessorsFile = new FileInfo( Settings.PostprocessorsFilePath ).FullName;
         var pluginsDir = Path.Combine( Settings.TranslationsPath, "plugins" ) + Path.DirectorySeparatorChar;

         if( _pluginDirectory == null )
         {
            result.Add( new KeyValuePair<string, bool>( mainTranslationFile, true ) );
         }

         foreach( var fullFileName in GetTranslationFiles().Except(
            new[] { mainTranslationFile, substitutionFile, preprocessorsFile, postprocessorsFile },
            StringComparer.OrdinalIgnoreCase ) )
         {
            if( _pluginDirectory == null && fullFileName.StartsWith( pluginsDir, StringComparison.OrdinalIgnoreCase ) )
            {
               continue;
            }

            result.Add( new KeyValuePair<string, bool>( fullFileName, false ) );
         }

         return result;
      }

      private TranslationFileSet GetOrReadFileSet( string path, bool isOutputFile, out int skippedLines )
      {
         skippedLines = 0;

         var fileInfo = new FileInfo( path );
         if( !fileInfo.Exists )
         {
            return new TranslationFileSet( path, isOutputFile, 0, DateTime.MinValue, new List<TranslationFileSection>() );
         }

         if( _fileSets.TryGetValue( path, out var cached )
            && cached.IsOutputFile == isOutputFile
            && cached.Matches( fileInfo ) )
         {
            return cached;
         }

         try
         {
            var result = TranslationFileReader.ReadFile(
               path,
               isOutputFile,
               entryName => !entryName.EndsWith( "resizer.txt", StringComparison.OrdinalIgnoreCase ) );

            _fileSets[ path ] = result.Set;
            skippedLines = result.SkippedLines;
            return result.Set;
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while reading translation file: " + path );

            if( cached != null && cached.IsOutputFile == isOutputFile )
            {
               return cached;
            }

            return new TranslationFileSet( path, isOutputFile, 0, DateTime.MinValue, new List<TranslationFileSection>() );
         }
      }

      private void RemoveStaleFileSets( List<KeyValuePair<string, bool>> filesToLoad )
      {
         var activeFiles = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
         foreach( var file in filesToLoad )
         {
            activeFiles.Add( file.Key );
         }

         List<string> staleFiles = null;
         foreach( var path in _fileSets.Keys )
         {
            if( !activeFiles.Contains( path ) )
            {
               if( staleFiles == null )
               {
                  staleFiles = new List<string>();
               }

               staleFiles.Add( path );
            }
         }

         if( staleFiles != null )
         {
            foreach( var path in staleFiles )
            {
               _fileSets.Remove( path );
            }
         }
      }

      private void PrepareTranslationState( int estimatedEntries )
      {
         if( estimatedEntries > _translationCapacity )
         {
            _translationCapacity = estimatedEntries;
            _translations = new Dictionary<string, string>( estimatedEntries );
            _reverseTranslations = new Dictionary<string, string>( estimatedEntries );
            _tokenTranslations = new Dictionary<string, string>( estimatedEntries );
            _reverseTokenTranslations = new Dictionary<string, string>( estimatedEntries );
         }
         else
         {
            _translations.Clear();
            _reverseTranslations.Clear();
            _tokenTranslations.Clear();
            _reverseTokenTranslations.Clear();
         }

         _partialTranslations.Clear();
         _registeredRegexes.Clear();
         _defaultRegexes.Clear();
         _registeredSplitterRegexes.Clear();
         _splitterRegexes.Clear();
         _scopedTranslations.Clear();
         _failedRegexLookups.Clear();
         _previousFailedRegexLookups.Clear();
      }

      private void MergeFileSet( TranslationFileSet set, bool isLoad )
      {
         foreach( var section in set.Sections )
         {
            var context = new TranslationFileLoadingContext();

            foreach( var line in section.Lines )
            {
               try
               {
                  if( line.Kind == TranslationFileLineKind.Directive )
                  {
                     context.Apply( line.Directive );

                     if( !Settings.EnableSilentMode && isLoad )
                     {
                        XuaLogger.AutoTranslator.Debug( "Directive in file: " + section.Name + ": " + line.Directive.ToString() );
                     }

                     continue;
                  }

                  if( !context.IsApplicable() )
                  {
                     continue;
                  }

                  var levels = context.GetLevels();
                  switch( line.Kind )
                  {
                     case TranslationFileLineKind.SplitterRegex:
                        var splitterRegex = new RegexTranslationSplitter( line.Key, line.Value );
                        if( levels.Count == 0 )
                        {
                           AddTranslationSplitterRegex( splitterRegex, TranslationScopes.None );
                        }
                        else
                        {
                           foreach( var level in levels )
                           {
                              AddTranslationSplitterRegex( splitterRegex, level );
                           }
                        }
                        break;

                     case TranslationFileLineKind.Regex:
                        var regex = new RegexTranslation( line.Key, line.Value );
                        if( levels.Count == 0 )
                        {
                           AddTranslationRegex( regex, TranslationScopes.None );
                        }
                        else
                        {
                           foreach( var level in levels )
                           {
                              AddTranslationRegex( regex, level );
                           }
                        }
                        break;

                     default:
                        if( levels.Count == 0 )
                        {
                           AddTranslation( line.Key, line.Value, TranslationScopes.None );
                        }
                        else
                        {
                           foreach( var level in levels )
                           {
                              AddTranslation( line.Key, line.Value, level );
                           }
                        }
                        break;
                  }
               }
               catch( Exception e )
               {
                  XuaLogger.AutoTranslator.Warn( e, $"An error occurred while reading translation in file '{section.Name}'." );
               }
            }

            if( isLoad )
            {
               AllowFallback = AllowFallback || context.IsEnabled( "fallback" );
            }
         }
      }

      private static bool HasFailedRegexLookup( HashSet<string> current, HashSet<string> previous, string key )
      {
         return current.Contains( key ) || ( previous != null && previous.Contains( key ) );
      }

      private void AddUnscopedFailedRegexLookup( string key )
      {
         if( _failedRegexLookups.Add( key ) && _failedRegexLookups.Count > 10000 )
         {
            // Keep one generation of old misses instead of discarding the whole cache.
            _previousFailedRegexLookups = _failedRegexLookups;
            _failedRegexLookups = new HashSet<string>();
         }
      }

      private static void AddScopedFailedRegexLookup( TranslationDictionaries dicts, string key )
      {
         if( dicts.FailedRegexLookups.Add( key ) && dicts.FailedRegexLookups.Count > 10000 )
         {
            dicts.PreviousFailedRegexLookups = dicts.FailedRegexLookups;
            dicts.FailedRegexLookups = new HashSet<string>();
         }
      }

      private static bool NeedsWhitespaceVariations( string text )
      {
         if( string.IsNullOrEmpty( text ) )
         {
            return false;
         }

         return char.IsWhiteSpace( text[ 0 ] )
            || char.IsWhiteSpace( text[ text.Length - 1 ] )
            || text.IndexOf( '\n' ) >= 0
            || text.IndexOf( '\r' ) >= 0;
      }

      private void AddWrapperVariation( string key, string value, int scope )
      {
         if( string.IsNullOrEmpty( key ) || string.IsNullOrEmpty( value ) )
         {
            return;
         }

         var keyUnwrapped = TranslationTextWrapper.TryGetUnwrappedCore( key, out var unwrappedKey );
         var valueUnwrapped = TranslationTextWrapper.TryGetUnwrappedCore( value, out var unwrappedValue );

         if( !keyUnwrapped && !valueUnwrapped )
         {
            return;
         }

         if( keyUnwrapped
            && !string.Equals( key, unwrappedKey, StringComparison.Ordinal )
            && !HasTranslated( unwrappedKey, scope, false ) )
         {
            AddTranslationAlias( unwrappedKey, valueUnwrapped ? unwrappedValue : value, scope );
         }

         if( valueUnwrapped && !string.Equals( value, unwrappedValue, StringComparison.Ordinal ) )
         {
            AddReverseTranslationAlias( unwrappedValue, keyUnwrapped ? unwrappedKey : key, scope );
         }
      }

      private void AddTranslationAlias( string key, string value, int scope )
      {
         if( scope != TranslationScopes.None )
         {
            if( !_scopedTranslations.TryGetValue( scope, out var dicts ) )
            {
               dicts = new TranslationDictionaries();
               _scopedTranslations.Add( scope, dicts );
            }

            dicts.Translations[ key ] = value;
            if( !dicts.ReverseTranslations.ContainsKey( value ) )
            {
               dicts.ReverseTranslations[ value ] = key;
            }
         }
         else
         {
            _translations[ key ] = value;
            if( !_reverseTranslations.ContainsKey( value ) )
            {
               _reverseTranslations[ value ] = key;
            }
         }
      }

      private void AddReverseTranslationAlias( string value, string key, int scope )
      {
         if( scope != TranslationScopes.None )
         {
            if( _scopedTranslations.TryGetValue( scope, out var dicts ) && !dicts.ReverseTranslations.ContainsKey( value ) )
            {
               dicts.ReverseTranslations[ value ] = key;
            }
         }
         else if( !_reverseTranslations.ContainsKey( value ) )
         {
            _reverseTranslations[ value ] = key;
         }
      }

      private void CreatePartialTranslationsFor( string originalText, string translatedText )
      {
         // determine how many tokens in both strings
         var originalTextTokens = Tokenify( originalText );
         var translatedTextTokens = Tokenify( translatedText );

         double rate = translatedTextTokens.Count / (double)originalTextTokens.Count;

         int translatedTextCursor = 0;
         var originalTextPartial = new StringBuilder( originalText.Length + 8 );
         var translatedTextPartial = new StringBuilder( translatedText.Length + 8 );

         for( int i = 0; i < originalTextTokens.Count; i++ )
         {
            var originalTextToken = originalTextTokens[ i ];
            if( originalTextToken.IsVariable )
            {
               originalTextPartial.Append( "{{" ).Append( originalTextToken.Character ).Append( "}}" );
            }
            else
            {
               originalTextPartial.Append( originalTextToken.Character );
            }

            var length = (int)( Math.Round( ( i + 1 ) * rate, 0, MidpointRounding.AwayFromZero ) );
            for( int j = translatedTextCursor; j < length; j++ )
            {
               var translatedTextToken = translatedTextTokens[ j ];
               if( translatedTextToken.IsVariable )
               {
                  translatedTextPartial.Append( "{{" ).Append( translatedTextToken.Character ).Append( "}}" );
               }
               else
               {
                  translatedTextPartial.Append( translatedTextToken.Character );
               }
            }

            translatedTextCursor = length;

            var originalTextPartialString = originalTextPartial.ToString();
            if( !HasTranslated( originalTextPartialString, TranslationScopes.None, false ) )
            {
               AddTranslation( originalTextPartialString, translatedTextPartial.ToString(), TranslationScopes.None );
               _partialTranslations.Add( originalTextPartialString );
            }
         }
      }

      private static List<TranslationCharacterToken> Tokenify( string text )
      {
         var result = new List<TranslationCharacterToken>( text.Length );

         for( int i = 0; i < text.Length; i++ )
         {
            var c = text[ i ];
            if( c == '{' && text.Length > i + 4 && text[ i + 1 ] == '{' && text[ i + 3 ] == '}' && text[ i + 4 ] == '}' )
            {
               c = text[ i + 2 ];
               result.Add( new TranslationCharacterToken( c, true ) );

               i += 4;
            }
            else
            {
               result.Add( new TranslationCharacterToken( c, false ) );
            }
         }

         return result;
      }

      private readonly struct TranslationCharacterToken
      {
         public TranslationCharacterToken( char c, bool isVariable )
         {
            Character = c;
            IsVariable = isVariable;
         }

         public char Character { get; }

         public bool IsVariable { get; }
      }

      private void LoadTranslationsInKeyValuePairs( IEnumerable<KeyValuePair<string, string>> pairs, string fullFileName )
      {
         if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Debug( $"Loading texts: {fullFileName}." );

         foreach( var kvp in pairs )
         {
            string key = kvp.Key;
            string value = kvp.Value;

            if( !string.IsNullOrEmpty( key ) && !string.IsNullOrEmpty( value ) )
            {
               if( key.StartsWith( "sr:" ) )
               {
                  try
                  {
                     var regex = new RegexTranslationSplitter( key, value );

                     AddTranslationSplitterRegex( regex, TranslationScopes.None );
                  }
                  catch( Exception e )
                  {
                     XuaLogger.AutoTranslator.Warn( e, $"An error occurred while constructing regex translation splitter: '{key}={value}'." );
                  }
               }
               else if( key.StartsWith( "r:" ) )
               {
                  try
                  {
                     var regex = new RegexTranslation( key, value );

                     AddTranslationRegex( regex, TranslationScopes.None );
                  }
                  catch( Exception e )
                  {
                     XuaLogger.AutoTranslator.Warn( e, $"An error occurred while constructing regex translation: '{key}={value}'." );
                  }
               }
               else
               {
                  AddTranslation( key, value, TranslationScopes.None );
               }
            }
         }
      }

      private void LoadStaticTranslations()
      {
         if( Settings.UseStaticTranslations && Settings.FromLanguage == Settings.DefaultFromLanguage && Settings.Language == Settings.DefaultLanguage )
         {
            // load static translations from previous titles
            using( var reader = new StringReader( Properties.Resources.StaticTranslations ) )
            {
               foreach( var translation in TranslationFileReader.ReadLines( reader ) )
               {
                  var kvp = TextHelper.ReadTranslationLineAndDecode( translation );
                  if( kvp != null )
                  {
                     string key = kvp[ 0 ];
                     string value = kvp[ 1 ];

                     if( !string.IsNullOrEmpty( key ) && !string.IsNullOrEmpty( value ) )
                     {
                        _staticTranslations[ key ] = value;
                     }
                  }
               }
            }
         }
      }

      internal void RegisterPackage( StreamTranslationPackage package )
      {
         _streamPackages.Add( package );
      }

      internal void RegisterPackage( KeyValuePairTranslationPackage package )
      {
         _kvpPackages.Add( package );
      }

      private void SaveNewTranslationsToDisk()
      {
         if( disposedValue )
         {
            return;
         }

         Dictionary<string, string> translations;
         lock( _writeToFileSync )
         {
            if( _newTranslations.Count == 0 )
            {
               return;
            }

            translations = new Dictionary<string, string>( _newTranslations );
            _newTranslations.Clear();
         }

         _fileWatcher?.Disable();
         try
         {
            if( _mainFileLength > 0
               && !TranslationWriter.FileMatches( Settings.AutoTranslationsFilePath, _mainFileLength, _mainFileLastWriteUtc ) )
            {
               XuaLogger.AutoTranslator.Warn( "The main translation file changed externally. Appending new translations without overwriting external edits." );
            }

            TranslationWriter.Append( Settings.AutoTranslationsFilePath, translations );
            TranslationWriter.SaveFileStamp( Settings.AutoTranslationsFilePath, out _mainFileLength, out _mainFileLastWriteUtc );
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while writing new translations to disk." );

            if( !disposedValue )
            {
               lock( _writeToFileSync )
               {
                  foreach( var kvp in translations )
                  {
                     if( !_newTranslations.ContainsKey( kvp.Key ) )
                     {
                        _newTranslations[ kvp.Key ] = kvp.Value;
                     }
                  }
               }
            }
         }
         finally
         {
            _fileWatcher?.Enable();
         }
      }

      private void AddTranslationSplitterRegex( RegexTranslationSplitter regex, int scope )
      {
         if( scope != TranslationScopes.None )
         {
            if( !_scopedTranslations.TryGetValue( scope, out var dicts ) )
            {
               dicts = new TranslationDictionaries();
               _scopedTranslations.Add( scope, dicts );
            }

            if( !dicts.RegisteredSplitterRegexes.Contains( regex.Original ) )
            {
               dicts.RegisteredSplitterRegexes.Add( regex.Original );
               dicts.SplitterRegexes.Add( regex );
            }
         }
         else
         {
            if( !_registeredSplitterRegexes.Contains( regex.Original ) )
            {
               _registeredSplitterRegexes.Add( regex.Original );
               _splitterRegexes.Add( regex );
            }
         }
      }

      private void AddTranslationRegex( RegexTranslation regex, int scope )
      {
         if( scope != TranslationScopes.None )
         {
            if( !_scopedTranslations.TryGetValue( scope, out var dicts ) )
            {
               dicts = new TranslationDictionaries();
               _scopedTranslations.Add( scope, dicts );
            }

            if( !dicts.RegisteredRegexes.Contains( regex.Original ) )
            {
               dicts.RegisteredRegexes.Add( regex.Original );
               dicts.DefaultRegexes.Add( regex );
            }
         }
         else
         {
            if( !_registeredRegexes.Contains( regex.Original ) )
            {
               _registeredRegexes.Add( regex.Original );
               _defaultRegexes.Add( regex );
            }
         }
      }

      private bool HasTranslated( string key, int scope, bool checkAll )
      {
         if( checkAll )
         {
            return _translations.ContainsKey( key )
               || ( scope != TranslationScopes.None && _scopedTranslations.TryGetValue( scope, out var td ) && td.Translations.ContainsKey( key ) );
         }
         else if( scope != TranslationScopes.None )
         {
            return scope != TranslationScopes.None && _scopedTranslations.TryGetValue( scope, out var td ) && td.Translations.ContainsKey( key );
         }
         else
         {
            return _translations.ContainsKey( key );
         }
      }

      private bool IsTranslation( string translation, int scope )
      {
         if( HasTranslated( translation, scope, true ) )
         {
            return false;
         }

         if( HasReverseTranslation( translation, scope ) )
         {
            return true;
         }

         if( TranslationTextWrapper.TryGetUnwrappedCore( translation, out var unwrappedTranslation ) )
         {
            if( HasTranslated( unwrappedTranslation, scope, true ) )
            {
               return false;
            }

            if( HasReverseTranslation( unwrappedTranslation, scope ) )
            {
               return true;
            }
         }

         return false;
      }

      private bool HasReverseTranslation( string translation, int scope )
      {
         return _reverseTranslations.ContainsKey( translation )
            || ( scope != TranslationScopes.None && _scopedTranslations.TryGetValue( scope, out var td ) && td.ReverseTranslations.ContainsKey( translation ) );
      }

      private bool IsTokenTranslation( string translation, int scope )
      {
         if( _reverseTokenTranslations.ContainsKey( translation ) )
         {
            return true;
         }
         else if( scope != TranslationScopes.None && _scopedTranslations.TryGetValue( scope, out var td ) && td.ReverseTokenTranslations.ContainsKey( translation ) )
         {
            return true;
         }

         if( TranslationTextWrapper.TryGetUnwrappedCore( translation, out var unwrappedTranslation ) )
         {
            if( _reverseTokenTranslations.ContainsKey( unwrappedTranslation ) )
            {
               return true;
            }
            else if( scope != TranslationScopes.None && _scopedTranslations.TryGetValue( scope, out var unwrappedTd ) && unwrappedTd.ReverseTokenTranslations.ContainsKey( unwrappedTranslation ) )
            {
               return true;
            }
         }

         return false;
      }

      private void AddTranslation( string key, string value, int scope )
      {
         if( key != null && value != null )
         {
            if( scope != TranslationScopes.None )
            {
               if( !_scopedTranslations.TryGetValue( scope, out var dicts ) )
               {
                  dicts = new TranslationDictionaries();
                  _scopedTranslations.Add( scope, dicts );
               }

               dicts.Translations[ key ] = value;
               dicts.ReverseTranslations[ value ] = key;
            }
            else
            {
               _translations[ key ] = value;
               _reverseTranslations[ value ] = key;
            }
         }
      }

      private void AddTokenTranslation( string key, string value, int scope )
      {
         if( key != null && value != null )
         {
            if( scope != TranslationScopes.None )
            {
               if( !_scopedTranslations.TryGetValue( scope, out var tokenDicts ) )
               {
                  tokenDicts = new TranslationDictionaries();
                  _scopedTranslations.Add( scope, tokenDicts );
               }

               tokenDicts.TokenTranslations[ key ] = value;
               tokenDicts.ReverseTokenTranslations[ value ] = key;
            }
            else
            {
               _tokenTranslations[ key ] = value;
               _reverseTokenTranslations[ value ] = key;
            }
         }
      }

      private void QueueNewTranslationForDisk( string key, string value )
      {
         if( disposedValue )
         {
            return;
         }

         lock( _writeToFileSync )
         {
            _newTranslations[ key ] = value;
         }
      }

      internal void AddTranslationToCache( string key, string value, bool persistToDisk, TranslationType type, int scope )
      {
         if( disposedValue )
         {
            return;
         }

         if( ( type & TranslationType.Token ) == TranslationType.Token && !persistToDisk )
         {
            AddTokenTranslation( key, value, scope );
         }

         if( ( type & TranslationType.Full ) == TranslationType.Full || persistToDisk )
         {
            if( !HasTranslated( key, scope, false ) )
            {
               AddTranslation( key, value, scope );

               // also add a modified version of the translation
               var ukey = new UntranslatedText( key, false, true, Settings.FromLanguageUsesWhitespaceBetweenWords, true, Settings.TemplateAllNumberAway );
               var uvalue = new UntranslatedText( value, false, true, Settings.ToLanguageUsesWhitespaceBetweenWords, true, Settings.TemplateAllNumberAway );
               if( ukey.Original_Text_ExternallyTrimmed != key && !HasTranslated( ukey.Original_Text_ExternallyTrimmed, scope, false ) )
               {
                  AddTranslation( ukey.Original_Text_ExternallyTrimmed, uvalue.Original_Text_ExternallyTrimmed, scope );
               }
               if( ukey.Original_Text_ExternallyTrimmed != ukey.Original_Text_FullyTrimmed && !HasTranslated( ukey.Original_Text_FullyTrimmed, scope, false ) )
               {
                  AddTranslation( ukey.Original_Text_FullyTrimmed, uvalue.Original_Text_FullyTrimmed, scope );
               }

               AddWrapperVariation( key, value, scope );

               if( persistToDisk )
               {
                  if( scope != TranslationScopes.None )
                  {
                     XuaLogger.AutoTranslator.Error( "Stored scoped translation to cache, even though this is not supported!" );
                  }

                  QueueNewTranslationForDisk( key, value );
               }
            }
         }
      }

      public bool TryGetTranslationSplitter( string text, int scope, out Match match, out RegexTranslationSplitter splitter )
      {
         if( scope != TranslationScopes.None && _scopedTranslations.TryGetValue( scope, out var dicts ) && dicts.SplitterRegexes.Count > 0 )
         {
            for( int i = dicts.SplitterRegexes.Count - 1; i > -1; i-- )
            {
               var regex = dicts.SplitterRegexes[ i ];
               try
               {
                  match = regex.CompiledRegex.Match( text );
                  splitter = regex;
                  if( match.Success )
                  {
                     return true;
                  }
               }
               catch( Exception e )
               {
                  dicts.SplitterRegexes.RemoveAt( i );

                  XuaLogger.AutoTranslator.Error( e, $"Failed while attempting to replace or match text of splitter regex '{regex.Original}'. Removing that regex from the cache." );
               }
            }
         }

         for( int i = _splitterRegexes.Count - 1; i > -1; i-- )
         {
            var regex = _splitterRegexes[ i ];
            try
            {
               match = regex.CompiledRegex.Match( text );
               splitter = regex;
               if( match.Success )
               {
                  return true;
               }
            }
            catch( Exception e )
            {
               _splitterRegexes.RemoveAt( i );

               XuaLogger.AutoTranslator.Error( e, $"Failed while attempting to replace or match text of splitter regex '{regex.Original}'. Removing that regex from the cache." );
            }
         }

         match = null;
         splitter = null;
         return false;
      }

      public bool TryGetTranslation( UntranslatedText key, bool allowRegex, bool allowToken, int scope, out string value )
      {
         if( TryGetTranslationCore( key, allowRegex, allowToken, scope, out value ) )
         {
            return true;
         }

         if( key == null || string.IsNullOrEmpty( key.Original_Text ) )
         {
            value = null;
            return false;
         }

         var currentText = key.IsTemplated ? key.TemplatedOriginal_Text : key.Original_Text;
         List<TranslationTextWrapper.Layer> layers = null;

         while( TranslationTextWrapper.TryUnwrapOneLayer( currentText, out var unwrappedText, out var layer ) )
         {
            if( layers == null )
            {
               layers = new List<TranslationTextWrapper.Layer>();
            }

            layers.Add( layer );
            currentText = unwrappedText;

            var unwrappedKey = new UntranslatedText(
               currentText,
               key.IsFromSpammingComponent,
               !key.IsFromSpammingComponent,
               Settings.FromLanguageUsesWhitespaceBetweenWords,
               false,
               Settings.TemplateAllNumberAway );

            if( TryGetTranslationCore( unwrappedKey, allowRegex, allowToken, scope, out var unwrappedValue ) && unwrappedValue != null )
            {
               value = TranslationTextWrapper.Wrap( layers, unwrappedValue );

               if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Info( $"Wrapper difference: '{key.Original_Text}' => '{value}'" );

               if( !allowToken )
               {
                  AddTranslationToCache( key.TemplatedOriginal_Text, value, false, TranslationType.Full, scope );
               }

               return true;
            }
         }

         value = null;
         return false;
      }

      private bool TryGetTranslationCore( UntranslatedText key, bool allowRegex, bool allowToken, int scope, out string value )
      {
         bool result;
         string untemplated;
         string unmodifiedValue;
         string unmodifiedKey;

         string untemplated_TemplatedOriginal_Text = null;
         string untemplated_TemplatedOriginal_Text_InternallyTrimmed = null;
         string untemplated_TemplatedOriginal_Text_ExternallyTrimmed = null;
         string untemplated_TemplatedOriginal_Text_FullyTrimmed = null;

         TranslationDictionaries dicts = null;
         if( scope != TranslationScopes.None && _scopedTranslations.TryGetValue( scope, out dicts ) )
         {
            // first lookup token, if we allow it! - ONLY ORIGINAL and INTERNALLY FIXED VARIATIONS
            if( allowToken && dicts.TokenTranslations.Count > 0 )
            {
               if( key.IsTemplated && !key.IsFromSpammingComponent )
               {
                  // key.TemplatedOriginal_Text = '   What are you \ndoing here, {{A}}?'
                  // untemplated                = '   What are you \ndoing here, Sophie?'

                  // FIRST: Check scoped translations
                  // lookup original
                  untemplated = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );
                  result = dicts.TokenTranslations.TryGetValue( untemplated, out value );
                  if( result )
                  {
                     return result;
                  }

                  // lookup internally trimmed
                  if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_InternallyTrimmed ) )
                  {
                     // key.TemplatedOriginal_Text_InternallyTrimmed = '   What are you doing here, {{A}}?'
                     // untemplated                                  = '   What are you doing here, Sophie?'

                     untemplated = untemplated_TemplatedOriginal_Text_InternallyTrimmed ?? ( untemplated_TemplatedOriginal_Text_InternallyTrimmed = key.Untemplate( key.TemplatedOriginal_Text_InternallyTrimmed ) );
                     result = dicts.TokenTranslations.TryGetValue( untemplated, out value );
                     if( result )
                     {
                        return result;
                     }
                  }
               }

               // lookup original
               // key.TemplatedOriginal_Text = '   What are you \ndoing here, {{A}}?'

               // FIRST: Check scoped translations
               // lookup original
               result = dicts.TokenTranslations.TryGetValue( key.TemplatedOriginal_Text, out value );
               if( result )
               {
                  return result;
               }

               // lookup internally trimmed
               if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_InternallyTrimmed ) )
               {
                  // key.TemplatedOriginal_Text_InternallyTrimmed = '   What are you doing here, {{A}}?'

                  result = dicts.TokenTranslations.TryGetValue( key.TemplatedOriginal_Text_InternallyTrimmed, out value );
                  if( result )
                  {
                     return result;
                  }
               }
            }

            // lookup UNTEMPLATED translations - ALL VARIATIONS
            if( key.IsTemplated && !key.IsFromSpammingComponent )
            {
               // key.TemplatedOriginal_Text = '   What are you \ndoing here, {{A}}?'
               // untemplated                = '   What are you \ndoing here, Sophie?'

               // FIRST: Check scoped translations
               if( dicts.Translations.Count > 0 )
               {
                  // lookup original
                  untemplated = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );
                  result = dicts.Translations.TryGetValue( untemplated, out value );
                  if( result )
                  {
                     return result;
                  }

                  // lookup original minus external whitespace
                  if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_ExternallyTrimmed ) )
                  {
                     // key.TemplatedOriginal_Text_ExternallyTrimmed = 'What are you \ndoing here, {{A}}?'
                     // untemplated                                  = 'What are you \ndoing here, Sophie?'

                     untemplated = untemplated_TemplatedOriginal_Text_ExternallyTrimmed ?? ( untemplated_TemplatedOriginal_Text_ExternallyTrimmed = key.Untemplate( key.TemplatedOriginal_Text_ExternallyTrimmed ) );
                     result = dicts.Translations.TryGetValue( untemplated, out value );
                     if( result )
                     {
                        // WHITESPACE DIFFERENCE, Store new value
                        unmodifiedValue = key.LeadingWhitespace + value + key.TrailingWhitespace;
                        unmodifiedKey = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );

                        if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Info( $"Whitespace difference (c1): '{unmodifiedKey}' => '{unmodifiedValue}'" );
                        AddTranslationToCache( unmodifiedKey, unmodifiedValue, false, TranslationType.Full, scope );

                        value = unmodifiedValue;
                        return result;
                     }
                  }

                  // lookup internally trimmed
                  if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_InternallyTrimmed ) )
                  {
                     // key.TemplatedOriginal_Text_InternallyTrimmed = '   What are you doing here, {{A}}?'
                     // untemplated                                  = '   What are you doing here, Sophie?'

                     untemplated = untemplated_TemplatedOriginal_Text_InternallyTrimmed ?? ( untemplated_TemplatedOriginal_Text_InternallyTrimmed = key.Untemplate( key.TemplatedOriginal_Text_InternallyTrimmed ) );
                     result = dicts.Translations.TryGetValue( untemplated, out value );
                     if( result )
                     {
                        // WHITESPACE DIFFERENCE, Store new value
                        unmodifiedValue = value;
                        unmodifiedKey = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );

                        if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Info( $"Whitespace difference (c2): '{unmodifiedKey}' => '{unmodifiedValue}'" );
                        AddTranslationToCache( unmodifiedKey, unmodifiedValue, false, TranslationType.Full, scope );

                        value = unmodifiedValue;
                        return result;
                     }
                  }

                  // lookup internally trimmed minus external whitespace
                  if( !ReferenceEquals( key.TemplatedOriginal_Text_InternallyTrimmed, key.TemplatedOriginal_Text_FullyTrimmed ) )
                  {
                     // key.TemplatedOriginal_Text_FullyTrimmed = 'What are you doing here, {{A}}?'
                     // untemplated                             = 'What are you doing here, Sophie?'

                     untemplated = untemplated_TemplatedOriginal_Text_FullyTrimmed ?? ( untemplated_TemplatedOriginal_Text_FullyTrimmed = key.Untemplate( key.TemplatedOriginal_Text_FullyTrimmed ) );
                     result = dicts.Translations.TryGetValue( untemplated, out value );
                     if( result )
                     {
                        // WHITESPACE DIFFERENCE, Store new value
                        unmodifiedValue = key.LeadingWhitespace + value + key.TrailingWhitespace;
                        unmodifiedKey = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );

                        if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Info( $"Whitespace difference (c3): '{unmodifiedKey}' => '{unmodifiedValue}'" );
                        AddTranslationToCache( unmodifiedKey, unmodifiedValue, false, TranslationType.Full, scope );

                        value = unmodifiedValue;
                        return result;
                     }
                  }
               }
            }

            // lookup original - ALL VARATIONS
            // FIRST: Check scoped translations
            if( dicts.Translations.Count > 0 )
            {
               // key.TemplatedOriginal_Text = '   What are you \ndoing here, {{A}}?'
               result = dicts.Translations.TryGetValue( key.TemplatedOriginal_Text, out value );
               if( result )
               {
                  return result;
               }

               // lookup original minus external whitespace
               if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_ExternallyTrimmed ) )
               {
                  // key.TemplatedOriginal_Text_ExternallyTrimmed = 'What are you \ndoing here, {{A}}?'

                  result = dicts.Translations.TryGetValue( key.TemplatedOriginal_Text_ExternallyTrimmed, out value );
                  if( result )
                  {
                     // WHITESPACE DIFFERENCE, Store new value
                     unmodifiedValue = key.LeadingWhitespace + value + key.TrailingWhitespace;

                     if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Info( $"Whitespace difference (c7): '{key.TemplatedOriginal_Text}' => '{unmodifiedValue}'" );
                     AddTranslationToCache( key.TemplatedOriginal_Text, unmodifiedValue, false, TranslationType.Full, scope );

                     value = unmodifiedValue;
                     return result;
                  }
               }

               // lookup internally trimmed
               if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_InternallyTrimmed ) )
               {
                  // key.TemplatedOriginal_Text_InternallyTrimmed = '   What are you doing here, {{A}}?'

                  result = dicts.Translations.TryGetValue( key.TemplatedOriginal_Text_InternallyTrimmed, out value );
                  if( result )
                  {
                     // WHITESPACE DIFFERENCE, Store new value
                     if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Info( $"Whitespace difference (c8): '{key.TemplatedOriginal_Text}' => '{value}'" );
                     AddTranslationToCache( key.TemplatedOriginal_Text, value, false, TranslationType.Full, scope ); // FIXED: using templated original

                     return result;
                  }
               }

               // lookup internally trimmed minus external whitespace
               if( !ReferenceEquals( key.TemplatedOriginal_Text_InternallyTrimmed, key.TemplatedOriginal_Text_FullyTrimmed ) )
               {
                  // key.TemplatedOriginal_Text_FullyTrimmed = 'What are you doing here, {{A}}?'

                  result = dicts.Translations.TryGetValue( key.TemplatedOriginal_Text_FullyTrimmed, out value );
                  if( result )
                  {
                     // WHITESPACE DIFFERENCE, Store new value
                     unmodifiedValue = key.LeadingWhitespace + value + key.TrailingWhitespace;

                     if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Info( $"Whitespace difference (c9): '{key.TemplatedOriginal_Text}' => '{unmodifiedValue}'" );
                     AddTranslationToCache( key.TemplatedOriginal_Text, unmodifiedValue, false, TranslationType.Full, scope );

                     value = unmodifiedValue;
                     return result;
                  }
               }
            }
         }








         // first lookup token, if we allow it! - ONLY ORIGINAL and INTERNALLY FIXED VARIATIONS
         if( allowToken && _tokenTranslations.Count > 0 )
         {
            if( key.IsTemplated && !key.IsFromSpammingComponent )
            {
               // key.TemplatedOriginal_Text = '   What are you \ndoing here, {{A}}?'
               // untemplated                = '   What are you \ndoing here, Sophie?'

               // THEN: Check unscoped translations
               // lookup original
               untemplated = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );
               result = _tokenTranslations.TryGetValue( untemplated, out value );
               if( result )
               {
                  return result;
               }

               // lookup internally trimmed
               if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_InternallyTrimmed ) )
               {
                  // key.TemplatedOriginal_Text_InternallyTrimmed = '   What are you doing here, {{A}}?'
                  // untemplated                                  = '   What are you doing here, Sophie?'

                  untemplated = untemplated_TemplatedOriginal_Text_InternallyTrimmed ?? ( untemplated_TemplatedOriginal_Text_InternallyTrimmed = key.Untemplate( key.TemplatedOriginal_Text_InternallyTrimmed ) );
                  result = _tokenTranslations.TryGetValue( untemplated, out value );
                  if( result )
                  {
                     return result;
                  }
               }
            }

            // lookup original
            // key.TemplatedOriginal_Text = '   What are you \ndoing here, {{A}}?'

            // THEN: Check unscoped translations
            // lookup original
            result = _tokenTranslations.TryGetValue( key.TemplatedOriginal_Text, out value );
            if( result )
            {
               return result;
            }

            // lookup internally trimmed
            if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_InternallyTrimmed ) )
            {
               // key.TemplatedOriginal_Text_InternallyTrimmed = '   What are you doing here, {{A}}?'

               result = _tokenTranslations.TryGetValue( key.TemplatedOriginal_Text_InternallyTrimmed, out value );
               if( result )
               {
                  return result;
               }
            }
         }

         // lookup UNTEMPLATED translations - ALL VARIATIONS
         if( key.IsTemplated && !key.IsFromSpammingComponent )
         {
            // key.TemplatedOriginal_Text = '   What are you \ndoing here, {{A}}?'
            // untemplated                = '   What are you \ndoing here, Sophie?'

            // THEN: Check unscoped translations
            // lookup original
            untemplated = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );
            result = _translations.TryGetValue( untemplated, out value );
            if( result )
            {
               return result;
            }

            // lookup original minus external whitespace
            if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_ExternallyTrimmed ) )
            {
               // key.TemplatedOriginal_Text_ExternallyTrimmed = 'What are you \ndoing here, {{A}}?'
               // untemplated                                  = 'What are you \ndoing here, Sophie?'

               untemplated = untemplated_TemplatedOriginal_Text_ExternallyTrimmed ?? ( untemplated_TemplatedOriginal_Text_ExternallyTrimmed = key.Untemplate( key.TemplatedOriginal_Text_ExternallyTrimmed ) );
               result = _translations.TryGetValue( untemplated, out value );
               if( result )
               {
                  // WHITESPACE DIFFERENCE, Store new value
                  unmodifiedValue = key.LeadingWhitespace + value + key.TrailingWhitespace;
                  unmodifiedKey = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );

                  if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Info( $"Whitespace difference (c4): '{unmodifiedKey}' => '{unmodifiedValue}'" );
                  AddTranslationToCache( unmodifiedKey, unmodifiedValue, Settings.CacheWhitespaceDifferences, TranslationType.Full, TranslationScopes.None );

                  value = unmodifiedValue;
                  return result;
               }
            }

            // lookup internally trimmed
            if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_InternallyTrimmed ) )
            {
               // key.TemplatedOriginal_Text_InternallyTrimmed = '   What are you doing here, {{A}}?'
               // untemplated                                  = '   What are you doing here, Sophie?'

               untemplated = untemplated_TemplatedOriginal_Text_InternallyTrimmed ?? ( untemplated_TemplatedOriginal_Text_InternallyTrimmed = key.Untemplate( key.TemplatedOriginal_Text_InternallyTrimmed ) );
               result = _translations.TryGetValue( untemplated, out value );
               if( result )
               {
                  // WHITESPACE DIFFERENCE, Store new value
                  unmodifiedValue = value;
                  unmodifiedKey = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );

                  if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Info( $"Whitespace difference (c5): '{unmodifiedKey}' => '{unmodifiedValue}'" );
                  AddTranslationToCache( unmodifiedKey, unmodifiedValue, Settings.CacheWhitespaceDifferences, TranslationType.Full, TranslationScopes.None );

                  value = unmodifiedValue;
                  return result;
               }
            }

            // lookup internally trimmed minus external whitespace
            if( !ReferenceEquals( key.TemplatedOriginal_Text_InternallyTrimmed, key.TemplatedOriginal_Text_FullyTrimmed ) )
            {
               // key.TemplatedOriginal_Text_FullyTrimmed = 'What are you doing here, {{A}}?'
               // untemplated                             = 'What are you doing here, Sophie?'

               untemplated = untemplated_TemplatedOriginal_Text_FullyTrimmed ?? ( untemplated_TemplatedOriginal_Text_FullyTrimmed = key.Untemplate( key.TemplatedOriginal_Text_FullyTrimmed ) );
               result = _translations.TryGetValue( untemplated, out value );
               if( result )
               {
                  // WHITESPACE DIFFERENCE, Store new value
                  unmodifiedValue = key.LeadingWhitespace + value + key.TrailingWhitespace;
                  unmodifiedKey = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );

                  if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Info( $"Whitespace difference (c6): '{unmodifiedKey}' => '{unmodifiedValue}'" );
                  AddTranslationToCache( unmodifiedKey, unmodifiedValue, Settings.CacheWhitespaceDifferences, TranslationType.Full, TranslationScopes.None );

                  value = unmodifiedValue;
                  return result;
               }
            }
         }

         // THEN: Check unscoped translations
         // key.TemplatedOriginal_Text = '   What are you \ndoing here, {{A}}?'
         result = _translations.TryGetValue( key.TemplatedOriginal_Text, out value );
         if( result )
         {
            return result;
         }

         // lookup original minus external whitespace
         if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_ExternallyTrimmed ) )
         {
            // key.TemplatedOriginal_Text_ExternallyTrimmed = 'What are you \ndoing here, {{A}}?'

            result = _translations.TryGetValue( key.TemplatedOriginal_Text_ExternallyTrimmed, out value );
            if( result )
            {
               // WHITESPACE DIFFERENCE, Store new value
               unmodifiedValue = key.LeadingWhitespace + value + key.TrailingWhitespace;

               if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Info( $"Whitespace difference (c10): '{key.TemplatedOriginal_Text}' => '{unmodifiedValue}'" );
               AddTranslationToCache( key.TemplatedOriginal_Text, unmodifiedValue, Settings.CacheWhitespaceDifferences, TranslationType.Full, TranslationScopes.None );

               value = unmodifiedValue;
               return result;
            }
         }

         // lookup internally trimmed
         if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_InternallyTrimmed ) )
         {
            // key.TemplatedOriginal_Text_InternallyTrimmed = '   What are you doing here, {{A}}?'

            result = _translations.TryGetValue( key.TemplatedOriginal_Text_InternallyTrimmed, out value );
            if( result )
            {
               // WHITESPACE DIFFERENCE, Store new value
               if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Info( $"Whitespace difference (c11): '{key.TemplatedOriginal_Text}' => '{value}'" );
               AddTranslationToCache( key.TemplatedOriginal_Text, value, Settings.CacheWhitespaceDifferences, TranslationType.Full, TranslationScopes.None ); // FIXED: using templated original

               return result;
            }
         }

         // lookup internally trimmed minus external whitespace
         if( !ReferenceEquals( key.TemplatedOriginal_Text_InternallyTrimmed, key.TemplatedOriginal_Text_FullyTrimmed ) )
         {
            // key.TemplatedOriginal_Text_FullyTrimmed = 'What are you doing here, {{A}}?'

            result = _translations.TryGetValue( key.TemplatedOriginal_Text_FullyTrimmed, out value );
            if( result )
            {
               // WHITESPACE DIFFERENCE, Store new value
               unmodifiedValue = key.LeadingWhitespace + value + key.TrailingWhitespace;

               if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Info( $"Whitespace difference (c12): '{key.TemplatedOriginal_Text}' => '{unmodifiedValue}'" );
               AddTranslationToCache( key.TemplatedOriginal_Text, unmodifiedValue, Settings.CacheWhitespaceDifferences, TranslationType.Full, TranslationScopes.None );

               value = unmodifiedValue;
               return result;
            }
         }

         // regex lookups - ONLY ORIGNAL VARIATION
         if( allowRegex )
         {
            if( dicts != null && dicts.DefaultRegexes.Count > 0
               && !HasFailedRegexLookup( dicts.FailedRegexLookups, dicts.PreviousFailedRegexLookups, key.TemplatedOriginal_Text ) )
            {
               for( int i = dicts.DefaultRegexes.Count - 1; i > -1; i-- )
               {
                  var regex = dicts.DefaultRegexes[ i ];
                  try
                  {
                     var match = regex.CompiledRegex.Match( key.TemplatedOriginal_Text );
                     if( !match.Success ) continue;

                     value = match.Result( regex.Translation );
                     value = RomanizationHelper.PostProcess( value, Settings.RegexPostProcessing );

                     if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Info( $"Regex lookup: '{key.TemplatedOriginal_Text}' => '{value}'" );
                     AddTranslationToCache( key.TemplatedOriginal_Text, value, false, TranslationType.Full, scope );

                     return true;
                  }
                  catch( Exception e )
                  {
                     dicts.DefaultRegexes.RemoveAt( i );

                     XuaLogger.AutoTranslator.Error( e, $"Failed while attempting to replace or match text of regex '{regex.Original}'. Removing that regex from the cache." );
                  }
               }

               AddScopedFailedRegexLookup( dicts, key.TemplatedOriginal_Text );
            }

            if( !HasFailedRegexLookup( _failedRegexLookups, _previousFailedRegexLookups, key.TemplatedOriginal_Text ) )
            {
               for( int i = _defaultRegexes.Count - 1; i > -1; i-- )
               {
                  var regex = _defaultRegexes[ i ];
                  try
                  {
                     var match = regex.CompiledRegex.Match( key.TemplatedOriginal_Text );
                     if( !match.Success ) continue;

                     value = match.Result( regex.Translation );
                     value = RomanizationHelper.PostProcess( value, Settings.RegexPostProcessing );

                     if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Info( $"Regex lookup: '{key.TemplatedOriginal_Text}' => '{value}'" );
                     AddTranslationToCache( key.TemplatedOriginal_Text, value, Settings.CacheRegexLookups, TranslationType.Full, TranslationScopes.None );

                     return true;
                  }
                  catch( Exception e )
                  {
                     _defaultRegexes.RemoveAt( i );

                     XuaLogger.AutoTranslator.Error( e, $"Failed while attempting to replace or match text of regex '{regex.Original}'. Removing that regex from the cache." );
                  }
               }

               AddUnscopedFailedRegexLookup( key.TemplatedOriginal_Text );
            }
         }

         // static lookups - ALL VARIATIONS
         if( _staticTranslations.Count > 0 )
         {
            // lookup based on templated value alone, which may also be the original text
            // key.TemplatedOriginal_Text = '   What are you \ndoing here, {{A}}?'

            // lookup original
            result = _staticTranslations.TryGetValue( key.TemplatedOriginal_Text, out value );
            if( result )
            {
               if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Info( $"Static lookup: '{key.TemplatedOriginal_Text}' => '{value}'" );
               AddTranslationToCache( key.TemplatedOriginal_Text, value, true, TranslationType.Full, TranslationScopes.None );

               return result;
            }

            // lookup internally trimmed
            if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_InternallyTrimmed ) )
            {
               // key.TemplatedOriginal_Text_InternallyTrimmed = '   What are you doing here, {{A}}?'

               result = _staticTranslations.TryGetValue( key.TemplatedOriginal_Text_InternallyTrimmed, out value );
               if( result )
               {
                  if( !Settings.EnableSilentMode ) XuaLogger.AutoTranslator.Info( $"Static lookup: '{key.TemplatedOriginal_Text_InternallyTrimmed}' => '{value}'" );
                  AddTranslationToCache( key.TemplatedOriginal_Text, value, true, TranslationType.Full, TranslationScopes.None );

                  return result;
               }
            }
         }

         return result;
      }

      internal bool TryGetReverseTranslation( string value, int scope, out string key )
      {
         if( TryGetReverseTranslationCore( value, scope, out key ) )
         {
            return true;
         }

         if( TranslationTextWrapper.TryGetUnwrappedCore( value, out var unwrappedValue ) )
         {
            return TryGetReverseTranslationCore( unwrappedValue, scope, out key );
         }

         key = null;
         return false;
      }

      private bool TryGetReverseTranslationCore( string value, int scope, out string key )
      {
         return ( scope != TranslationScopes.None && _scopedTranslations.TryGetValue( scope, out var td ) && td.ReverseTranslations.TryGetValue( value, out key ) ) ||
            _reverseTranslations.TryGetValue( value, out key );
      }

      public bool IsTranslatable( string text, bool isToken, int scope )
      {
         var translatable = !IsTranslation( text, scope );
         if( isToken && translatable )
         {
            translatable = !IsTokenTranslation( text, scope );
         }
         return translatable;
      }

      public bool IsPartial( string text, int scope )
      {
         return _partialTranslations.Contains( text );
      }

      public class TranslationDictionaries
      {
         public TranslationDictionaries()
         {
            Translations = new Dictionary<string, string>();
            ReverseTranslations = new Dictionary<string, string>();
            DefaultRegexes = new List<RegexTranslation>();
            RegisteredRegexes = new HashSet<string>();
            TokenTranslations = new Dictionary<string, string>();
            ReverseTokenTranslations = new Dictionary<string, string>();
            SplitterRegexes = new List<RegexTranslationSplitter>();
            RegisteredSplitterRegexes = new HashSet<string>();
            FailedRegexLookups = new HashSet<string>();
            PreviousFailedRegexLookups = new HashSet<string>();
         }

         public Dictionary<string, string> TokenTranslations { get; }
         public Dictionary<string, string> ReverseTokenTranslations { get; }
         public Dictionary<string, string> Translations { get; }
         public Dictionary<string, string> ReverseTranslations { get; }
         public List<RegexTranslation> DefaultRegexes { get; }
         public HashSet<string> RegisteredRegexes { get; }
         public List<RegexTranslationSplitter> SplitterRegexes { get; }
         public HashSet<string> RegisteredSplitterRegexes { get; }
         public HashSet<string> FailedRegexLookups { get; set; }
         public HashSet<string> PreviousFailedRegexLookups { get; set; }
      }

      private void Dispose( bool disposing )
      {
         if( !disposedValue )
         {
            if( disposing )
            {
               if( _fileWatcher != null )
               {
                  _fileWatcher.DirectoryUpdated -= FileWatcher_DirectoryUpdated;
                  _fileWatcher.Dispose();
                  _fileWatcher = null;
               }

               if( _maintenanceAction != null )
               {
                  MaintenanceHelper.RemoveMaintenanceFunction( _maintenanceAction );
               }

               // Flush anything that is still pending before the cache stops accepting writes.
               SaveNewTranslationsToDisk();

               TextTranslationFileChanged = null;
               disposedValue = true;

               lock( _writeToFileSync )
               {
                  _newTranslations.Clear();
               }

               _translations.Clear();
               _reverseTranslations.Clear();
               _tokenTranslations.Clear();
               _reverseTokenTranslations.Clear();
               _staticTranslations.Clear();
               _partialTranslations.Clear();
               _defaultRegexes.Clear();
               _registeredRegexes.Clear();
               _splitterRegexes.Clear();
               _registeredSplitterRegexes.Clear();
               _failedRegexLookups.Clear();
               _previousFailedRegexLookups.Clear();
               _scopedTranslations.Clear();
               _fileSets.Clear();
               _compositeCaches.Clear();
            }
            else
            {
               disposedValue = true;
            }
         }
      }

      public void Dispose()
      {
         // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
         Dispose( disposing: true );
         GC.SuppressFinalize( this );
      }
   }
}
