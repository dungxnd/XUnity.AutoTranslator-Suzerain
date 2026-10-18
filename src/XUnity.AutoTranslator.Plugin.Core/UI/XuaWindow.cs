using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.UI
{
   internal class XuaWindow
   {
      private const int WindowId = 5464332;
      private const float WindowWidth = 320;

      private Rect _windowRect = new Rect( 20, 20, WindowWidth, 300 );

      private DropdownGUI<TranslatorDropdownOptionViewModel, TranslationEndpointManager> _endpointDropdown;
      private DropdownGUI<TranslatorDropdownOptionViewModel, TranslationEndpointManager> _fallbackDropdown;
      private XuaViewModel _viewModel;
      private bool _isMouseDownOnWindow = false;

      public bool IsShown
      {
         get => _viewModel.IsShown;
         set => _viewModel.IsShown = value;
      }

      public XuaWindow( XuaViewModel viewModel )
      {
         _viewModel = viewModel;
      }

      public void OnGUI()
      {
         _windowRect.height = GetPreferredHeight();
         _windowRect = GUIUtil.ClampToScreen( _windowRect );

         _windowRect = GUI.Window( WindowId, _windowRect, (GUI.WindowFunction)CreateWindowUI, "XUAT - Unity Auto Translator", UITheme.WindowStyle );

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

      private float GetPreferredHeight()
      {
         var rows = ( _viewModel.CommandButtons.Count + 2 ) / 3;
         var posy = GUIUtil.WindowTitleClearance + GUIUtil.ComponentSpacing;

         posy += GUIUtil.RowHeight * _viewModel.Toggles.Count;
         posy += GUIUtil.ComponentSpacing;

         posy += GUIUtil.RowHeight + GUIUtil.ComponentSpacing;
         posy += ( GUIUtil.RowHeight + GUIUtil.ComponentSpacing ) * rows;

         posy += GUIUtil.RowHeight + GUIUtil.ComponentSpacing;
         posy += GUIUtil.RowHeight + GUIUtil.HalfComponentSpacing;
         posy += GUIUtil.RowHeight + GUIUtil.ComponentSpacing;

         posy += GUIUtil.RowHeight + GUIUtil.ComponentSpacing;
         posy += ( GUIUtil.RowHeight + GUIUtil.ComponentSpacing ) * _viewModel.Labels.Count;

         posy += GUIUtil.RowHeight * 5;
         posy += GUIUtil.ComponentSpacing;

         return posy;
      }
      
      private void CreateWindowUI( int id )
      {
         try
         {
            AutoTranslationPlugin.Current.DisableAutoTranslator();

            float posx = GUIUtil.ComponentSpacing;
            float posy = GUIUtil.WindowTitleClearance + GUIUtil.ComponentSpacing;
            const float col1x = GUIUtil.ComponentSpacing;
            const float col2x = 120;
            const float col2 = WindowWidth - col2x - GUIUtil.ComponentSpacing;
            const float col12 = WindowWidth - ( 2 * GUIUtil.ComponentSpacing );

            if( GUI.Button( GUIUtil.R( WindowWidth - 28, 2, 22, 18 ), "X", UITheme.SmallButtonStyle ) )
            {
               IsShown = false;
            }

            // GROUP
            var toggles = _viewModel.Toggles;
            var groupHeight = ( GUIUtil.RowHeight * toggles.Count ) + ( GUIUtil.HalfComponentSpacing * toggles.Count ) - GUIUtil.ComponentSpacing;
            GUI.Box( GUIUtil.R( GUIUtil.HalfComponentSpacing, posy, WindowWidth - GUIUtil.ComponentSpacing, groupHeight ), GUIUtil.none, UITheme.PanelStyle );

            foreach( var vm in toggles )
            {
               var previousValue = vm.IsToggled();
               var newValue = GUI.Toggle( GUIUtil.R( col1x, posy + 3, col12, GUIUtil.RowHeight - 3 ), previousValue, vm.Text, UITheme.ToggleStyle );
               if( previousValue != newValue )
               {
                  vm.OnToggled();
               }
               posy += GUIUtil.RowHeight;
            }
            posy += GUIUtil.ComponentSpacing;

            var commandButtons = _viewModel.CommandButtons;
            const int buttonsPerRow = 3;
            const float buttonWidth = ( col12 - ( GUIUtil.ComponentSpacing * ( buttonsPerRow - 1 ) ) ) / buttonsPerRow;
            var rows = commandButtons.Count / buttonsPerRow;
            if( commandButtons.Count % 3 != 0 ) rows++;

            // GROUP
            groupHeight = GUIUtil.LabelHeight + ( GUIUtil.RowHeight * rows ) + ( GUIUtil.ComponentSpacing * ( rows + 1 ) ) - GUIUtil.HalfComponentSpacing;
            GUI.Box( GUIUtil.R( GUIUtil.HalfComponentSpacing, posy, WindowWidth - GUIUtil.ComponentSpacing, groupHeight ), GUIUtil.none, UITheme.PanelStyle );

            GUI.Label( GUIUtil.R( col1x, posy, col12, GUIUtil.LabelHeight ), "ACTIONS", UITheme.HeaderStyle );
            posy += GUIUtil.RowHeight + GUIUtil.ComponentSpacing;

            for( int row = 0; row < rows; row++ )
            {
               posx = GUIUtil.ComponentSpacing;

               for( int col = 0; col < buttonsPerRow; col++ )
               {
                  int idx = ( row * buttonsPerRow ) + col;
                  if( idx >= commandButtons.Count ) break;

                  var vm = commandButtons[ idx ];

                  GUI.enabled = vm.CanClick?.Invoke() != false;
                  if( GUI.Button( GUIUtil.R( posx, posy, buttonWidth, GUIUtil.RowHeight ), vm.Text, UITheme.ButtonStyle ) )
                  {
                     vm.OnClicked?.Invoke();
                  }
                  GUI.enabled = true;

                  posx += GUIUtil.ComponentSpacing + buttonWidth;
               }
               posy += GUIUtil.RowHeight + GUIUtil.ComponentSpacing;
            }

            // GROUP
            groupHeight = GUIUtil.LabelHeight + ( GUIUtil.RowHeight * 2 ) + ( GUIUtil.ComponentSpacing * 2 );
            GUI.Box( GUIUtil.R( GUIUtil.HalfComponentSpacing, posy, WindowWidth - GUIUtil.ComponentSpacing, groupHeight ), GUIUtil.none, UITheme.PanelStyle );

            GUI.Label( GUIUtil.R( col1x, posy, col12, GUIUtil.LabelHeight ), "TRANSLATOR", UITheme.HeaderStyle );
            posy += GUIUtil.RowHeight + GUIUtil.ComponentSpacing;

            GUI.Label( GUIUtil.R( col1x, posy, GUIUtil.LabelWidth + 10, GUIUtil.LabelHeight ), "Translator", UITheme.LabelStyle );
            float endpointDropdownPosy = posy;
            posy += GUIUtil.RowHeight + GUIUtil.HalfComponentSpacing;

            GUI.Label( GUIUtil.R( col1x, posy, GUIUtil.LabelWidth, GUIUtil.LabelHeight ), "Fallback", UITheme.LabelStyle );
            float fallbackDropdownPosy = posy;
            posy += GUIUtil.RowHeight + GUIUtil.ComponentSpacing;

            // GROUP
            var labels = _viewModel.Labels;
            groupHeight = GUIUtil.LabelHeight + ( GUIUtil.RowHeight * labels.Count ) + ( GUIUtil.ComponentSpacing * ( labels.Count + 1 ) ) - GUIUtil.HalfComponentSpacing;
            GUI.Box( GUIUtil.R( GUIUtil.HalfComponentSpacing, posy, WindowWidth - GUIUtil.ComponentSpacing, groupHeight ), GUIUtil.none, UITheme.PanelStyle );

            GUI.Label( GUIUtil.R( col1x, posy, col12, GUIUtil.LabelHeight ), "STATUS", UITheme.HeaderStyle );
            posy += GUIUtil.RowHeight + GUIUtil.ComponentSpacing;

            foreach( var label in labels )
            {
               GUI.Label( GUIUtil.R( col1x, posy, col2x - col1x - GUIUtil.HalfComponentSpacing, GUIUtil.LabelHeight ), label.Title, UITheme.LabelStyle );
               GUI.Label( GUIUtil.R( col2x, posy, col2, GUIUtil.LabelHeight ), label.GetValue(), UITheme.LabelRight );
               posy += GUIUtil.RowHeight + GUIUtil.ComponentSpacing;
            }

            var endpointDropdown = _endpointDropdown ?? ( _endpointDropdown = new DropdownGUI<TranslatorDropdownOptionViewModel, TranslationEndpointManager>( col2x, endpointDropdownPosy, col2, GetPreferredHeight(), _viewModel.TranslatorDropdown ) );
            var isShown = endpointDropdown.OnGUI(true);

            var fallbackDropdown = _fallbackDropdown ?? ( _fallbackDropdown = new DropdownGUI<TranslatorDropdownOptionViewModel, TranslationEndpointManager>( col2x, fallbackDropdownPosy, col2, GetPreferredHeight(), _viewModel.FallbackDropdown ) );
            fallbackDropdown.OnGUI( !isShown );

            GUI.Label( GUIUtil.R( col1x, posy, col12, GUIUtil.RowHeight * 5 ), GUI.tooltip, UITheme.TooltipStyle );
         }
         finally
         {
            AutoTranslationPlugin.Current.EnableAutoTranslator();
         }
      }
   }
}
