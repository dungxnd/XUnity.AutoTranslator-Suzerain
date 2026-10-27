using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.Common.Constants;
using XUnity.Common.Extensions;
using XUnity.Common.Logging;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Fonts
{
   internal static class FontCache
   {
      private static readonly Dictionary<int, Font> CachedFonts = new Dictionary<int, Font>();
      private static bool _hasReadOverrideFontTextMeshPro = false;
      private static UnityEngine.Object OverrideFontTextMeshPro;
      private static bool _hasReadFallbackFontTextMeshPro = false;
      private static UnityEngine.Object FallbackFontTextMeshPro;
      private static readonly HashSet<char> _prewarmedTmpChars = new HashSet<char>();

#if IL2CPP
      private static bool ShouldRetryWithCustomProxies( Exception e )
      {
         while( ( e is System.Reflection.TargetInvocationException || e is TypeInitializationException ) && e.InnerException != null )
         {
            e = e.InnerException;
         }

         if( e is NotSupportedException || e is MissingMethodException )
         {
            return true;
         }

         var message = e.ToString();
         return message.IndexOf( "missing", StringComparison.OrdinalIgnoreCase ) >= 0
            || message.IndexOf( "not found", StringComparison.OrdinalIgnoreCase ) >= 0;
      }
#endif

      public static Font GetOrCreate( int size )
      {
         if( !CachedFonts.TryGetValue( size, out Font font ) )
         {
            font = FontHelper.GetTextFont( size );
            CachedFonts.Add( size, font );
         }
         return font;
      }

      public static object GetOrCreateOverrideFontTextMeshPro()
      {
         if( !_hasReadOverrideFontTextMeshPro )
         {
            try
            {
               _hasReadOverrideFontTextMeshPro = true;
               OverrideFontTextMeshPro = FontHelper.GetTextMeshProFont( Settings.OverrideFontTextMeshPro );
            }
#if IL2CPP
            catch( Exception e ) when( ShouldRetryWithCustomProxies( e ) )
            {
               XuaLogger.AutoTranslator.Warn( e, "An error occurred while loading text mesh pro override font. Retrying load with custom proxies..." );

               try
               {
                  OverrideFontTextMeshPro = FontHelper.GetTextMeshProFontByCustomProxies( Settings.OverrideFontTextMeshPro );
               }
               catch( Exception ex )
               {
                  XuaLogger.AutoTranslator.Error( ex, "An error occurred while loading text mesh pro override font: " + Settings.OverrideFontTextMeshPro );
               }
            }
#endif
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Error( e, "An error occurred while loading text mesh pro override font: " + Settings.OverrideFontTextMeshPro );
            }
         }

         return OverrideFontTextMeshPro;
      }

      public static UnityEngine.Object GetOrCreateFallbackFontTextMeshPro()
      {
         if( !_hasReadFallbackFontTextMeshPro )
         {
            try
            {
               _hasReadFallbackFontTextMeshPro = true;
               FallbackFontTextMeshPro = FontHelper.GetTextMeshProFont( Settings.FallbackFontTextMeshPro );
            }
#if IL2CPP
            catch( Exception e ) when( ShouldRetryWithCustomProxies( e ) )
            {
               XuaLogger.AutoTranslator.Warn( e, "An error occurred while loading text mesh pro fallback font. Retrying load with custom proxies..." );

               try
               {
                  FallbackFontTextMeshPro = FontHelper.GetTextMeshProFontByCustomProxies( Settings.FallbackFontTextMeshPro );
               }
               catch( Exception ex )
               {
                  XuaLogger.AutoTranslator.Error( ex, "An error occurred while loading text mesh pro fallback font: " + Settings.FallbackFontTextMeshPro );
               }
            }
#endif
            catch( Exception e )
            {
               XuaLogger.AutoTranslator.Error( e, "An error occurred while loading text mesh pro fallback font: " + Settings.FallbackFontTextMeshPro );
            }
         }

         return FallbackFontTextMeshPro;
      }

      private static readonly MethodInfo TmpTryAddCharacters = FindTmpTryAddCharacters();

      private static MethodInfo FindTmpTryAddCharacters()
      {
         var type = UnityTypes.TMP_FontAsset?.ClrType;
         if( type == null ) return null;

         return type
            .GetMethods( BindingFlags.Instance | BindingFlags.Public )
            .Where( x => x.Name == "TryAddCharacters" )
            .Where( x => x.GetParameters().Length >= 2 )
            .Where( x => x.GetParameters()[ 0 ].ParameterType == typeof( string ) )
            .Where( x => x.GetParameters()[ 1 ].ParameterType.IsByRef )
            .FirstOrDefault();
      }

      public static void PrewarmTranslation( object ui, string text )
      {
         if( ui == null || string.IsNullOrEmpty( text ) ) return;

         try
         {
            var type = ui.GetUnityType();

            if( UnityTypes.Text != null && UnityTypes.Text.IsAssignableFrom( type ) )
            {
               var font = UnityTypes.Text_Properties.Font?.Get( ui ) as Font;
               if( font != null )
               {
                  var sizeValue = UnityTypes.Text_Properties.FontSize?.Get( ui );
                  var size = sizeValue != null ? (int)sizeValue : font.fontSize;
                  if( size > 0 )
                  {
                     var requestCharacters = font.GetType().GetMethod( "RequestCharactersInTexture", new[] { typeof( string ), typeof( int ) } );
                     requestCharacters?.Invoke( font, new object[] { text, size } );
                  }
               }
            }
            else if( UnityTypes.TMP_Text != null && UnityTypes.TMP_Text.IsAssignableFrom( type ) )
            {
               bool hasNewChars = false;
               for( int i = 0 ; i < text.Length ; i++ )
               {
                  if( !_prewarmedTmpChars.Contains( text[ i ] ) )
                  {
                     hasNewChars = true;
                     break;
                  }
               }

               if( !hasNewChars ) return;

               var font = ui.GetType().CachedProperty( "font" )?.Get( ui );
               if( font != null && TmpTryAddCharacters != null )
               {
                  var parameters = TmpTryAddCharacters.GetParameters();
                  var arguments = new object[ parameters.Length ];
                  arguments[ 0 ] = text;
                  for( int i = 1 ; i < arguments.Length ; i++ )
                  {
                     arguments[ i ] = parameters[ i ].ParameterType == typeof( bool ) ? (object)false : null;
                  }

                  TmpTryAddCharacters.Invoke( font, arguments );

                  for( int i = 0 ; i < text.Length ; i++ )
                  {
                     _prewarmedTmpChars.Add( text[ i ] );
                  }

                  var missing = arguments.Length > 1 ? arguments[ 1 ] as string : null;
                  if( !string.IsNullOrEmpty( missing ) )
                  {
                     XuaLogger.AutoTranslator.Debug( $"TextMesh Pro font is missing characters after prewarming: {missing}" );
                  }
               }
            }
         }
         catch( Exception e )
         {
            XuaLogger.AutoTranslator.Debug( e, "An error occurred while prewarming translated text glyphs." );
         }
      }
   }
}
