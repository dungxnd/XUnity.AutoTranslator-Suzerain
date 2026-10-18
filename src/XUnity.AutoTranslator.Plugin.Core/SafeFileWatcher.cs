using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core
{
   internal sealed class SafeFileWatcher : IDisposable
   {
      public event Action DirectoryUpdated;

      private readonly string _directory;
      private readonly string[] _extensions;
      private readonly int _debounceMilliseconds;

      private int _counter;
      private int _suppressEventsUntil;
      private FileSystemWatcher _watcher;
      private volatile bool _disposed;
      private readonly object _sync = new object();
      private readonly Timer _timer;
      private string _pendingFilePath;

      public SafeFileWatcher( string directory, params string[] extensions )
         : this( directory, 500, extensions )
      {
      }

      public SafeFileWatcher( string directory, int debounceMilliseconds, params string[] extensions )
      {
         _directory = directory;
         _extensions = extensions;
         _debounceMilliseconds = debounceMilliseconds;
         _timer = new Timer( RaiseEvent, null, Timeout.Infinite, Timeout.Infinite );

         EnableWatcher();
      }

      public void EnableWatcher()
      {
         lock( _sync )
         {
            if( _disposed || _watcher != null )
            {
               return;
            }

            var watcher = new FileSystemWatcher( _directory )
            {
               IncludeSubdirectories = true,
               NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
               InternalBufferSize = 64 * 1024
            };

            watcher.Changed += Watcher_Changed;
            watcher.Created += Watcher_Created;
            watcher.Deleted += Watcher_Deleted;
            watcher.Renamed += Watcher_Renamed;
            watcher.Error += Watcher_Error;
            watcher.EnableRaisingEvents = true;

            _watcher = watcher;
         }
      }

      public void DisableWatcher()
      {
         lock( _sync )
         {
            if( _watcher != null )
            {
               _watcher.EnableRaisingEvents = false;
               _watcher.Changed -= Watcher_Changed;
               _watcher.Created -= Watcher_Created;
               _watcher.Deleted -= Watcher_Deleted;
               _watcher.Renamed -= Watcher_Renamed;
               _watcher.Error -= Watcher_Error;
               _watcher.Dispose();
               _watcher = null;
            }
         }
      }

      public void RaiseEvent( object state )
      {
         if( _disposed )
         {
            return;
         }

         var pendingFilePath = _pendingFilePath;
         _pendingFilePath = null;
         if( pendingFilePath != null )
         {
            WaitForFile( pendingFilePath, 2000 );
         }

         try
         {
            DirectoryUpdated?.Invoke();
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Error( e, "An error occurred while handling a file watcher event." );
         }
      }

      public void Disable()
      {
         Interlocked.Increment( ref _counter );
         _suppressEventsUntil = Environment.TickCount + 1000;

         if( _disposed )
         {
            return;
         }

         lock( _sync )
         {
            if( _watcher != null )
            {
               _watcher.EnableRaisingEvents = false;
            }
         }
      }

      public void Enable()
      {
         var counter = Interlocked.Decrement( ref _counter );
         if( counter < 0 )
         {
            Interlocked.Exchange( ref _counter, 0 );
            counter = 0;
         }

         if( counter == 0 )
         {
            // Ignore events queued by the plugin's own write.
            _suppressEventsUntil = Environment.TickCount + 1000;
            EnableWatcher();
         }
      }

      private bool AreEventsSuppressed()
      {
         if( Thread.VolatileRead( ref _counter ) > 0 )
         {
            return true;
         }

         return unchecked( _suppressEventsUntil - Environment.TickCount ) > 0;
      }

      private void Watcher_Deleted( object sender, FileSystemEventArgs e )
      {
         QueueEvent( null );
      }

      private void Watcher_Created( object sender, FileSystemEventArgs e )
      {
         QueueEvent( e.FullPath );
      }

      private void Watcher_Changed( object sender, FileSystemEventArgs e )
      {
         QueueEvent( e.FullPath );
      }

      private void Watcher_Renamed( object sender, RenamedEventArgs e )
      {
         QueueEvent( e.FullPath );
      }

      private void Watcher_Error( object sender, ErrorEventArgs e )
      {
         if( _disposed )
         {
            return;
         }

         XuaLogger.AutoTranslator.Error( e.GetException(), "An error occurred in the translation file watcher. Recreating the watcher." );

         try
         {
            DisableWatcher();
            EnableWatcher();
         }
         catch( Exception ex )
         {
            XuaLogger.AutoTranslator.Error( ex, "An error occurred while recreating the translation file watcher." );
         }
      }

      private void QueueEvent( string fullPath )
      {
         if( _disposed || AreEventsSuppressed() || !IsWatchedPath( fullPath ) )
         {
            return;
         }

         if( fullPath != null )
         {
            _pendingFilePath = fullPath;
         }

         try
         {
            _timer.Change( _debounceMilliseconds, Timeout.Infinite );
         }
         catch( ObjectDisposedException )
         {
            // Disposed between the check and the timer update.
         }
      }

      private bool IsWatchedPath( string fullPath )
      {
         if( fullPath == null || _extensions == null || _extensions.Length == 0 )
         {
            return true;
         }

         for( int i = 0; i < _extensions.Length; i++ )
         {
            if( fullPath.EndsWith( _extensions[ i ], StringComparison.OrdinalIgnoreCase ) )
            {
               return true;
            }
         }

         return false;
      }

      private static void WaitForFile( string path, int timeoutMilliseconds )
      {
         if( !File.Exists( path ) )
         {
            return;
         }

         var stopwatch = Stopwatch.StartNew();
         while( IsFileLocked( path ) )
         {
            if( stopwatch.ElapsedMilliseconds >= timeoutMilliseconds )
            {
               XuaLogger.AutoTranslator.Warn( "Timed out waiting for translation file to become available: " + path );
               return;
            }

            Thread.Sleep( 100 );
         }
      }

      private static bool IsFileLocked( string path )
      {
         try
         {
            using( File.Open( path, FileMode.Open, FileAccess.ReadWrite, FileShare.None ) )
            {
            }
         }
         catch( FileNotFoundException )
         {
            return false;
         }
         catch( DirectoryNotFoundException )
         {
            return false;
         }
         catch( IOException )
         {
            return true;
         }
         catch( UnauthorizedAccessException )
         {
            return true;
         }

         return false;
      }

      public void Dispose()
      {
         if( _disposed )
         {
            return;
         }

         _disposed = true;
         DirectoryUpdated = null;

         DisableWatcher();
         _timer.Dispose();
      }
   }
}
