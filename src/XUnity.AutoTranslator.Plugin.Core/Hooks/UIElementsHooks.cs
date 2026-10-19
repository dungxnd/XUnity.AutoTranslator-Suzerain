using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Constants;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using XUnity.AutoTranslator.Plugin.Core.Text;
using XUnity.Common.Constants;
using XUnity.Common.Extensions;
using XUnity.Common.Harmony;
using XUnity.Common.Logging;
using XUnity.Common.MonoMod;
using XUnity.Common.Utilities;

namespace XUnity.AutoTranslator.Plugin.Core.Hooks.UIElements
{
   internal static class UIElementsHooks
   {
      public static readonly Type[] All = new[] {
         typeof( TextElement_text_Hook ),
#if MANAGED
         typeof( TextElement_SetText_StringBuilder_Hook ),
         typeof( TextElement_SetText_CharArray_Range_Hook ),
         typeof( TextElement_SetText_Int_Hook ),
         typeof( TextElement_SetText_Float_Hook ),
#endif
      };
   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TextElement_text_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TextElement != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Property( UnityTypes.TextElement?.ClrType, "text" )?.GetSetMethod();
      }

#if MANAGED
      static void Postfix( object __instance )
#else
      static void Postfix( Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase __instance )
#endif
      {
#if IL2CPP
         __instance = Il2CppUtilities.CreateProxyComponentWithDerivedType( __instance.Pointer, UnityTypes.TextElement.ClrType );
#endif

         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, false );
      }

#if MANAGED
      static Action<object, string> _original;

      static void MM_Init( object detour )
      {
         _original = detour.GenerateTrampolineEx<Action<object, string>>();
      }

      static void MM_Detour( object __instance, string value )
      {
         _original( __instance, value );

         Postfix( __instance );
      }
#endif
   }

#if MANAGED
   [HookingHelperPriority( HookPriority.Last )]
   internal static class TextElement_SetText_StringBuilder_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TextElement != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.TextElement?.ClrType, "SetText", new[] { typeof( StringBuilder ) } );
      }

      static void Postfix( object __instance )
      {
         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, false );
      }

      static Action<object, StringBuilder> _original;

      static void MM_Init( object detour )
      {
         _original = detour.GenerateTrampolineEx<Action<object, StringBuilder>>();
      }

      static void MM_Detour( object __instance, StringBuilder value )
      {
         _original( __instance, value );

         Postfix( __instance );
      }
   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TextElement_SetText_CharArray_Range_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TextElement != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.TextElement?.ClrType, "SetText", new[] { typeof( char[] ), typeof( int ), typeof( int ) } );
      }

      static void Postfix( object __instance )
      {
         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, false );
      }

      static Action<object, char[], int, int> _original;

      static void MM_Init( object detour )
      {
         _original = detour.GenerateTrampolineEx<Action<object, char[], int, int>>();
      }

      static void MM_Detour( object __instance, char[] value, int start, int length )
      {
         _original( __instance, value, start, length );

         Postfix( __instance );
      }
   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TextElement_SetText_Int_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TextElement != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.TextElement?.ClrType, "SetText", new[] { typeof( int ) } );
      }

      static void Postfix( object __instance )
      {
         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, false );
      }

      static Action<object, int> _original;

      static void MM_Init( object detour )
      {
         _original = detour.GenerateTrampolineEx<Action<object, int>>();
      }

      static void MM_Detour( object __instance, int value )
      {
         _original( __instance, value );

         Postfix( __instance );
      }
   }

   [HookingHelperPriority( HookPriority.Last )]
   internal static class TextElement_SetText_Float_Hook
   {
      static bool Prepare( object instance )
      {
         return UnityTypes.TextElement != null;
      }

      static MethodBase TargetMethod( object instance )
      {
         return AccessToolsShim.Method( UnityTypes.TextElement?.ClrType, "SetText", new[] { typeof( float ), typeof( string ) } );
      }

      static void Postfix( object __instance )
      {
         AutoTranslationPlugin.Current.Hook_TextChanged( __instance, false );
      }

      static Action<object, float, string> _original;

      static void MM_Init( object detour )
      {
         _original = detour.GenerateTrampolineEx<Action<object, float, string>>();
      }

      static void MM_Detour( object __instance, float value, string format )
      {
         _original( __instance, value, format );

         Postfix( __instance );
      }
   }
#endif
}
