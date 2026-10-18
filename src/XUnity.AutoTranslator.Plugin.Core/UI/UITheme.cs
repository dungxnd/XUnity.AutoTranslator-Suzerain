using System;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Fonts;

namespace XUnity.AutoTranslator.Plugin.Core.UI
{
   internal static class UITheme
   {
      public static readonly Color BackgroundColor = new Color( 0.055f, 0.067f, 0.086f, 1f );
      public static readonly Color SurfaceColor = new Color( 0.086f, 0.106f, 0.133f, 1f );
      public static readonly Color SurfaceRaisedColor = new Color( 0.122f, 0.149f, 0.188f, 1f );
      public static readonly Color BorderColor = new Color( 0.176f, 0.216f, 0.282f, 1f );
      public static readonly Color TextColor = new Color( 0.902f, 0.929f, 0.953f, 1f );
      public static readonly Color MutedTextColor = new Color( 0.545f, 0.596f, 0.647f, 1f );
      public static readonly Color AccentColor = new Color( 0.298f, 0.761f, 1f, 1f );
      public static readonly Color AccentStrongColor = new Color( 0.165f, 0.616f, 0.957f, 1f );
      public static readonly Color SuccessColor = new Color( 0.247f, 0.725f, 0.314f, 1f );
      public static readonly Color WarningColor = new Color( 0.824f, 0.600f, 0.133f, 1f );
      public static readonly Color DangerColor = new Color( 0.973f, 0.318f, 0.286f, 1f );

      public static readonly Font UIFont;

      public static readonly GUIStyle WindowStyle;
      public static readonly GUIStyle PanelStyle;
      public static readonly GUIStyle NoSpacingBoxStyle;
      public static readonly GUIStyle TextAreaStyle;
      public static readonly GUIStyle ButtonStyle;
      public static readonly GUIStyle ButtonPressedStyle;
      public static readonly GUIStyle SmallButtonStyle;
      public static readonly GUIStyle ToggleStyle;
      public static readonly GUIStyle HeaderStyle;
      public static readonly GUIStyle LabelStyle;
      public static readonly GUIStyle LabelRight;
      public static readonly GUIStyle LabelCenter;
      public static readonly GUIStyle LabelTranslation;
      public static readonly GUIStyle LabelRich;
      public static readonly GUIStyle TooltipStyle;
      public static readonly GUIStyle ToastStyle;
      public static readonly GUIStyle HorizontalSliderStyle;
      public static readonly GUIStyle HorizontalSliderThumbStyle;

      private static readonly RectOffset Empty = new RectOffset( 0, 0, 0, 0 );
      private static readonly Texture2D WindowBackground;
      private static readonly Texture2D PanelBackground;
      private static readonly Texture2D ButtonNormal;
      private static readonly Texture2D ButtonHover;
      private static readonly Texture2D ButtonActive;
      private static readonly Texture2D ButtonPressed;
      private static readonly Texture2D TrackBackground;
      private static readonly Texture2D ThumbNormal;
      private static readonly Texture2D ThumbHover;
      private static readonly Texture2D ToastBackground;

