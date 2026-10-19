using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using XUnity.Common.Utilities;
using XUnity.AutoTranslator.Plugin.Core.Extensions;
using UnityEngine;
using XUnity.Common.Extensions;

#if IL2CPP
using Il2CppInterop.Runtime;
#endif

namespace XUnity.AutoTranslator.Plugin.Core.Utilities
{
   internal static class ComponentHelper
   {
#if MANAGED
      private static readonly MethodInfo FindObjectsByTypeMethod = typeof( UnityEngine.Object )
         .GetMethods( BindingFlags.Public | BindingFlags.Static )
         .Where( x => x.Name == "FindObjectsByType" )
         .Where( x => x.GetParameters().Length == 3 && x.GetParameters()[ 0 ].ParameterType == typeof( Type ) )
         .FirstOrDefault()
         ?? typeof( UnityEngine.Object )
         .GetMethods( BindingFlags.Public | BindingFlags.Static )
         .Where( x => x.Name == "FindObjectsByType" )
         .Where( x => x.GetParameters().Length == 2 && x.GetParameters()[ 0 ].ParameterType == typeof( Type ) )
         .FirstOrDefault();
#endif

      public static T[] FindObjectsOfType<T>()
         where T : UnityEngine.Object
      {
#if MANAGED
         if( typeof( T ) == typeof( GameObject ) || typeof( Component ).IsAssignableFrom( typeof( T ) ) )
         {
            return FindObjectsOfType<T>( false );
         }
#endif

#if IL2CPP
         var il2cppType = Il2CppSystem.Type.internal_from_handle( IL2CPP.il2cpp_class_get_type( Il2CppClassPointerStore<T>.NativeClassPtr ) );
         var objects = UnityEngine.Object.FindObjectsOfType( il2cppType );
#else
         var objects = UnityEngine.Object.FindObjectsOfType( typeof( T ) );
#endif
         return Cast<T>( objects );
      }

      public static T[] FindObjectsOfType<T>( bool includeInactive )
         where T : UnityEngine.Object
      {
#if MANAGED
         return Cast<T>( FindObjectsOfType( typeof( T ), includeInactive ) );
#else
         return FindObjectsOfType<T>();
#endif
      }

      public static UnityEngine.Object[] FindObjectsOfType( Type type, bool includeInactive )
      {
#if MANAGED
         if( FindObjectsByTypeMethod != null )
         {
            try
            {
               var parameters = FindObjectsByTypeMethod.GetParameters();
               var inactive = Enum.Parse( parameters[ 1 ].ParameterType, includeInactive ? "Include" : "Exclude" );
               object[] arguments;
               if( parameters.Length == 3 )
               {
                  var sortMode = Enum.Parse( parameters[ 2 ].ParameterType, "None" );
                  arguments = new object[] { type, inactive, sortMode };
               }
               else
               {
                  arguments = new object[] { type, inactive };
               }

               return (UnityEngine.Object[])FindObjectsByTypeMethod.Invoke( null, arguments );
            }
            catch
            {
            }
         }

         if( includeInactive )
         {
            var all = Resources.FindObjectsOfTypeAll( type );
            var result = new List<UnityEngine.Object>( all.Length );
            for( int i = 0 ; i < all.Length ; i++ )
            {
               if( IsSceneObject( all[ i ] ) )
               {
                  result.Add( all[ i ] );
               }
            }
            return result.ToArray();
         }

         return UnityEngine.Object.FindObjectsOfType( type );
#else
         return null;
#endif
      }

      private static T[] Cast<T>( UnityEngine.Object[] objects )
         where T : UnityEngine.Object
      {
         if( objects == null ) return null;

         var typedArr = new T[ objects.Length ];
         for( int i = 0 ; i < typedArr.Length ; i++ )
         {
            objects[ i ].TryCastTo<T>( out var castedObj );
            typedArr[ i ] = castedObj;
         }

         return typedArr;
      }

#if MANAGED
      private static bool IsSceneObject( UnityEngine.Object obj )
      {
         if( obj is GameObject go )
         {
            return go.scene.IsValid();
         }
         if( obj is Component component )
         {
            return component.gameObject.scene.IsValid();
         }
         return true;
      }
#endif

      public static Texture2D CreateEmptyTexture2D( TextureFormat originalTextureFormat )
      {
         TextureFormat newFormat;
         switch( originalTextureFormat )
         {
            case TextureFormat.RGB24:
               newFormat = TextureFormat.RGB24;
               break;
            case TextureFormat.DXT1:
               newFormat = TextureFormat.RGB24;
               break;
            case TextureFormat.DXT5:
               newFormat = TextureFormat.ARGB32;
               break;
            default:
               newFormat = TextureFormat.ARGB32;
               break;
         }

         return new Texture2D( 2, 2, newFormat, false );
      }
   }
}
