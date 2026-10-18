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

            GUILayout.Space( 22f );
            GUILayout.Label( "AVAILABLE TRANSLATORS", UITheme.HeaderStyle, ArrayHelper.Null<GUILayoutOption>() );

            // GROUP
            if( _isScrollViewSupported )
            {
               bool hasStartedScrollView = false;
               try
               {
                  _scrollPosition = GUILayout.BeginScrollView( _scrollPosition, UITheme.TextAreaStyle, GUILayout.Height( 250 ) );
                  hasStartedScrollView = true;

                  foreach( var vm in _toggles )
                  {
                     var previousEnabled = GUI.enabled;

                     GUI.enabled = vm.Enabled;
                     var previousValue = vm.IsToggled();
                     var newValue = GUILayout.Toggle( previousValue, vm.Text, UITheme.ToggleStyle, ArrayHelper.Null<GUILayoutOption>() );
                     if( previousValue != newValue )
                     {
                        vm.OnToggled();
                     }

                     GUI.enabled = previousEnabled;
                  }
               }
               catch( System.Exception e )
               {
                  if ( e is System.NotSupportedException )
                  {
                     XUnity.Common.Logging.XuaLogger.AutoTranslator.Warn( e, "An error occurred while calling GUILayout.BeginScrollView. Fallback mode will be used." );
                     _isScrollViewSupported = false;
                  }
                  else throw;
               }
               finally
               {
                  if( hasStartedScrollView ) GUILayout.EndScrollView();
               }
            }

            if( !_isScrollViewSupported )
            {
               GUILayout.BeginVertical( UITheme.TextAreaStyle, ArrayHelper.Null<GUILayoutOption>() );

               foreach( var vm in _toggles )
               {
                  var previousEnabled = GUI.enabled;

                  GUI.enabled = vm.Enabled;
                  var previousValue = vm.IsToggled();
                  var newValue = GUILayout.Toggle( previousValue, vm.Text, UITheme.ToggleStyle, ArrayHelper.Null<GUILayoutOption>() );
                  if( previousValue != newValue )
                  {
                     vm.OnToggled();
                  }

                  GUI.enabled = previousEnabled;
               }

               GUILayout.EndVertical();
            }

            GUILayout.BeginHorizontal( ArrayHelper.Null<GUILayoutOption>() );
            GUILayout.Label( "Height", UITheme.LabelStyle, ArrayHelper.Null<GUILayoutOption>() );
            _viewModel.Height = Mathf.Round( GUILayout.HorizontalSlider( _viewModel.Height, 50, 300, UITheme.HorizontalSliderStyle, UITheme.HorizontalSliderThumbStyle, ArrayHelper.Null<GUILayoutOption>() ) );
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal( ArrayHelper.Null<GUILayoutOption>() );
            GUILayout.Label( "Width", UITheme.LabelStyle, ArrayHelper.Null<GUILayoutOption>() );
            _viewModel.Width = Mathf.Round( GUILayout.HorizontalSlider( _viewModel.Width, 200, 1000, UITheme.HorizontalSliderStyle, UITheme.HorizontalSliderThumbStyle, ArrayHelper.Null<GUILayoutOption>() ) );
            GUILayout.EndHorizontal();

            GUILayout.Space( 4f );
            GUILayout.BeginHorizontal( ArrayHelper.Null<GUILayoutOption>() );
            if( GUILayout.Button( "-", UITheme.SmallButtonStyle, GUILayout.Width( 28 ) ) )
            {
               ChangeUIScale( -0.1f );
            }
            GUILayout.Label( "UI Scale: " + GUIUtil.UIScale.ToString( "0.00", CultureInfo.InvariantCulture ) + "x", UITheme.LabelStyle, GUILayout.Width( 120 ) );
            if( GUILayout.Button( "+", UITheme.SmallButtonStyle, GUILayout.Width( 28 ) ) )
            {
               ChangeUIScale( 0.1f );
            }
            if( GUILayout.Button( "Auto", UITheme.SmallButtonStyle, GUILayout.Width( 48 ) ) )
            {
               Settings.SetUIScale( "Auto" );
            }
            GUILayout.EndHorizontal();
         }
         finally
         {
            AutoTranslationPlugin.Current.EnableAutoTranslator();
         }
      }

      private static void ChangeUIScale( float delta )
      {
         var scale = Mathf.Clamp( GUIUtil.UIScale + delta, 0.8f, 2.5f );
         Settings.SetUIScale( scale.ToString( "0.00", CultureInfo.InvariantCulture ) );
      }
   }
}
