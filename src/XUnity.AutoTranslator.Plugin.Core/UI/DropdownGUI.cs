using System;
using System.Collections.Generic;
using UnityEngine;
using XUnity.Common.Logging;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.UI
{

   internal class DropdownGUI<TDropdownOptionViewModel, TSelection>
      where TDropdownOptionViewModel : DropdownOptionViewModel<TSelection>
      where TSelection : class
   {

      private const float MaxHeight = GUIUtil.RowHeight * 6;

      private GUIContent _noSelection;
      private GUIContent _unselect;
      private DropdownViewModel<TDropdownOptionViewModel, TSelection> _viewModel;

      private float _x;
      private float _y;
      private float _width;
      private float _maxY;
      private bool _isShown;
      private Rect _popupRect;
      private Vector2 _scrollPosition;

      public DropdownGUI( float x, float y, float width, float maxY, DropdownViewModel<TDropdownOptionViewModel, TSelection> viewModel )
      {
         _x = x;
         _y = y;
         _width = width;
         _maxY = maxY;
         _noSelection = GUIUtil.CreateContent( viewModel.NoSelection, viewModel.NoSelectionTooltip );
         _unselect = GUIUtil.CreateContent( viewModel.Unselect, viewModel.UnselectTooltip );

         _viewModel = viewModel;
      }

      public bool OnGUI( bool enabled )
      {
         var previouslyEnabled = GUI.enabled;

         try
         {
            GUI.enabled = enabled;

            var headerRect = GUIUtil.R( _x, _y, _width, GUIUtil.RowHeight );
            bool clicked = GUI.Button(
               headerRect,
               _viewModel.CurrentSelection?.Text ?? _noSelection,
               _isShown ? UITheme.DropdownHeaderActiveStyle : UITheme.DropdownHeaderStyle );

            GUI.DrawTexture(
               GUIUtil.R( headerRect.x + headerRect.width - 16, headerRect.y + ( headerRect.height - 4f ) / 2f, 7f, 4f ),
               _isShown ? UITheme.CaretUp : UITheme.CaretDown );

            if( clicked )
            {
               _isShown = !_isShown;
            }

            if( !enabled )
            {
               _isShown = false;
            }

            if( _isShown )
            {
               ShowDropdown( _x, _y + GUIUtil.RowHeight, _width );
            }

            // This runs inside GUI.Window, so Event.current.mousePosition is already
            // in window-local GUI coordinates and matches headerRect/_popupRect.
            var mousePosition = Event.current.mousePosition;
            if( !clicked
               && Event.current.type == EventType.MouseDown
               && !headerRect.Contains( mousePosition )
               && !_popupRect.Contains( mousePosition ) )
            {
               _isShown = false;
            }

            return _isShown;
         }
         finally
         {
            GUI.enabled = previouslyEnabled;
         }
      }

      private bool _supportsScrollView = true;

      private void ShowDropdown( float x, float y, float width )
      {
         var contentHeight = ( _viewModel.Options.Count + 1 ) * GUIUtil.RowHeight;
         var padding = UITheme.DropdownPopupStyle.padding;
         var viewportHeight = Mathf.Min( contentHeight, MaxHeight );
         var popupHeight = viewportHeight + padding.vertical;

         if( _maxY > 0f && y + popupHeight > _maxY )
         {
            y = Mathf.Max( 0f, _y - popupHeight );
         }

         var rect = GUIUtil.R( x, y, width, popupHeight );
         _popupRect = rect;

         GUI.Box( rect, GUIUtil.none, UITheme.DropdownPopupStyle );

         var viewport = GUIUtil.R(
            rect.x + padding.left,
            rect.y + padding.top,
            rect.width - padding.horizontal,
            viewportHeight );

         var itemY = 0f;
         var scrollStarted = false;
         float contentWidth = viewport.width;

         if( _supportsScrollView )
         {
            try
            {
               _scrollPosition = GUIUtil.BeginVerticalScrollView( viewport, _scrollPosition, contentHeight, out contentWidth );
               scrollStarted = true;
               DrawItems( ref itemY, contentWidth );
            }
            catch( NotSupportedException e )
            {
               _supportsScrollView = false;
               XuaLogger.AutoTranslator.Warn( e, "A dropdown scroll view could not be created. Proceeding without scrolling." );
            }
            finally
            {
               if( scrollStarted )
               {
                  GUIUtil.EndVerticalScrollView( viewport, _scrollPosition, contentHeight );
               }
            }
         }

         if( !scrollStarted )
         {
            GUILayout.BeginArea( viewport );
            itemY = 0f;
            DrawItems( ref itemY, viewport.width );
            GUILayout.EndArea();
         }
      }

      private void DrawItems( ref float itemY, float contentWidth )
      {
         var previousEnabled = GUI.enabled;

         var unselectStyle = _viewModel.CurrentSelection == null ? UITheme.DropdownItemSelectedStyle : UITheme.DropdownItemStyle;
         if( GUI.Button( GUIUtil.R( 0f, itemY, contentWidth, GUIUtil.RowHeight ), _unselect, unselectStyle ) )
         {
            _viewModel.Select( null );
            _isShown = false;
         }
         itemY += GUIUtil.RowHeight;

         foreach( var option in _viewModel.Options )
         {
            GUI.enabled = option.IsEnabled();
            var style = option.IsSelected() ? UITheme.DropdownItemSelectedStyle : UITheme.DropdownItemStyle;
            if( GUI.Button( GUIUtil.R( 0f, itemY, contentWidth, GUIUtil.RowHeight ), option.Text, style ) )
            {
               _viewModel.Select( option );
               _isShown = false;
            }
            itemY += GUIUtil.RowHeight;
         }

         GUI.enabled = previousEnabled;
      }
   }
}
