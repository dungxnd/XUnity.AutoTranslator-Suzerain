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
      public const float ScrollbarWidth = 7;

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

      public static float GetScrollbarWidth( float viewportHeight, float contentHeight )
      {
         return contentHeight > viewportHeight + 0.5f ? ScrollbarWidth + 2f : 0f;
      }

      /// <summary>
      /// Opens a vertical-only scroll view with a slim custom scrollbar. The
      /// horizontal axis is intentionally never available, so content cannot
      /// produce an X-axis scrollbar. Nested groups are used instead of
      /// GUI.BeginScrollView so no default Unity scrollbar chrome is drawn.
      /// </summary>
      public static Vector2 BeginVerticalScrollView( Rect viewport, Vector2 scrollPosition, float contentHeight, out float contentWidth )
      {
         var scrollbarWidth = GetScrollbarWidth( viewport.height, contentHeight );
         contentWidth = Mathf.Max( 1f, viewport.width - scrollbarWidth );

         var maxScroll = Mathf.Max( 0f, contentHeight - viewport.height );
         if( maxScroll > 0f
            && Event.current != null
            && Event.current.type == EventType.ScrollWheel
            && viewport.Contains( Event.current.mousePosition ) )
         {
            scrollPosition.y += Event.current.delta.y * ( RowHeight * 1.5f );
            Event.current.Use();
         }

         scrollPosition.x = 0f;
         scrollPosition.y = Mathf.Clamp( scrollPosition.y, 0f, maxScroll );

         GUI.BeginGroup( viewport );
         try
         {
            GUI.BeginGroup( new Rect( 0f, -scrollPosition.y, contentWidth, contentHeight ) );
         }
         catch
         {
            GUI.EndGroup();
            throw;
         }

         return scrollPosition;
      }

      public static void EndVerticalScrollView( Rect viewport, Vector2 scrollPosition, float contentHeight )
      {
         GUI.EndGroup();
         GUI.EndGroup();

         var scrollbarWidth = GetScrollbarWidth( viewport.height, contentHeight );
         if( scrollbarWidth <= 0f )
         {
            return;
         }

         var trackRect = R( viewport.x + viewport.width - ScrollbarWidth, viewport.y, ScrollbarWidth, viewport.height );
         GUI.DrawTexture( trackRect, UITheme.ScrollbarTrack );

         var thumbHeight = Mathf.Clamp( viewport.height * ( viewport.height / contentHeight ), 20f, viewport.height );
         var maxScroll = Mathf.Max( 0f, contentHeight - viewport.height );
         var progress = maxScroll > 0f ? Mathf.Clamp01( scrollPosition.y / maxScroll ) : 0f;
         var thumbY = viewport.y + progress * ( viewport.height - thumbHeight );

         GUI.DrawTexture(
            R( trackRect.x + 1f, thumbY + 1f, trackRect.width - 2f, Mathf.Max( 8f, thumbHeight - 2f ) ),
            UITheme.ScrollbarThumb );
      }

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
