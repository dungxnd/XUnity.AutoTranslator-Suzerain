using System;
using System.Collections.Generic;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.UI
{
   internal class LlmAdvancedWindow
   {
      private const int WindowId = 45733722;
      private const float WindowWidth = 420;
      private const float WindowHeight = 360;

      private readonly List<AdvancedSettingViewModel> _settings;
      private Rect _windowRect = new Rect( 360, 20, WindowWidth, WindowHeight );
      private bool _isMouseDownOnWindow;
      private bool _isShown;
      private bool _needsSync;
      private ITranslateEndpointSettingsEditor _lastEditor;
      private string _error = string.Empty;

      public LlmAdvancedWindow()
      {
         _settings = new List<AdvancedSettingViewModel>
         {
            new AdvancedSettingViewModel( "Temperature", "temperature", "Model temperature. Leave empty or use 'default' to remove a UI override." ),
            new AdvancedSettingViewModel( "Max tokens", "max_tokens", "Maximum response tokens. For Ollama this becomes num_predict." ),
            new AdvancedSettingViewModel( "Top P", "top_p", "Nucleus sampling probability between 0 and 1." ),
            new AdvancedSettingViewModel( "Max translations", "maxTranslationsPerRequest", "Maximum strings sent in one request." ),
            new AdvancedSettingViewModel( "Max chars/request", "maxCharactersPerRequest", "Character budget for one request." ),
            new AdvancedSettingViewModel( "Max chars/translation", "maxCharactersPerTranslation", "Longer texts are sent alone and split into blocks." ),
            new AdvancedSettingViewModel( "Model", "model", "Model identifier." ),
            new AdvancedSettingViewModel( "URL", "url", "Chat-completions endpoint URL." ),
         };
      }

      public bool IsShown
      {
         get => _isShown;
         set
         {
            if( value && !_isShown )
            {
               _needsSync = true;
            }

            _isShown = value;
         }
      }

      public void OnGUI()
      {
         var editor = GetEditor();
         if( _needsSync || !ReferenceEquals( editor, _lastEditor ) )
         {
            SyncValues( editor );
            _needsSync = false;
         }

         _windowRect = GUIUtil.ClampToScreen( _windowRect );

         _windowRect = GUI.Window( WindowId, _windowRect, (GUI.WindowFunction)CreateWindowUI, "LLM Advanced Settings", UITheme.WindowStyle );

         if( GUIUtil.IsAnyMouseButtonOrScrollWheelDownSafe )
         {
            var point = GUIUtil.ScreenToGuiPoint( new Vector2( UnityInput.Current.mousePosition.x, UnityInput.Current.mousePosition.y ) );
            _isMouseDownOnWindow = _windowRect.Contains( point );
         }

         if( !_isMouseDownOnWindow || !GUIUtil.IsAnyMouseButtonOrScrollWheelSafe )
            return;

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

      private static ITranslateEndpointSettingsEditor GetEditor()
      {
         return AutoTranslationPlugin.Current?.TranslationManager?.CurrentEndpoint?.Endpoint as ITranslateEndpointSettingsEditor;
      }

      private void SyncValues( ITranslateEndpointSettingsEditor editor )
      {
         _lastEditor = editor;
         _error = string.Empty;

         foreach( var setting in _settings )
         {
            setting.Value = editor != null && editor.TryGetSetting( setting.Key, out var value )
               ? value
               : string.Empty;
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

            var editor = GetEditor();
            if( editor == null )
            {
               GUI.Label(
                  GUIUtil.R( GUIUtil.ComponentSpacing, GUIUtil.WindowTitleClearance + GUIUtil.ComponentSpacing, WindowWidth - ( 2 * GUIUtil.ComponentSpacing ), GUIUtil.LabelHeight * 2 ),
                  "Select an LLM endpoint to edit advanced settings.",
                  UITheme.TooltipStyle );
               return;
            }

            if( !ReferenceEquals( editor, _lastEditor ) )
            {
               SyncValues( editor );
            }

            float posx = GUIUtil.ComponentSpacing;
            float posy = GUIUtil.WindowTitleClearance + GUIUtil.ComponentSpacing;
            float contentWidth = WindowWidth - ( 2 * GUIUtil.ComponentSpacing );

            GUI.Label( GUIUtil.R( posx, posy, contentWidth, GUIUtil.LabelHeight ), "REQUEST SETTINGS", UITheme.HeaderStyle );
            posy += GUIUtil.RowHeight + GUIUtil.HalfComponentSpacing;

            var listRect = GUIUtil.R( posx, posy, contentWidth, WindowHeight - posy - 60f );
            GUI.Box( listRect, GUIUtil.none, UITheme.TextAreaStyle );

            var padding = UITheme.TextAreaStyle.padding;
            var rowX = listRect.x + padding.left;
            var rowY = listRect.y + padding.top;
            var itemWidth = listRect.width - padding.horizontal;
            const float labelWidth = 130f;
            const float buttonWidth = 46f;
            var fieldWidth = itemWidth - labelWidth - buttonWidth - GUIUtil.ComponentSpacing;

            foreach( var setting in _settings )
            {
               GUI.Label( GUIUtil.R( rowX, rowY, labelWidth, GUIUtil.RowHeight ), GUIUtil.CreateContent( setting.Title, setting.Tooltip ), UITheme.LabelStyle );
               setting.Value = GUI.TextField(
                  GUIUtil.R( rowX + labelWidth, rowY, fieldWidth, GUIUtil.RowHeight ),
                  setting.Value,
                  UITheme.TextFieldStyle );

               if( GUI.Button(
                  GUIUtil.R( rowX + labelWidth + fieldWidth + GUIUtil.ComponentSpacing, rowY, buttonWidth, GUIUtil.RowHeight ),
                  "Set",
                  UITheme.SmallButtonStyle ) )
               {
                  if( editor.TrySetSetting( setting.Key, setting.Value, out var error ) )
                  {
                     _error = string.Empty;
                     if( editor.TryGetSetting( setting.Key, out var value ) )
                     {
                        setting.Value = value;
                     }
                  }
                  else
                  {
                     _error = error;
                  }
               }

               rowY += GUIUtil.RowHeight + 4f;
            }

            var message = string.IsNullOrEmpty( _error )
               ? "Saved to LLMConfig.yaml.\n'default' removes a model parameter."
               : _error;
            GUI.Label(
               GUIUtil.R( posx, listRect.y + listRect.height + GUIUtil.ComponentSpacing, contentWidth, GUIUtil.LabelHeight * 2 ),
               message,
               UITheme.TooltipStyle );
         }
         finally
         {
            AutoTranslationPlugin.Current.EnableAutoTranslator();
         }
      }

      private sealed class AdvancedSettingViewModel
      {
         public AdvancedSettingViewModel( string title, string key, string tooltip )
         {
            Title = title;
            Key = key;
            Tooltip = tooltip;
            Value = string.Empty;
         }

         public string Title { get; }

         public string Key { get; }

         public string Tooltip { get; }

         public string Value { get; set; }
      }
   }
}
