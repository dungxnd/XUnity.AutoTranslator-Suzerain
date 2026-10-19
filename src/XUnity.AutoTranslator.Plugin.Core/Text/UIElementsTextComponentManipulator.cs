using System;
using System.Linq;
using System.Reflection;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Text
{
   internal sealed class UIElementsTextComponentManipulator : ITextComponentManipulator
   {
      private readonly CachedProperty _textProperty;
      private readonly Action<object, string> _setValueWithoutNotify;
      private readonly Action<object> _markDirtyText;
      private readonly Action<object> _markDirtyRepaint;

      public UIElementsTextComponentManipulator( Type type )
      {
         _textProperty = type.CachedProperty( "text" );
         _setValueWithoutNotify = CreateSetValueWithoutNotify( type );

         var markDirtyText = type.CachedMethod( "MarkDirtyText" );
         _markDirtyText = markDirtyText != null
            ? new Action<object>( ui => markDirtyText.Invoke( ui ) )
            : null;

         var markDirtyRepaint = type.CachedMethod( "MarkDirtyRepaint" );
         _markDirtyRepaint = markDirtyRepaint != null
            ? new Action<object>( ui => markDirtyRepaint.Invoke( ui ) )
            : null;
      }

      public string GetText( object ui )
      {
         return (string)_textProperty?.Get( ui );
      }

      public void SetText( object ui, string text )
      {
         if( _setValueWithoutNotify != null )
         {
            try
            {
               _setValueWithoutNotify( ui, text );
               if( _markDirtyText != null )
               {
                  _markDirtyText( ui );
               }
               else
               {
                  _markDirtyRepaint?.Invoke( ui );
               }
               return;
            }
            catch
            {
            }
         }

         _textProperty?.Set( ui, text );
      }

      private static Action<object, string> CreateSetValueWithoutNotify( Type type )
      {
         try
         {
            var setValueWithoutNotify = type
               .GetInterfaces()
               .Where( x => x.IsGenericType )
               .Where( x => x.GetGenericTypeDefinition().Name == "INotifyValueChanged`1" )
               .Where( x => x.GetGenericArguments()[ 0 ] == typeof( string ) )
               .Select( x => x.GetMethod( "SetValueWithoutNotify" ) )
               .FirstOrDefault( x => x != null )
               ?? type.GetMethod( "SetValueWithoutNotify", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof( string ) }, null );

            if( setValueWithoutNotify != null )
            {
               return ( ui, text ) => setValueWithoutNotify.Invoke( ui, new object[] { text } );
            }
         }
         catch
         {
         }

         return null;
      }
   }
}
