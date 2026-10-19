using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.UI
{
   internal class TranslationAggregatorWindow
   {
      private static string[] Empty = new string[ 0 ];

      private const int WindowId = 2387602;

      private Rect _windowRect;
      private bool _isMouseDownOnWindow = false;
      private TranslationAggregatorViewModel _viewModel;
      private float _panelHeight;
      private float _copiedUntil;
      private string _copiedText;

      private ScrollPositioned _originalText;
      private ScrollPositioned _defaultTranslation;
      private ScrollPositioned<TranslatorViewModel>[] _translationViews;

      public TranslationAggregatorWindow( TranslationAggregatorViewModel viewModel )
      {
         _viewModel = viewModel;

         _windowRect = new Rect( 360, 20, _viewModel.Width, 300 );

         _originalText = new ScrollPositioned();
         _defaultTranslation = new ScrollPositioned();
         _translationViews = viewModel.AvailableTranslators.Select( x => new ScrollPositioned<TranslatorViewModel>( x ) ).ToArray();
      }

      public bool IsShown
      {
         get => _viewModel.IsShown;
         set => _viewModel.IsShown = value;
      }

      public void OnGUI()
      {
         var screenWidth = Screen.width / GUIUtil.UIScale;
         var screenHeight = Screen.height / GUIUtil.UIScale;
         var current = _viewModel.Current;
         var panelCount = current != null
            ? current.AggregatedTranslations.Count( x => x.Translator.IsEnabled ) + 2
            : _viewModel.AvailableTranslators.Count( x => x.IsEnabled ) + 2;
         var preferredPanelHeight = Mathf.Clamp( _viewModel.Height, 50f, 300f );
         var maxPanelHeight = Mathf.Max( 60f, ( screenHeight - 90f ) / panelCount );
         _panelHeight = Mathf.Min( preferredPanelHeight, maxPanelHeight );

         var windowHeight = ( panelCount * _panelHeight ) + 34 + GUIUtil.LabelHeight + GUIUtil.ComponentSpacing;

         _windowRect.height = Mathf.Max( 1f, Mathf.Min( windowHeight, screenHeight ) );
         _windowRect.width = Mathf.Max( 1f, Mathf.Min( _viewModel.Width, screenWidth ) );
         _windowRect = GUIUtil.ClampToScreen( _windowRect );

         _windowRect = GUI.Window( WindowId, _windowRect, (GUI.WindowFunction)CreateWindowUI, "Translation Aggregator", UITheme.WindowStyle );

         if( GUIUtil.IsAnyMouseButtonOrScrollWheelDownSafe )
         {
            var point = GUIUtil.ScreenToGuiPoint( new Vector2( UnityInput.Current.mousePosition.x, UnityInput.Current.mousePosition.y ) );
            _isMouseDownOnWindow = _windowRect.Contains( point );
         }

         if( !_isMouseDownOnWindow || !GUIUtil.IsAnyMouseButtonOrScrollWheelSafe )
            return;

         // make sure window is focused if scroll wheel is used to indicate we consumed that event
         GUI.BringWindowToFront( WindowId );
         GUI.FocusWindow( WindowId );

         var point1 = GUIUtil.ScreenToGuiPoint( new Vector2( UnityInput.Current.mousePosition.x, UnityInput.Current.mousePosition.y ) );
         if( !_windowRect.Contains( point1 ) )
            return;

         UnityInput.Current.ResetInputAxes();
         if( Event.current.isMouse )
         {
            Event.current.Use();
         }
      }

      public void Update()
      {
         _viewModel.Update();
      }

      public void OnNewTranslationAdded( string originalText, string defaultTranslation )
      {
         _viewModel.OnNewTranslationAdded( originalText, defaultTranslation );
      }

      private void CreateWindowUI( int id )
      {
         try
         {
            AutoTranslationPlugin.Current.DisableAutoTranslator();

            float posy = GUIUtil.WindowTitleClearance + GUIUtil.ComponentSpacing;

            if( GUI.Button( GUIUtil.R( _viewModel.Width - 28, 2, 22, 18 ), "X", UITheme.SmallButtonStyle ) )
            {
               IsShown = false;
            }

            if( Time.realtimeSinceStartup < _copiedUntil && !string.IsNullOrEmpty( _copiedText ) )
            {
               GUI.Label( GUIUtil.R( GUIUtil.HalfComponentSpacing, 2, 160, 18 ), _copiedText, UITheme.ToastStyle );
            }

            var current = _viewModel.Current;
            if( current != null )
            {
               if( GUI.Button( GUIUtil.R( _viewModel.Width - GUIUtil.HalfComponentSpacing - 50, posy + 5 + 1, 50, GUIUtil.LabelHeight ), "Copy", UITheme.SmallButtonStyle ) )
               {
                  current.CopyOriginalTextToClipboard();
                  ShowCopied( "Copied original" );
               }
               DrawTextArea( posy, _originalText, "ORIGINAL TEXT", current.OriginalTexts, _panelHeight );
               posy += _panelHeight;

               if( GUI.Button( GUIUtil.R( _viewModel.Width - GUIUtil.HalfComponentSpacing - 50, posy + 5 + 1, 50, GUIUtil.LabelHeight ), "Copy", UITheme.SmallButtonStyle ) )
               {
                  current.CopyDefaultTranslationToClipboard();
                  ShowCopied( "Copied translation" );
               }
               DrawTextArea( posy, _defaultTranslation, "DEFAULT TRANSLATION", current.DefaultTranslations, _panelHeight );
               posy += _panelHeight;

               for( int i = 0; i < current.AggregatedTranslations.Count; i++ )
               {
                  var aggregatedTranslation = current.AggregatedTranslations[ i ];
                  if( aggregatedTranslation.Translator.IsEnabled )
                  {
                     var scroller = GetTranslationView( aggregatedTranslation.Translator );

                     GUI.enabled = aggregatedTranslation.CanCopyToClipboard();
                     if( GUI.Button( GUIUtil.R( _viewModel.Width - GUIUtil.HalfComponentSpacing - 50, posy + 5 + 1, 50, GUIUtil.LabelHeight ), "Copy", UITheme.SmallButtonStyle ) )
                     {
                        aggregatedTranslation.CopyToClipboard();
                        ShowCopied( "Copied " + aggregatedTranslation.Translator.Endpoint.Endpoint.FriendlyName );
                     }
                     GUI.enabled = true;
                     DrawTextArea(
                        posy,
                        scroller,
                        aggregatedTranslation.Translator.Endpoint.Endpoint.FriendlyName,
                        aggregatedTranslation.Translation.Translations,
                        _panelHeight );
                     posy += _panelHeight;
                  }
               }
            }
            else
            {
               GUI.enabled = false;
               GUI.Button( GUIUtil.R( _viewModel.Width - GUIUtil.HalfComponentSpacing - 50, posy + 5 + 1, 50, GUIUtil.LabelHeight ), "Copy", UITheme.SmallButtonStyle );
               GUI.enabled = true;
               DrawTextArea( posy, _originalText, "ORIGINAL TEXT", Empty, _panelHeight );
               posy += _panelHeight;

               GUI.enabled = false;
               GUI.Button( GUIUtil.R( _viewModel.Width - GUIUtil.HalfComponentSpacing - 50, posy + 5 + 1, 50, GUIUtil.LabelHeight ), "Copy", UITheme.SmallButtonStyle );
               GUI.enabled = true;
               DrawTextArea( posy, _defaultTranslation, "DEFAULT TRANSLATION", Empty, _panelHeight );
               posy += _panelHeight;

               for( int i = 0; i < _viewModel.AvailableTranslators.Count; i++ )
               {
                  var translator = _viewModel.AvailableTranslators[ i ];
                  if( translator.IsEnabled )
                  {
                     var scroller = GetTranslationView( translator );

                     GUI.enabled = false;
                     GUI.Button( GUIUtil.R( _viewModel.Width - GUIUtil.HalfComponentSpacing - 50, posy + 5 + 1, 50, GUIUtil.LabelHeight ), "Copy", UITheme.SmallButtonStyle );
                     GUI.enabled = true;
                     DrawTextArea(
                        posy,
                        scroller,
                        translator.Endpoint.Endpoint.FriendlyName,
                        Empty,
                        _panelHeight );
                     posy += _panelHeight;
                  }
               }
            }

            posy += GUIUtil.HalfComponentSpacing + GUIUtil.ComponentSpacing;

            var previousEnabled = GUI.enabled;

            GUI.enabled = _viewModel.HasPrevious();
            if( GUI.Button( GUIUtil.R( GUIUtil.HalfComponentSpacing, posy, 75, GUIUtil.LabelHeight ), "Previous", UITheme.SmallButtonStyle ) )
            {
               _viewModel.MovePrevious();
            }

            GUI.enabled = _viewModel.HasNext();
            if( GUI.Button( GUIUtil.R( GUIUtil.HalfComponentSpacing * 2 + 75 * 1, posy, 75, GUIUtil.LabelHeight ), "Next", UITheme.SmallButtonStyle ) )
            {
               _viewModel.MoveNext();
            }

            GUI.enabled = _viewModel.HasNext();
            if( GUI.Button( GUIUtil.R( GUIUtil.HalfComponentSpacing * 3 + 75 * 2, posy, 75, GUIUtil.LabelHeight ), "Last", UITheme.SmallButtonStyle ) )
            {
               _viewModel.MoveLatest();
            }

            GUI.enabled = true;
            if( GUI.Button( GUIUtil.R( _viewModel.Width - GUIUtil.HalfComponentSpacing - 75, posy, 75, GUIUtil.LabelHeight ), "Options", UITheme.SmallButtonStyle ) )
            {
               _viewModel.IsShowingOptions = true;
            }

            GUI.enabled = previousEnabled;
         }
         finally
         {
            AutoTranslationPlugin.Current.EnableAutoTranslator();
         }
      }

      private static bool _isScrollViewSupported = true;

      private ScrollPositioned<TranslatorViewModel> GetTranslationView( TranslatorViewModel translator )
      {
         return _translationViews.FirstOrDefault( x => x.ViewModel == translator )
            ?? new ScrollPositioned<TranslatorViewModel>( translator );
      }

      private void ShowCopied( string text )
      {
         _copiedText = text;
         _copiedUntil = Time.realtimeSinceStartup + 1.5f;
      }

      private void DrawTextArea( float posy, ScrollPositioned positioned, string title, IEnumerable<string> texts, float panelHeight )
      {
         GUI.Label( GUIUtil.R( GUIUtil.HalfComponentSpacing + 5, posy + 5, _viewModel.Width - GUIUtil.ComponentSpacing - 70, GUIUtil.LabelHeight ), title, UITheme.HeaderStyle );

         posy += GUIUtil.LabelHeight + GUIUtil.HalfComponentSpacing;

         float boxWidth = _viewModel.Width - GUIUtil.ComponentSpacing;
         float boxHeight = panelHeight - GUIUtil.LabelHeight - GUIUtil.HalfComponentSpacing;
         var areaRect = GUIUtil.R( GUIUtil.HalfComponentSpacing, posy, boxWidth, boxHeight );

         GUI.Box( areaRect, GUIUtil.none, UITheme.TextAreaStyle );

         var padding = UITheme.TextAreaStyle.padding;
         var viewport = GUIUtil.R(
            areaRect.x + padding.left,
            areaRect.y + padding.top,
            areaRect.width - padding.horizontal,
            areaRect.height - padding.vertical );

         var textList = texts as IList<string> ?? texts.ToList();
         var style = UITheme.LabelTranslation;
         var heights = MeasureTextHeights( textList, viewport.width, style );
         var contentHeight = GetTextContentHeight( heights );
         var scrollbarWidth = GUIUtil.GetScrollbarWidth( viewport.height, contentHeight );
         var contentWidth = viewport.width - scrollbarWidth;
         if( scrollbarWidth > 0f )
         {
            heights = MeasureTextHeights( textList, contentWidth, style );
            contentHeight = GetTextContentHeight( heights );
         }

         if( _isScrollViewSupported )
         {
            bool hasStartedScrollView = false;
            try
            {
               positioned.ScrollPosition = GUIUtil.BeginVerticalScrollView( viewport, positioned.ScrollPosition, contentHeight, out contentWidth );
               hasStartedScrollView = true;

               DrawTextLabels( textList, heights, contentWidth, style );
            }
            catch( NotSupportedException e )
            {
               XUnity.Common.Logging.XuaLogger.AutoTranslator.Warn( e, "A text scroll view could not be created. Fallback mode will be used." );
               _isScrollViewSupported = false;
            }
            finally
            {
               if( hasStartedScrollView )
               {
                  GUIUtil.EndVerticalScrollView( viewport, positioned.ScrollPosition, contentHeight );
               }
            }
         }

         if( !_isScrollViewSupported )
         {
            GUILayout.BeginArea( viewport );
            var fallbackHeights = MeasureTextHeights( textList, viewport.width, style );
            DrawTextLabels( textList, fallbackHeights, viewport.width, style );
            GUILayout.EndArea();
         }
      }

      private static float[] MeasureTextHeights( IList<string> texts, float width, GUIStyle style )
      {
         var heights = new float[ texts.Count ];
         for( int i = 0; i < texts.Count; i++ )
         {
            heights[ i ] = style.CalcHeight( new GUIContent( texts[ i ] ), width );
         }
         return heights;
      }

      private static float GetTextContentHeight( float[] heights )
      {
         float height = 0f;
         for( int i = 0; i < heights.Length; i++ )
         {
            height += heights[ i ] + 2f;
         }
         return height;
      }

      private static void DrawTextLabels( IList<string> texts, float[] heights, float width, GUIStyle style )
      {
         float y = 0f;
         for( int i = 0; i < texts.Count; i++ )
         {
            var content = new GUIContent( texts[ i ] );
            var height = heights[ i ];
            GUI.Label( GUIUtil.R( 0f, y, width, height ), content, style );
            y += height + 2f;
         }
      }
   }
}
