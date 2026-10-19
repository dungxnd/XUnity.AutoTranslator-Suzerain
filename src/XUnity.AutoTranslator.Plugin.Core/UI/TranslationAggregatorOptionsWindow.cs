using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.UI
{
   internal class TranslationAggregatorOptionsWindow
   {
      private static bool _isScrollViewSupported = true;
      private const int WindowId = 45733721;
      private const float WindowWidth = 320;

      private Rect _windowRect = new Rect( 700, 20, WindowWidth, 452 );
      private bool _isMouseDownOnWindow = false;
      private TranslationAggregatorViewModel _viewModel;
      private List<ToggleViewModel> _toggles;
      private Vector2 _scrollPosition;

      public TranslationAggregatorOptionsWindow( TranslationAggregatorViewModel viewModel )
      {
         _viewModel = viewModel;
         _toggles = _viewModel.AllTranslators.Select( x =>
         new ToggleViewModel(
            " " + x.Endpoint.Endpoint.FriendlyName,
            null,
            null,
            () => x.IsEnabled = !x.IsEnabled,
            () => x.IsEnabled,
            x.Endpoint.Error == null ) ).ToList();
      }

      public bool IsShown
      {
         get => _viewModel.IsShowingOptions;
         set => _viewModel.IsShowingOptions = value;
      }

      public void OnGUI()
      {
         _windowRect = GUIUtil.ClampToScreen( _windowRect );

         _windowRect = GUI.Window( WindowId, _windowRect, (GUI.WindowFunction)CreateWindowUI, "Aggregator Options", UITheme.WindowStyle );

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

      private void CreateWindowUI( int id )
      {
         try
         {
            AutoTranslationPlugin.Current.DisableAutoTranslator();

            if( GUI.Button( GUIUtil.R( WindowWidth - 28, 2, 22, 18 ), "X", UITheme.SmallButtonStyle ) )
            {
               IsShown = false;
            }

            float posx = GUIUtil.ComponentSpacing;
            float posy = GUIUtil.WindowTitleClearance + GUIUtil.ComponentSpacing;
            float contentWidth = WindowWidth - ( 2 * GUIUtil.ComponentSpacing );

            GUI.Label( GUIUtil.R( posx, posy, contentWidth, GUIUtil.LabelHeight ), "AVAILABLE TRANSLATORS", UITheme.HeaderStyle );
            posy += GUIUtil.LabelHeight + GUIUtil.HalfComponentSpacing;

            var listRect = GUIUtil.R( posx, posy, contentWidth, 250f );
            GUI.Box( listRect, GUIUtil.none, UITheme.TextAreaStyle );

            var padding = UITheme.TextAreaStyle.padding;
            var viewport = GUIUtil.R(
               listRect.x + padding.left,
               listRect.y + padding.top,
               listRect.width - padding.horizontal,
               listRect.height - padding.vertical );

            var contentHeight = Mathf.Max( 1f, _toggles.Count * ( GUIUtil.RowHeight + 2f ) );
            var scrollbarWidth = GUIUtil.GetScrollbarWidth( viewport.height, contentHeight );
            var itemWidth = viewport.width - scrollbarWidth;

            if( _isScrollViewSupported )
            {
               bool hasStartedScrollView = false;
               try
               {
                  _scrollPosition = GUIUtil.BeginVerticalScrollView( viewport, _scrollPosition, contentHeight, out itemWidth );
                  hasStartedScrollView = true;

                  DrawTranslatorToggles( itemWidth );
               }
               catch( NotSupportedException e )
               {
                  XUnity.Common.Logging.XuaLogger.AutoTranslator.Warn( e, "An options scroll view could not be created. Fallback mode will be used." );
                  _isScrollViewSupported = false;
               }
               finally
               {
                  if( hasStartedScrollView )
                  {
                     GUIUtil.EndVerticalScrollView( viewport, _scrollPosition, contentHeight );
                  }
               }
            }

            if( !_isScrollViewSupported )
            {
               GUILayout.BeginArea( viewport );
               DrawTranslatorToggles( viewport.width );
               GUILayout.EndArea();
            }

            posy += listRect.height + GUIUtil.ComponentSpacing;

            GUI.Label( GUIUtil.R( posx, posy, 50f, GUIUtil.RowHeight ), "Height", UITheme.LabelStyle );
            _viewModel.Height = Mathf.Round( GUI.HorizontalSlider(
               GUIUtil.R( posx + 55f, posy + 4f, contentWidth - 55f, 16f ),
               _viewModel.Height,
               50f,
               300f,
               UITheme.HorizontalSliderStyle,
               UITheme.HorizontalSliderThumbStyle ) );
            posy += GUIUtil.RowHeight + GUIUtil.HalfComponentSpacing;

            GUI.Label( GUIUtil.R( posx, posy, 50f, GUIUtil.RowHeight ), "Width", UITheme.LabelStyle );
            _viewModel.Width = Mathf.Round( GUI.HorizontalSlider(
               GUIUtil.R( posx + 55f, posy + 4f, contentWidth - 55f, 16f ),
               _viewModel.Width,
               200f,
               1000f,
               UITheme.HorizontalSliderStyle,
               UITheme.HorizontalSliderThumbStyle ) );
            posy += GUIUtil.RowHeight + GUIUtil.HalfComponentSpacing;

            if( GUI.Button( GUIUtil.R( posx, posy, 28f, GUIUtil.RowHeight ), "-", UITheme.SmallButtonStyle ) )
            {
               ChangeUIScale( -0.1f );
            }

            GUI.Label(
               GUIUtil.R( posx + 32f, posy, 120f, GUIUtil.RowHeight ),
               "UI Scale: " + GUIUtil.UIScale.ToString( "0.00", CultureInfo.InvariantCulture ) + "x",
               UITheme.LabelStyle );

            if( GUI.Button( GUIUtil.R( posx + 156f, posy, 28f, GUIUtil.RowHeight ), "+", UITheme.SmallButtonStyle ) )
            {
               ChangeUIScale( 0.1f );
            }

            if( GUI.Button( GUIUtil.R( posx + 188f, posy, 48f, GUIUtil.RowHeight ), "Auto", UITheme.SmallButtonStyle ) )
            {
               Settings.SetUIScale( "Auto" );
            }
         }
         finally
         {
            AutoTranslationPlugin.Current.EnableAutoTranslator();
         }
      }

      private void DrawTranslatorToggles( float itemWidth )
      {
         float y = 0f;
         foreach( var vm in _toggles )
         {
            var previousEnabled = GUI.enabled;

            GUI.enabled = vm.Enabled;
            var previousValue = vm.IsToggled();
            var newValue = GUI.Toggle(
               GUIUtil.R( 0f, y, itemWidth, GUIUtil.RowHeight ),
               previousValue,
               vm.Text,
               UITheme.ToggleStyle );
            if( previousValue != newValue )
            {
               vm.OnToggled();
            }

            GUI.enabled = previousEnabled;
            y += GUIUtil.RowHeight + 2f;
         }
      }

      private static void ChangeUIScale( float delta )
      {
         var scale = Mathf.Clamp( GUIUtil.UIScale + delta, 0.8f, 2.5f );
         Settings.SetUIScale( scale.ToString( "0.00", CultureInfo.InvariantCulture ) );
      }
   }
}