      static UITheme()
      {
         UIFont = LoadUIFont();

         WindowBackground = CreateBoxTexture( SurfaceColor, BorderColor, AccentColor );
         PanelBackground = CreateBoxTexture( SurfaceColor, BorderColor );
         ButtonNormal = CreateBoxTexture( SurfaceRaisedColor, BorderColor );
         ButtonHover = CreateBoxTexture( new Color( 0.149f, 0.184f, 0.231f, 1f ), AccentColor );
         ButtonActive = CreateBoxTexture( AccentStrongColor, AccentColor );
         ButtonPressed = CreateBoxTexture( AccentColor, AccentStrongColor );
         TrackBackground = CreateSolidTexture( SurfaceRaisedColor );
         ThumbNormal = CreateSolidTexture( AccentColor );
         ThumbHover = CreateSolidTexture( AccentStrongColor );
         ToastBackground = CreateBoxTexture( SurfaceColor, SuccessColor );

         WindowStyle = CreateStyle( GUI.skin.window, style =>
         {
            style.normal.background = WindowBackground;
            style.hover.background = WindowBackground;
            style.active.background = WindowBackground;
            style.focused.background = WindowBackground;
            style.onNormal.background = WindowBackground;
            style.onHover.background = WindowBackground;
            style.onActive.background = WindowBackground;
            style.normal.textColor = TextColor;
            style.onNormal.textColor = TextColor;
            style.hover.textColor = TextColor;
            style.active.textColor = TextColor;
            style.focused.textColor = TextColor;
            style.border = new RectOffset( 1, 1, 1, 1 );
            style.alignment = TextAnchor.MiddleLeft;
            ApplyFont( style, 14, FontStyle.Bold );
         } );

         PanelStyle = CreateStyle( GUI.skin.box, style =>
         {
            style.normal.background = PanelBackground;
            style.border = new RectOffset( 1, 1, 1, 1 );
            style.padding = new RectOffset( 8, 8, 8, 8 );
            style.margin = Empty;
            style.alignment = TextAnchor.UpperLeft;
            ApplyFont( style, 13, FontStyle.Normal );
         } );

         NoSpacingBoxStyle = CreateStyle( PanelStyle, style =>
         {
            style.padding = Empty;
            style.margin = Empty;
         } );

         TextAreaStyle = CreateStyle( GUI.skin.box, style =>
         {
            style.normal.background = PanelBackground;
            style.border = new RectOffset( 1, 1, 1, 1 );
            style.padding = new RectOffset( 6, 6, 6, 6 );
            style.margin = Empty;
            ApplyFont( style, 13, FontStyle.Normal );
         } );

         ButtonStyle = CreateStyle( GUI.skin.button, style =>
         {
            style.normal.background = ButtonNormal;
            style.hover.background = ButtonHover;
            style.active.background = ButtonActive;
            style.focused.background = ButtonHover;
            style.onNormal.background = ButtonActive;
            style.onHover.background = ButtonActive;
            style.onActive.background = ButtonPressed;
            style.normal.textColor = TextColor;
            style.hover.textColor = TextColor;
            style.active.textColor = TextColor;
            style.focused.textColor = TextColor;
            style.onNormal.textColor = TextColor;
            style.onHover.textColor = TextColor;
            style.onActive.textColor = BackgroundColor;
            style.border = new RectOffset( 1, 1, 1, 1 );
            style.padding = new RectOffset( 8, 8, 4, 4 );
            style.margin = Empty;
            style.alignment = TextAnchor.MiddleCenter;
            style.wordWrap = false;
            style.clipping = TextClipping.Clip;
            ApplyFont( style, 13, FontStyle.Normal );
         } );

         ButtonPressedStyle = CreateStyle( ButtonStyle, style =>
         {
            style.normal.background = ButtonActive;
            style.hover.background = ButtonActive;
            style.active.background = ButtonPressed;
            style.focused.background = ButtonActive;
            style.onNormal.background = ButtonActive;
            style.onHover.background = ButtonActive;
            style.onActive.background = ButtonPressed;
            style.normal.textColor = TextColor;
            style.hover.textColor = TextColor;
            style.active.textColor = TextColor;
            style.onNormal.textColor = TextColor;
            style.onHover.textColor = TextColor;
            style.onActive.textColor = BackgroundColor;
         } );

         SmallButtonStyle = CreateStyle( ButtonStyle, style =>
         {
            style.padding = new RectOffset( 6, 6, 2, 2 );
            ApplyFont( style, 12, FontStyle.Normal );
         } );

         ToggleStyle = CreateStyle( GUI.skin.toggle, style =>
         {
            style.normal.textColor = TextColor;
            style.hover.textColor = TextColor;
            style.active.textColor = TextColor;
            style.focused.textColor = TextColor;
            style.onNormal.textColor = TextColor;
            style.onHover.textColor = TextColor;
            style.onActive.textColor = TextColor;
            ApplyFont( style, 13, FontStyle.Normal );
         } );

         HeaderStyle = CreateStyle( GUI.skin.label, style =>
         {
            style.normal.textColor = AccentColor;
            style.alignment = TextAnchor.MiddleLeft;
            style.richText = false;
            style.wordWrap = false;
            style.clipping = TextClipping.Clip;
            style.padding = Empty;
            style.margin = Empty;
            ApplyFont( style, 12, FontStyle.Bold );
         } );

         LabelStyle = CreateStyle( GUI.skin.label, style =>
         {
            style.normal.textColor = TextColor;
            style.alignment = TextAnchor.MiddleLeft;
            style.richText = false;
            style.wordWrap = false;
            style.clipping = TextClipping.Clip;
            style.margin = Empty;
            ApplyFont( style, 13, FontStyle.Normal );
         } );

         LabelRight = CreateStyle( LabelStyle, style =>
         {
            style.alignment = TextAnchor.MiddleRight;
         } );

         LabelCenter = CreateStyle( LabelStyle, style =>
         {
            style.alignment = TextAnchor.MiddleCenter;
         } );

         LabelTranslation = CreateStyle( GUI.skin.label, style =>
         {
            style.normal.textColor = TextColor;
            style.alignment = TextAnchor.UpperLeft;
            style.richText = false;
            style.wordWrap = true;
            style.margin = Empty;
            style.padding = new RectOffset( 0, 0, 2, 3 );
            ApplyFont( style, 13, FontStyle.Normal );
         } );

         LabelRich = CreateStyle( GUI.skin.label, style =>
         {
            style.normal.textColor = TextColor;
            style.alignment = TextAnchor.UpperLeft;
            style.richText = true;
            style.wordWrap = true;
            style.margin = Empty;
            ApplyFont( style, 13, FontStyle.Normal );
         } );

         TooltipStyle = CreateStyle( GUI.skin.box, style =>
         {
            style.normal.background = PanelBackground;
            style.normal.textColor = MutedTextColor;
            style.border = new RectOffset( 1, 1, 1, 1 );
            style.padding = new RectOffset( 8, 8, 8, 8 );
            style.margin = Empty;
            style.alignment = TextAnchor.UpperLeft;
            style.wordWrap = true;
            style.richText = true;
            ApplyFont( style, 12, FontStyle.Normal );
         } );

         ToastStyle = CreateStyle( GUI.skin.box, style =>
         {
            style.normal.background = ToastBackground;
            style.normal.textColor = SuccessColor;
            style.border = new RectOffset( 1, 1, 1, 1 );
            style.padding = new RectOffset( 6, 6, 2, 2 );
            style.margin = Empty;
            style.alignment = TextAnchor.MiddleCenter;
            style.clipping = TextClipping.Clip;
            ApplyFont( style, 12, FontStyle.Normal );
         } );

         HorizontalSliderStyle = CreateStyle( GUI.skin.horizontalSlider, style =>
         {
            style.normal.background = TrackBackground;
            style.hover.background = TrackBackground;
            style.active.background = TrackBackground;
            style.border = new RectOffset( 0, 0, 0, 0 );
         } );

         HorizontalSliderThumbStyle = CreateStyle( GUI.skin.horizontalSliderThumb, style =>
         {
            style.normal.background = ThumbNormal;
            style.hover.background = ThumbHover;
            style.active.background = ThumbHover;
            style.border = new RectOffset( 0, 0, 0, 0 );
         } );
      }

