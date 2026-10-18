using System;
using System.Globalization;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.Common.Constants;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.UI
{
   internal static class GUIUtil
   {
      public const float WindowTitleClearance = 10;
      public const float ComponentSpacing = 10;
      public const float HalfComponentSpacing = ComponentSpacing / 2;
      public const float LabelWidth = 60;
      public const float LabelHeight = 21;
      public const float RowHeight = 21;

      public static GUIContent none = new GUIContent( "" );

      public static float UIScale { get; set; } = 1f;

      public static GUIStyle LabelTranslation => UITheme.LabelTranslation;
      public static GUIStyle LabelCenter => UITheme.LabelCenter;
      public static GUIStyle LabelRight => UITheme.LabelRight;
      public static GUIStyle LabelRich => UITheme.LabelRich;
      public static GUIStyle NoMarginButtonStyle => UITheme.ButtonStyle;
      public static GUIStyle NoMarginButtonPressedStyle => UITheme.ButtonPressedStyle;
      public static GUIStyle NoSpacingBoxStyle => UITheme.NoSpacingBoxStyle;

      public static Rect R( float x, float y, float width, float height ) => new Rect( x, y, width, height );

      public static Vector2 ScreenToGuiPoint( Vector2 screenPoint )
      {
         return new Vector2( screenPoint.x / UIScale, ( Screen.height - screenPoint.y ) / UIScale );
      }

      public static Rect ClampToScreen( Rect rect )
      {
         var screenWidth = Mathf.Max( 1f, Screen.width / UIScale );
         var screenHeight = Mathf.Max( 1f, Screen.height / UIScale );

         rect.width = Mathf.Min( rect.width, screenWidth );
         rect.height = Mathf.Min( rect.height, screenHeight );
         rect.x = Mathf.Clamp( rect.x, 0f, Mathf.Max( 0f, screenWidth - rect.width ) );
         rect.y = Mathf.Clamp( rect.y, 0f, Mathf.Max( 0f, screenHeight - rect.height ) );

         return rect;
      }

      public static float ResolveUIScale()
      {
         float scale;
         var configured = Settings.UIScale;
         if( string.IsNullOrEmpty( configured ) || string.Equals( configured, "Auto", StringComparison.OrdinalIgnoreCase ) )
         {
            scale = Screen.height / 1080f;
         }
         else if( !float.TryParse( configured, NumberStyles.Float, CultureInfo.InvariantCulture, out scale ) )
         {
            scale = Screen.height / 1080f;
         }

         scale = Mathf.Clamp( scale, 0.8f, 2.5f );

         // Keep the main window inside the screen even when a larger scale is configured.
         var maxByHeight = Screen.height / 660f;
         if( maxByHeight > 0f )
         {
            scale = Mathf.Min( scale, Mathf.Max( 0.8f, maxByHeight ) );
         }

         return scale;
      }

      public static GUIContent CreateContent( string text )
      {
         return new GUIContent( text );
      }

      public static GUIContent CreateContent( string text, string tooltip )
      {
#if IL2CPP
         return new GUIContent( text, null, tooltip );
#else
         return new GUIContent( text, tooltip );
#endif
      }

      public static bool IsAnyMouseButtonOrScrollWheelDownSafe
      {
         get
         {
            return UnityFeatures.SupportsMouseScrollDelta
               ? IsAnyMouseButtonOrScrollWheelDown
               : IsAnyMouseButtonOrScrollWheelDownLegacy;
         }
      }

      public static bool IsAnyMouseButtonOrScrollWheelDownLegacy
      {
         get
         {
            return UnityInput.Current.GetMouseButtonDown( 0 )
               || UnityInput.Current.GetMouseButtonDown( 1 )
               || UnityInput.Current.GetMouseButtonDown( 2 );
         }
      }

      public static bool IsAnyMouseButtonOrScrollWheelDown
      {
         get
         {
            return UnityInput.Current.mouseScrollDelta.y != 0f
               || UnityInput.Current.GetMouseButtonDown( 0 )
               || UnityInput.Current.GetMouseButtonDown( 1 )
               || UnityInput.Current.GetMouseButtonDown( 2 );
         }
      }

      public static bool IsAnyMouseButtonOrScrollWheelSafe
      {
         get
         {
            return UnityFeatures.SupportsMouseScrollDelta
               ? IsAnyMouseButtonOrScrollWheel
               : IsAnyMouseButtonOrScrollWheelLegacy;
         }
      }

      public static bool IsAnyMouseButtonOrScrollWheelLegacy
      {
         get
         {
            return UnityInput.Current.GetMouseButton( 0 )
               || UnityInput.Current.GetMouseButton( 1 )
               || UnityInput.Current.GetMouseButton( 2 );
         }
      }

      public static bool IsAnyMouseButtonOrScrollWheel
      {
         get
         {
            return UnityInput.Current.mouseScrollDelta.y != 0f
               || UnityInput.Current.GetMouseButton( 0 )
               || UnityInput.Current.GetMouseButton( 1 )
               || UnityInput.Current.GetMouseButton( 2 );
         }
      }
   }
}