      private static Font LoadUIFont()
      {
         try
         {
            return FontHelper.GetUIFont( 14 );
         }
         catch
         {
            return null;
         }
      }

      private static void ApplyFont( GUIStyle style, int size, FontStyle fontStyle )
      {
         if( UIFont != null )
         {
            style.font = UIFont;
         }
         style.fontSize = size;
         style.fontStyle = fontStyle;
      }

      private static GUIStyle CreateStyle( GUIStyle source, Action<GUIStyle> configure )
      {
         if( source == null )
         {
            source = new GUIStyle();
         }

         try
         {
            return CopyStyle( source, configure );
         }
         catch
         {
            return source;
         }
      }

      private static GUIStyle CopyStyle( GUIStyle other, Action<GUIStyle> setProperties )
      {
#if IL2CPP
         return CopyWithPropertiesSafe( other, setProperties );
#else
         var style = new GUIStyle( other );
         setProperties( style );
         return style;
#endif
      }

#if IL2CPP
      private static bool _hasCtor = true;

      private static GUIStyle CopyWithPropertiesSafe( GUIStyle other, Action<GUIStyle> setProperties )
      {
         if( _hasCtor )
         {
            try
            {
               return CopyWithPropertiesUnsafe( other, setProperties );
            }
            catch
            {
               _hasCtor = false;
            }
         }
         return CopyWithPropertiesSpecial( other, setProperties );
      }

      private static GUIStyle CopyWithPropertiesSpecial( GUIStyle other, Action<GUIStyle> setProperties )
      {
         var style = new GUIStyle();
         style.m_Ptr = GUIStyle.Internal_Copy( style, other );
         setProperties( style );
         return style;
      }

      private static GUIStyle CopyWithPropertiesUnsafe( GUIStyle other, Action<GUIStyle> setProperties )
      {
         var style = new GUIStyle( other );
         setProperties( style );
         return style;
      }
#endif

      private static Texture2D CreateSolidTexture( Color color )
      {
         var texture = new Texture2D( 1, 1, TextureFormat.ARGB32, false );
         texture.SetPixel( 0, 0, color );
         texture.Apply();
         GameObject.DontDestroyOnLoad( texture );
         return texture;
      }

      private static Texture2D CreateBoxTexture( Color fill, Color border, Color? topBorder = null )
      {
         var top = topBorder ?? border;
         var texture = new Texture2D( 3, 3, TextureFormat.ARGB32, false );
         for( int y = 0; y < 3; y++ )
         {
            for( int x = 0; x < 3; x++ )
            {
               var isEdge = x == 0 || x == 2 || y == 0 || y == 2;
               var color = isEdge ? ( y == 2 ? top : border ) : fill;
               texture.SetPixel( x, y, color );
            }
         }
         texture.Apply();
         GameObject.DontDestroyOnLoad( texture );
         return texture;
      }
   }
}
