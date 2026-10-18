# Developer Guide

This guide is inherited from the upstream XUnity.AutoTranslator README. It is kept out of the main README to keep the fork documentation user-facing.

## Integrating with Auto Translator
*NOTE: Everything below this point requires programming knowledge!*

### Implementing a plugin that can query translations
As a mod author, you may want to query translations from the plugin. This easily done, take a look at the example below.

```C#
public class MyPlugin : XPluginBase
{
   public void Start()
   {
      var untranslatedText = "お前はもう死んでいる！";

      // EXAMPLE 1) Query cache, and if it cannot be found in cache query the user-selected translation endpoint
      AutoTranslator.Default.TranslateAsync( untranslatedText, result =>
      {
         if( result.Succeeded )
         {
            var translatedText = result.TranslatedText;
         }
         else
         {
            var errorMessage = result.ErrorMessage;
         }
      } );

      // EXAMPLE 2) Query cache only
      if( AutoTranslator.Default.TryTranslate( untranslatedText, out string translation ) )
      {
         // success
      }
      else
      {
         // failure
      }
   }
}
```

This requires version 3.7.0 or later!

### Implementing a component that the Auto Translator should not interfere with
As a mod author, you might not want the Auto Translator to interfere with your mods UI. If this is the case there's two ways to tell Auto Translator not to perform any translation:
 * If your UI is based on GameObjects, you can simply name your GameObjects containing the text element (for example Text class) to something that contains the string "XUAIGNORE". The Auto Translator will check for this and ignore components that contains the string. If you name the GameObject with "XUAIGNORETREE" instead, Auto Translator will also ignore components on all child GameObjects. Warning: This is checked on component creation. If you rename the GameObject after the component has been instantiated, it will not be ignored.
 * If your UI is based on IMGUI, the above approach is not possible, because there are no GameObject. In that case you can do the following instead:

```C#
public class MyPlugin : XPluginBase
{
   private GameObject _xua;
   private bool _lookedForXua;

   public void OnGUI()
   {
      // make sure we only do this lookup once, as it otherwise may be detrimental to performance!
      // also: do not attempt to do this in the Awake method or similar of your plugin, as your plugin may be instantiated before the auto translator!
      if(!_lookedForXua)
      {
         _lookedForXua = true;
         _xua = GameObject.Find( "___XUnityAutoTranslator" );
      }

      // try-finally block is important to make sure you re-enable the plugin
      try
      {
         _xua?.SendMessage("DisableAutoTranslator");

         // do your GUI things here
         GUILayout.Button( "こんにちは！" );
      }
      finally
      {
         _xua?.SendMessage("EnableAutoTranslator");
      }
   }
}
```

This requires version 2.15.0 or later!

## Implementing a Translator
Since version 3.0.0, you can now also implement your own translators.

In order to do so, all you have to do is implement the following interface, build the assembly and place the generated DLL in the `Translators` folder.

```C#
/// <summary>
/// The interface that must be implemented by a translator.
/// </summary>
public interface ITranslateEndpoint
{
   /// <summary>
   /// Gets the id of the ITranslateEndpoint that is used as a configuration parameter.
   /// </summary>
   string Id { get; }

   /// <summary>
   /// Gets a friendly name that can be displayed to the user representing the plugin.
   /// </summary>
   string FriendlyName { get; }

   /// <summary>
   /// Gets the maximum concurrency for the endpoint. This specifies how many times "Translate"
   /// can be called before it returns.
   /// </summary>
   int MaxConcurrency { get; }

   /// <summary>
   /// Gets the maximum number of translations that can be served per translation request.
   /// </summary>
   int MaxTranslationsPerRequest { get; }

   /// <summary>
   /// Called during initialization. Use this to initialize plugin or throw exception if impossible.
   /// </summary>
   void Initialize( IInitializationContext context );

   /// <summary>
   /// Attempt to translated the provided untranslated text. Will be used in a "coroutine",
   /// so it can be implemented in an asynchronous fashion.
   /// </summary>
   IEnumerator Translate( ITranslationContext context );
}
```

Often an implementation of this interface will access an external web service. If this is the case, you do not need to implement the entire interface yourself. Instead you can rely on a base class in the `XUnity.AutoTranslator.Plugin.Core` assembly. But more on this later.

### Important Notes on Implementing a Translator based on an Online Service
Whenever you implement a translator based on an online service, it is important to not use it in an abusive way. For example by:
 * Establishing a large number of connections to it
 * Performing web scraping instead of using an available API
 * Making concurrent requests towards it
 * *This is especially important if the service is not authenticated*

With that in mind, consider the following:
 * The `WWW` class in Unity establishes a new TCP connection on each request you make, making it extremely poor at this kind of job. Especially if SSL (https) is involved because it has to do the entire handshake procedure each time. Yuck.
 * The `UnityWebRequest` class in Unity does not exist in most games, because the authors use an old engine, so it is not a good choice either.
 * The `WebClient` class from .NET is capable of using persistent connections (it does so by default), but has its own problems with SSL. The version of Mono used in most Unity games rejects all certificates by default making all HTTPS connections fail. This, however, can be remedied during the initialization phase of the translator (see examples below). Another shortcoming of this API is the fact that the runtime will never release the TCP connections it has used until the process ends. The API also integrates terribly with Unity because callbacks return on a background thread.
 * The `WebRequest` class from .NET is essentially the same as WebClient.
 * The `HttpClient` class from .NET is also unlikely to exist in most Unity games.

None of these are therefore an ideal solution.

To remedy this, the plugin implements a class `XUnityWebClient`, which is based on Mono's version of WebClient. However, it adds the following features:
 * Enables integration with Unity by returning result classes that can be 'yielded'.
 * Properly closes connections that has not been used for 50 seconds.

I recommend using this class, or in case that cannot be used, falling back to the .NET 'WebClient'.

### How-To
Follow these steps:
 1. Download XUnity.AutoTranslator-Developer-{VERSION}.zip from [releases](../../../releases)
 2. Start a new project (.NET 3.5) in Visual Studio 2017 or later. I recommend using the same name for your assembly/project as the "Id" you are going to use in your interface implementation. This makes it easier for users to know how to configure your translator
    * I recommend using the "Class Library (.NET Standard)" and simply editing the generated .csproj file to use 'net35' instead of 'netstandard2.0'. This generates much cleaner .csproj files.
 3. Add a reference to the XUnity.AutoTranslator.Plugin.Core.dll that you downloaded in step 1
 4. You do not need to directly reference the UnityEngine.dll assembly. This is good, because you do not need to worry about which version of Unity is used.
    * If you do need a reference to this assembly (because you need functionality from it) consider using an old version of it (if `UnityEngine.CoreModule.dll` exists in the Managed folder, it is not an old version!)
 5. Create a new class that either:
    * Implements the `ITranslateEndpoint` interface
    * Inherits from the `HttpEndpoint` class
    * Inherits from the `WwwEndpoint` class
    * Inherits from the `ExtProtocolEndpoint` class

Here's an example that simply reverses the text and also reads some configuration from the configuration file the plugin uses:

```C#
public class ReverseTranslatorEndpoint : ITranslateEndpoint
{
   private bool _myConfig;

   public string Id => "ReverseTranslator";

   public string FriendlyName => "Reverser";

   public int MaxConcurrency => 50;

   public int MaxTranslationsPerRequest => 1;

   public void Initialize( IInitializationContext context )
   {
      _myConfig = context.GetOrCreateSetting( "Reverser", "MyConfig", true );
   }

   public IEnumerator Translate( ITranslationContext context )
   {
      var reversedText = new string( context.UntranslatedText.Reverse().ToArray() );
      context.Complete( reversedText );

      return null;
   }
}
```

Arguably, this is not a particularly interesting example, but it illustrates the basic principles of what must be done in order to implement a Translator.

Let's take a look at a more advanced example that accesses the web:

```C#
internal class YandexTranslateEndpoint : HttpEndpoint
{
   private static readonly HashSet<string> SupportedLanguages = new HashSet<string> { "az", "sq", "am", "en", "ar", "hy", "af", "eu", "ba", "be", "bn", "my", "bg", "bs", "cy", "hu", "vi", "ht", "gl", "nl", "mrj", "el", "ka", "gu", "da", "he", "yi", "id", "ga", "it", "is", "es", "kk", "kn", "ca", "ky", "zh", "ko", "xh", "km", "lo", "la", "lv", "lt", "lb", "mg", "ms", "ml", "mt", "mk", "mi", "mr", "mhr", "mn", "de", "ne", "no", "pa", "pap", "fa", "pl", "pt", "ro", "ru", "ceb", "sr", "si", "sk", "sl", "sw", "su", "tg", "th", "tl", "ta", "tt", "te", "tr", "udm", "uz", "uk", "ur", "fi", "fr", "hi", "hr", "cs", "sv", "gd", "et", "eo", "jv", "ja" };
   private static readonly string HttpsServicePointTemplateUrl = "https://translate.yandex.net/api/v1.5/tr.json/translate?key={3}&text={2}&lang={0}-{1}&format=plain";

   private string _key;

   public override string Id => "YandexTranslate";

   public override string FriendlyName => "Yandex Translate";

   public override void Initialize( IInitializationContext context )
   {
      _key = context.GetOrCreateSetting( "Yandex", "YandexAPIKey", "" );
      context.DisableCertificateChecksFor( "translate.yandex.net" );

      // if the plugin cannot be enabled, simply throw so the user cannot select the plugin
      if( string.IsNullOrEmpty( _key ) ) throw new Exception( "The YandexTranslate endpoint requires an API key which has not been provided." );
      if( !SupportedLanguages.Contains( context.SourceLanguage ) ) throw new Exception( $"The source language '{context.SourceLanguage}' is not supported." );
      if( !SupportedLanguages.Contains( context.DestinationLanguage ) ) throw new Exception( $"The destination language '{context.DestinationLanguage}' is not supported." );
   }

   public override void OnCreateRequest( IHttpRequestCreationContext context )
   {
      var request = new XUnityWebRequest(
         string.Format(
            HttpsServicePointTemplateUrl,
            context.SourceLanguage,
            context.DestinationLanguage,
            WwwHelper.EscapeUrl( context.UntranslatedText ),
            _key ) );
         
      request.Headers[ HttpRequestHeader.Accept ] = "*/*";
      request.Headers[ HttpRequestHeader.AcceptCharset ] = "UTF-8";

      context.Complete( request );
   }

   public override void OnExtractTranslation( IHttpTranslationExtractionContext context )
   {
      var data = context.Response.Data;
      var obj = JSON.Parse( data );

      var code = obj.AsObject[ "code" ].ToString();
      if( code != "200" ) context.Fail( "Received bad response code: " + code );

      var token = obj.AsObject[ "text" ].ToString();
      var translation = JsonHelper.Unescape( token.Substring( 2, token.Length - 4 ) );

      if( string.IsNullOrEmpty( translation ) ) context.Fail( "Received no translation." );

      context.Complete( translation );
   }
}
```

This plugin extends from `HttpEndpoint`. Let's look at the three methods it overrides:
 * `Initialize` is used to read the API key the user has configured. In addition it calls `context.DisableCertificateChecksFor( "translate.yandex.net" )` in order to disable the certificate check for this specific hostname. If this is neglected, SSL will fail in most versions of Unity. Finally, it throws an exception if the plugin cannot be used with the specified configuration.
 * `OnCreateRequest` is used to construct the `XUnityWebRequest` object that will be sent to the external endpoint. The call to `context.Complete( request )` specifies the request to use.
 * `OnExtractTranslation` is used to extract the text from the response returned from the web server.

As you can see, the `XUnityWebClient` class is not even used. We simply specify a request object that the `HttpEndpoint` will use internally to perform the request.

After implementing the class, simply build the project and place the generated DLL file in the "Translators" directory of the plugin folder. That's it.

For more examples of implementations, you can simply take a look at this projects source code.

**NOTE**: If you implement a class based on the `HttpEndpoint` and you get an error where the web request is never completed, then it is likely due to the web server requiring Tls1.2. Unity-mono has issues with this spec and it will cause the request to lock up forever. The only solutions to this for now are:
 * Disable SSL, if you can. There are many situations where it is simply not possible to do this because the web server will simply redirect back to the HTTPS endpoint.
 * Use the `WwwEndpoint` instead. I highly advise against this though, unless it is an authenticated endpoint.

Another way to implement a translator is to implement the `ExtProtocolEndpoint` class. This can be used to delegate the actual translation logic to an external process. Currently there is no documentation on this, but you can take a look at the LEC implementation, which uses it.

If instead, you use the interface directly, it is also possible to extend from MonoBehaviour to get access to all the normal lifecycle callbacks of Unity components.

## Implementing a Resource Redirector
The resource director allows you to modify resources loaded through the `Resources` and `AssetBundle` API as they are being loaded by the game.

The following API surface is made available by the Resource Redirector:

```C#
/// <summary>
/// Entrypoint to the resource redirection API.
/// </summary>
public static class ResourceRedirection
{
    /// <summary>
    /// Gets or sets a bool indicating if the resource redirector
    /// should log all loaded resources/assets to the console.
    /// </summary>
    public static bool LogAllLoadedResources { get; set; }

    /// <summary>
    /// Register an AssetLoading hook (prefix to loading an asset from an asset bundle).
    /// </summary>
    /// <param name="priority">The priority of the callback, the higher the sooner it will be called.</param>
    /// <param name="action">The callback.</param>
    public static void RegisterAssetLoadingHook( int priority, Action<AssetLoadingContext> action );

    /// <summary>
    /// Unregister an AssetLoading hook (prefix to loading an asset from an asset bundle).
    /// </summary>
    /// <param name="action">The callback.</param>
    public static void UnregisterAssetLoadingHook( Action<AssetLoadingContext> action );

    /// <summary>
    /// Register an AsyncAssetLoading hook (prefix to loading an asset from an asset bundle asynchronously).
    /// </summary>
    /// <param name="priority">The priority of the callback, the higher the sooner it will be called.</param>
    /// <param name="action">The callback.</param>
    public static void RegisterAsyncAssetLoadingHook( int priority, Action<AsyncAssetLoadingContext> action );

    /// <summary>
    /// Unregister an AsyncAssetLoading hook (prefix to loading an asset from an asset bundle asynchronously).
    /// </summary>
    /// <param name="action">The callback.</param>
    public static void UnregisterAsyncAssetLoadingHook( Action<AsyncAssetLoadingContext> action );

    /// <summary>
    /// Register an AsyncAssetLoading hook and AssetLoading hook (prefix to loading an asset from an asset bundle synchronously/asynchronously).
    /// </summary>
    /// <param name="priority">The priority of the callback, the higher the sooner it will be called.</param>
    /// <param name="action">The callback.</param>
    public static void RegisterAsyncAndSyncAssetLoadingHook( int priority, Action<IAssetLoadingContext> action );

    /// <summary>
    /// Unregister an AsyncAssetLoading hook and AssetLoading hook (prefix to loading an asset from an asset bundle synchronously/asynchronously).
    /// </summary>
    /// <param name="action">The callback.</param>
    public static void UnregisterAsyncAndSyncAssetLoadingHook( Action<IAssetLoadingContext> action );

    /// <summary>
    /// Register an AssetLoaded hook (postfix to loading an asset from an asset bundle (both synchronous and asynchronous)).
    /// </summary>
    /// <param name="behaviour">The behaviour of the callback.</param>
    /// <param name="priority">The priority of the callback, the higher the sooner it will be called.</param>
    /// <param name="action">The callback.</param>
    public static void RegisterAssetLoadedHook( HookBehaviour behaviour, int priority, Action<AssetLoadedContext> action );

    /// <summary>
    /// Unregister an AssetLoaded hook (postfix to loading an asset from an asset bundle (both synchronous and asynchronous)).
    /// </summary>
    /// <param name="action">The callback.</param>
    public static void UnregisterAssetLoadedHook( Action<AssetLoadedContext> action );

    /// <summary>
    /// Register an AssetBundleLoading hook (prefix to loading an asset bundle synchronously).
    /// </summary>
    /// <param name="priority">The priority of the callback, the higher the sooner it will be called.</param>
    /// <param name="action">The callback.</param>
    public static void RegisterAssetBundleLoadingHook( int priority, Action<AssetBundleLoadingContext> action );

    /// <summary>
    /// Unregister an AssetBundleLoading hook (prefix to loading an asset bundle synchronously).
    /// </summary>
    /// <param name="action">The callback.</param>
    public static void UnregisterAssetBundleLoadingHook( Action<AssetBundleLoadingContext> action );

    /// <summary>
    /// Register an AsyncAssetBundleLoading hook (prefix to loading an asset bundle asynchronously).
    /// </summary>
    /// <param name="priority">The priority of the callback, the higher the sooner it will be called.</param>
    /// <param name="action">The callback.</param>
    public static void RegisterAsyncAssetBundleLoadingHook( int priority, Action<AsyncAssetBundleLoadingContext> action );

    /// <summary>
    /// Unregister an AsyncAssetBundleLoading hook (prefix to loading an asset bundle asynchronously).
    /// </summary>
    /// <param name="action">The callback.</param>
    public static void UnregisterAsyncAssetBundleLoadingHook( Action<AsyncAssetBundleLoadingContext> action );

    /// <summary>
    /// Register an AsyncAssetBundleLoading hook and AssetBundleLoading hook (prefix to loading an asset bundle synchronously/asynchronously).
    /// </summary>
    /// <param name="priority">The priority of the callback, the higher the sooner it will be called.</param>
    /// <param name="action">The callback.</param>
    public static void RegisterAsyncAndSyncAssetBundleLoadingHook( int priority, Action<IAssetBundleLoadingContext> action );

    /// <summary>
    /// Unregister an AsyncAssetBundleLoading hook and AssetBundleLoading hook (prefix to loading an asset bundle synchronously/asynchronously).
    /// </summary>
    /// <param name="action">The callback.</param>
    public static void UnregisterAsyncAndSyncAssetBundleLoadingHook( Action<IAssetBundleLoadingContext> action );

    /// <summary>
    /// Register a ResourceLoaded hook (postfix to loading a resource from the Resources API (both synchronous and asynchronous)).
    /// </summary>
    /// <param name="behaviour">The behaviour of the callback.</param>
    /// <param name="priority">The priority of the callback, the higher the sooner it will be called.</param>
    /// <param name="action">The callback.</param>
    public static void RegisterResourceLoadedHook( HookBehaviour behaviour, int priority, Action<ResourceLoadedContext> action );

    /// <summary>
    /// Unregister a ResourceLoaded hook (postfix to loading a resource from the Resources API (both synchronous and asynchronous)).
    /// </summary>
    /// <param name="action">The callback.</param>
    public static void UnregisterResourceLoadedHook( Action<ResourceLoadedContext> action );

    /// <summary>
    /// Enables experimental hooks that allows returning an Asset instead of a Request from async prefix
    /// asset load operations.
    /// </summary>
    public static void EnableSyncOverAsyncAssetLoads();

    /// <summary>
    /// Disables all recursive behaviour in the plugin. This means that trying to load an asset
    /// using the hooked APIs will not trigger a callback back into the callback chain. This makes
    /// setting the correct priorities on callbacks much more important.
    ///
    /// This method should not be called lightly. It should not be something a single plugin randomly
    /// decides to call, but rather decision for how to use the ResourceRedirection API on a game-wide basis.
    /// </summary>
    public static void DisableRecursionPermanently();

    /// <summary>
    /// Creates an asset bundle hook that attempts to load asset bundles in the emulation directory
    /// over the default asset bundles if they exist.
    /// </summary>
    /// <param name="hookPriority">Priority of the hook.</param>
    /// <param name="emulationDirectory">The directory to look for the asset bundles in.</param>
    public static void EnableEmulateAssetBundles( int hookPriority, string emulationDirectory );

    /// <summary>
    /// Disable a previously enabled asset bundle emulation.
    /// </summary>
    public static void DisableEmulateAssetBundles();

    /// <summary>
    /// Creates an asset bundle hook that redirects asset bundles loads to an empty
    /// asset bundle if the file that is being loaded does not exist.
    /// </summary>
    /// <param name="hookPriority">Priority of the hook.</param>
    public static void EnableRedirectMissingAssetBundlesToEmptyAssetBundle( int hookPriority );

    /// <summary>
    /// Disable a previously enabled redirect missing asset bundles to empty asset bundle.
    /// </summary>
    public static void DisableRedirectMissingAssetBundlesToEmptyAssetBundle();
}

```

Let's attach some comments to this API.

### Asset Loading/Loaded Methods
The resource redirector comes with both postfix and prefix callbacks when loading an asset from the `AssetBundle` API.

The event callback chain looks like this `[AssetLoading / AsyncAssetLoading hooks] => [Original Method] => [AssetLoaded hooks]`. The `AssetLoaded` event handles postfixes for both synchronous and asynchronous loading of assets.

#### Asset Loading Methods
The methods `RegisterAssetLoadingHook( HookBehaviour behaviour, Action<AssetLoadingContext> action )` and `RegisterAsyncAssetLoadingHook( int priority, Action<AsyncAssetLoadingContext> action )` hooks into the `AssetBundle` API when loading assets.

These methods register prefix callbacks, which means the assets themselves won't be loaded yet when they are called.

The callbacks take the types `AssetLoadingContext` and `AsyncAssetLoadingContext` as an argument, respectively. Let's take a look at their definitions:

```C#
/// <summary>
/// The operation context surrounding the AssetLoading hook (synchronous).
/// </summary>
public class AssetLoadingContext : IAssetLoadingContext
{
    /// <summary>
    /// Gets the original path the asset bundle was loaded with.
    /// </summary>
    /// <returns>The unmodified, original path the asset bundle was loaded with.</returns>
    public string GetAssetBundlePath();

    /// <summary>
    /// Gets the normalized path to the asset bundle that is:
    ///  * Relative to the current directory
    ///  * Lower-casing
    ///  * Uses '\' as separators.
    /// </summary>
    /// <returns></returns>
    public string GetNormalizedAssetBundlePath();

    /// <summary>
    /// Indicate your work is done and if any other hooks to this asset/resource load should be called.
    /// </summary>
    /// <param name="skipRemainingPrefixes">Indicate if the remaining prefixes should be skipped.</param>
    /// <param name="skipOriginalCall">Indicate if the original call should be skipped. If you set the asset, you likely want to set this to true.</param>
    /// <param name="skipAllPostfixes">Indicate if the postfixes should be skipped.</param>
    public void Complete( bool skipRemainingPrefixes = true, bool? skipOriginalCall = true, bool? skipAllPostfixes = true );

    /// <summary>
    /// Disables recursive calls if you make an asset/asset bundle load call
    /// from within your callback. If you want to prevent recursion this should
    /// be called before you load the asset/asset bundle.
    /// </summary>
    public void DisableRecursion();

    /// <summary>
    /// Gets the original parameters the asset load call was called with.
    /// </summary>
    public AssetLoadingParameters Parameters { get; }

    /// <summary>
    /// Gets the AssetBundle associated with the loaded assets.
    /// </summary>
    public AssetBundle Bundle { get; }

    /// <summary>
    /// Gets or sets the loaded assets.
    ///
    /// Consider using this if the load type is 'LoadByType' or 'LoadNamedWithSubAssets'.
    /// </summary>
    public UnityEngine.Object[] Assets { get; set; }

    /// <summary>
    /// Gets or sets the loaded assets. This is simply equal to the first index of the Assets property, with some
    /// additional null guards to prevent NullReferenceExceptions when using it.
    /// </summary>
    public UnityEngine.Object Asset { get; set; }
}

/// <summary>
/// The operation context surrounding the AsyncAssetLoading hook (asynchronous).
/// </summary>
public class AsyncAssetLoadingContext : IAssetLoadingContext
{
    /// <summary>
    /// Gets the original path the asset bundle was loaded with.
    /// </summary>
    /// <returns>The unmodified, original path the asset bundle was loaded with.</returns>
    public string GetAssetBundlePath();

    /// <summary>
    /// Gets the normalized path to the asset bundle that is:
    ///  * Relative to the current directory
    ///  * Lower-casing
    ///  * Uses '\' as separators.
    /// </summary>
    /// <returns></returns>
    public string GetNormalizedAssetBundlePath();

    /// <summary>
    /// Indicate your work is done and if any other hooks to this asset/resource load should be called.
    /// </summary>
    /// <param name="skipRemainingPrefixes">Indicate if the remaining prefixes should be skipped.</param>
    /// <param name="skipOriginalCall">Indicate if the original call should be skipped. If you set the asset, you likely want to set this to true.</param>
    /// <param name="skipAllPostfixes">Indicate if the postfixes should be skipped.</param>
    public void Complete( bool skipRemainingPrefixes = true, bool? skipOriginalCall = true, bool? skipAllPostfixes = true );

    /// <summary>
    /// Disables recursive calls if you make an asset/asset bundle load call
    /// from within your callback. If you want to prevent recursion this should
    /// be called before you load the asset/asset bundle.
    /// </summary>
    public void DisableRecursion();

    /// <summary>
    /// Gets the original parameters the asset load call was called with.
    /// </summary>
    public AssetLoadingParameters Parameters { get; }

    /// <summary>
    /// Gets the AssetBundle associated with the loaded assets.
    /// </summary>
    public AssetBundle Bundle { get; }

    /// <summary>
    /// Gets or sets the AssetBundleRequest used to load assets.
    /// </summary>
    public AssetBundleRequest Request { get; set; }

    /// <summary>
    /// Gets or sets the loaded assets.
    ///
    /// Consider using this if the load type is 'LoadByType' or 'LoadNamedWithSubAssets'.
    /// </summary>
    UnityEngine.Object[] Assets { get; set; }

    /// <summary>
    /// Gets or sets the loaded assets. This is simply equal to the first index of the Assets property, with some
    /// additional null guards to prevent NullReferenceExceptions when using it.
    /// </summary>
    UnityEngine.Object Asset { get; set; }

    /// <summary>
    /// Gets or sets how this load operation should be resolved.
    /// Setting the Asset/Assets/Request property will automatically update this value.
    /// </summary>
    public AsyncAssetLoadingResolve ResolveType { get; set; }
}
```

The only difference between these two contexts is that one has an `Asset/Assets` property you can set, while the other has a `Request` property you can set.

Now if you actually paid attention to what you were reading(!?), you would notice that both of the above context objects has an `Asset/Assets` property that can be set.

Under normal circumstances, however, you cannot use the `Assets/Asset` property on the `AsyncAssetLoadingContext`. In order to be able to use these, you must first call `ResourceRedirection.EnableSyncOverAsyncAssetLoads` once during your initialization logic. This will allow you to set the asset directly so you don't have to go through the standard `AssetBundle` API to obtain a request object.

It is, however, recommended that if you can that you set the `Request` property instead of the `Assets/Asset` property as that will keep the operation asynchronous and not block the game while the asset is being loaded.

If you can handle the loading of the asset remember to call the `Complete` method to indicate your intentions regarding:
 * Whether the rest of the prefixes registered should be skipped.
 * Whether the original method should be skipped.
 * Whether all the postfixes should be skipped.

An important point to make here is that there are both an `Asset` and an `Assets` property on the context object. These can be used interchangeably, but an array will only ever be used if the following condition applies:
 * The `LoadType` in the `Parameters` property is `LoadByType` or `LoadNamedWithSubAssets`, which are the only types of resource loading that may return multiple resources.

Finally, if we take a look at the `Parameters` property of the context object, we will find the following definition:

```C#
/// <summary>
/// Class representing the original parameters of the load call.
/// </summary>
public class AssetLoadingParameters
{
    /// <summary>
    /// Gets or sets the name of the asset being loaded. Will be null if loaded through 'LoadMainAsset' or 'LoadByType'.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the type that passed to the asset load call.
    /// </summary>
    public Type Type { get; set; }

    /// <summary>
    /// Gets the type of call that loaded this asset. If 'LoadByType' or 'LoadNamedWithSubAssets' is specified
    /// multiple assets may be returned if subscribed as 'OneCallbackPerLoadCall'.
    /// </summary>
    public AssetLoadType LoadType { get; }
}

/// <summary>
/// Enum representing the different ways an asset may be loaded.
/// </summary>
public enum AssetLoadType
{
    /// <summary>
    /// Indicates that this asset has been loaded as the 'mainAsset' in the AssetBundle API.
    /// </summary>
    LoadMainAsset,

    /// <summary>
    /// Indicates that this call is loading all assets of a specific type in an AssetBundle API.
    /// </summary>
    LoadByType,

    /// <summary>
    /// Indicates that this call is loading a specific named asset in the AssetBundle API.
    /// </summary>
    LoadNamed,

    /// <summary>
    /// Indicates that this call is loading a specific named asset and all those below it in the AssetBundle API.
    /// </summary>
    LoadNamedWithSubAssets
}
```

Another way to change the result of the asset load operation is to change the value of the `Name` and `Type` properties in the `Parameters` property. If you do this, you likely will not want to call the Complete method, as you will want the original method to still be called.

An important additional way to subscribe to the prefix asset loading operations are through the method `RegisterAsyncAndSyncAssetLoadingHook( int priority, Action<IAssetLoadingContext> action )`. This method will handle both async and sync asset loading operations. The `IAssetLoadingContext` is an interface implemented by both the `AssetLoadingContext` and `AsyncAssetLoadingContext`.

Do note, that if you want to use this method you must first call the method `EnableSyncOverAsyncAssetLoads()` to enable the hooks required for this to work.

#### Asset Loaded Methods
The method `RegisterAssetLoadedHook( HookBehaviour behaviour, Action<AssetLoadedContext> action )` hooks into the `AssetBundle` API in the UnityEngine. Any time an asset is loaded through this API a callback is sent to these hooks.

This API is a postfix hook to the `AssetBundle` API, which means that it is first called once the original asset has already been loaded, but is still replaceable.

The `AssetLoadedContext` class has the following definition:

```C#
/// <summary>
/// The operation context surrounding the AssetLoaded hook.
/// </summary>
public class AssetLoadedContext : IAssetOrResourceLoadedContext
{
    /// <summary>
    /// Gets a bool indicating if this resource has already been redirected before.
    /// </summary>
    public bool HasReferenceBeenRedirectedBefore( UnityEngine.Object asset );

    /// <summary>
    /// Gets a file system path for the specific asset that should be unique.
    /// </summary>
    /// <param name="asset"></param>
    /// <returns></returns>
    public string GetUniqueFileSystemAssetPath( UnityEngine.Object asset );

    /// <summary>
    /// Gets the original path the asset bundle was loaded with.
    /// </summary>
    /// <returns>The unmodified, original path the asset bundle was loaded with.</returns>
    public string GetAssetBundlePath();

    /// <summary>
    /// Gets the normalized path to the asset bundle that is:
    ///  * Relative to the current directory
    ///  * Lower-casing
    ///  * Uses '\' as separators.
    /// </summary>
    /// <returns></returns>
    public string GetNormalizedAssetBundlePath();

    /// <summary>
    /// Indicate your work is done and if any other hooks to this asset/resource load should be called.
    /// </summary>
    /// <param name="skipRemainingPostfixes">Indicate if any other hooks should be skipped.</param>
    public void Complete( bool skipRemainingPostfixes = true );

    /// <summary>
    /// Disables recursive calls if you make an asset/asset bundle load call
    /// from within your callback. If you want to prevent recursion this should
    /// be called before you load the asset/asset bundle.
    /// </summary>
    public void DisableRecursion();

    /// <summary>
    /// Gets the original parameters the asset load call was called with.
    /// </summary>
    public AssetLoadedParameters Parameters { get; }

    /// <summary>
    /// Gets the AssetBundle associated with the loaded assets.
    /// </summary>
    public AssetBundle Bundle { get; }

    /// <summary>
    /// Gets the loaded assets. Override individual indices to change the asset reference that will be loaded.
    ///
    /// Consider using this if the load type is 'LoadByType' or 'LoadNamedWithSubAssets' and you subscribed with 'OneCallbackPerLoadCall'.
    /// </summary>
    public UnityEngine.Object[] Assets { get; set; }

    /// <summary>
    /// Gets the loaded asset. This is simply equal to the first index of the Assets property, with some
    /// additional null guards to prevent NullReferenceExceptions when using it.
    /// </summary>
    public UnityEngine.Object Asset { get; set; }
}
```

The HookBehaviour is an enum with the following definition:

```C#
/// <summary>
/// Enum indicating how the resource redirector should treat the callback.
/// </summary>
public enum HookBehaviour
{
    /// <summary>
    /// Specifies that exactly one callback should be received per call to asset/resource load method.
    /// </summary>
    OneCallbackPerLoadCall = 1,

    /// <summary>
    /// Specifies that exactly one callback should be received per loaded asset/resources. This means
    /// that the 'Asset' property should be used over the 'Assets' property on the context object.
    /// Do note that when using this option, if no resources are returned by a load call, no callbacks
    /// will be received.
    /// </summary>
    OneCallbackPerResourceLoaded = 2
}
```

An important point to make here is that there are both an `Asset` and an `Assets` property on the context object. These can be used interchangeably, but an array will only ever be used if the following two conditions apply:
 * You've subscribed with `OneCallbackPerLoadCall`.
 * The `LoadType` in the `Parameters` property is `LoadByType` or `LoadNamedWithSubAssets`, which are the only types of resource loading that may return multiple resources.

In relation to this, it is worth mentioning that if a call to load assets returns 0 assets, you will not receive any callbacks if you subscribe through `OneCallbackPerResourceLoaded` where as if you subscribe through `OneCallbackPerLoadCall` you would still get your one callback.

If you update or replace the asset being loaded remember to call the `Complete` method to indicate your intentions regarding:
 * Whether the remaining postfixes should be called.

In addition, if we take a look at the `Parameters` property of the context object, we will find the following definition:

```C#
/// <summary>
/// Class representing the original parameters of the load call.
/// </summary>
public class AssetLoadedParameters
{
    /// <summary>
    /// Gets the name of the asset being loaded. Will be null if loaded through 'LoadMainAsset' or 'LoadByType'.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the type that passed to the asset load call.
    /// </summary>
    public Type Type { get; }

    /// <summary>
    /// Gets the type of call that loaded this asset. If 'LoadByType' or 'LoadNamedWithSubAssets' is specified
    /// multiple assets may be returned if subscribed as 'OneCallbackPerLoadCall'.
    /// </summary>
    public AssetLoadType LoadType { get; }
}

/// <summary>
/// Enum representing the different ways an asset may be loaded.
/// </summary>
public enum AssetLoadType
{
    /// <summary>
    /// Indicates that this asset has been loaded as the 'mainAsset' in the AssetBundle API.
    /// </summary>
    LoadMainAsset,

    /// <summary>
    /// Indicates that this call is loading all assets of a specific type in an AssetBundle API.
    /// </summary>
    LoadByType,

    /// <summary>
    /// Indicates that this call is loading a specific named asset in the AssetBundle API.
    /// </summary>
    LoadNamed,

    /// <summary>
    /// Indicates that this call is loading a specific named asset and all those below it in the AssetBundle API.
    /// </summary>
    LoadNamedWithSubAssets
}
```

It is also worth mentioning that these hooks handle both synchronous and asynchronous loading of assets.

Hooks subscribed through hook behaviour `OneCallbackPerLoadCall` will be called before hooks with the behaviour `OneCallbackPerResourceLoaded`.

### Resource Load Methods
The resource redirector comes only with postfix callbacks when loading an asset from the `Resources` API.

The event callback chain looks like this `[Original Method] => [ResourceLoaded hooks]`. The `ResourceLoaded` event handles postfixes for both synchronous and asynchronous loading of assets.

#### Resource Loaded Methods
The method `RegisterResourceLoadedHook( HookBehaviour behaviour, Action<ResourceLoadedContext> action )` hooks into the `Resources` API in the UnityEngine. Any time a resource is loaded through this API a callback is sent to these hooks.

This API is a postfix hook to the `Resources` API, which means that it is first called once the original asset has already been loaded, but is still replaceable.

The `ResourceLoadedContext` class has the following definition:

```C#
/// <summary>
/// The operation context surrounding the ResourceLoaded hook.
/// </summary>
public class ResourceLoadedContext : IAssetOrResourceLoadedContext
{
    /// <summary>
    /// Gets a bool indicating if this resource has already been redirected before.
    /// </summary>
    public bool HasReferenceBeenRedirectedBefore( UnityEngine.Object asset );

    /// <summary>
    /// Gets a file system path for the specific asset that should be unique.
    /// </summary>
    /// <param name="asset"></param>
    /// <returns></returns>
    public string GetUniqueFileSystemAssetPath( UnityEngine.Object asset );

    /// <summary>
    /// Indicate your work is done and if any other hooks to this asset/resource load should be called.
    /// </summary>
    /// <param name="skipRemainingPostfixes">Indicate if any other hooks should be skipped.</param>
    public void Complete( bool skipRemainingPostfixes = true );

    /// <summary>
    /// Disables recursive calls if you make an asset/asset bundle load call
    /// from within your callback. If you want to prevent recursion this should
    /// be called before you load the asset/asset bundle.
    /// </summary>
    public void DisableRecursion();

    /// <summary>
    /// Gets the original parameters the asset load call was called with.
    /// </summary>
    public ResourceLoadParameters Parameters { get; }

    /// <summary>
    /// Gets the loaded assets. Override individual indices to change the asset reference that will be loaded.
    ///
    /// Consider using this if the load type is 'LoadByType' and you subscribed with 'OneCallbackPerLoadCall'.
    /// </summary>
    public UnityEngine.Object[] Assets { get; set; }

    /// <summary>
    /// Gets the loaded asset. This is simply equal to the first index of the Assets property, with some
    /// additional null guards to prevent NullReferenceExceptions when using it.
    /// </summary>
    public UnityEngine.Object Asset { get; set; }
}
```

The HookBehaviour is an enum with the following definition:

```C#
/// <summary>
/// Enum indicating how the resource redirector should treat the callback.
/// </summary>
public enum HookBehaviour
{
    /// <summary>
    /// Specifies that exactly one callback should be received per call to asset/resource load method.
    /// </summary>
    OneCallbackPerLoadCall = 1,

    /// <summary>
    /// Specifies that exactly one callback should be received per loaded asset/resources. This means
    /// that the 'Asset' property should be used over the 'Assets' property on the context object.
    /// Do note that when using this option, if no resources are returned by a load call, no callbacks
    /// will be received.
    /// </summary>
    OneCallbackPerResourceLoaded = 2
}
```

An important point to make here is that there are both an `Asset` and an `Assets` property on the context object. These can be used interchangeably, but an array will only ever be used if the following two conditions apply:
 * You've subscribed with `OneCallbackPerLoadCall`.
 * The `LoadType` in the `Parameters` property is `LoadByType`, which is the only type of resource loading that may return multiple resources.

In relation to this, it is worth mentioning that if a call to load assets returns 0 assets, you will not receive any callbacks if you subscribe through `OneCallbackPerResourceLoaded` where as if you subscribe through `OneCallbackPerLoadCall` you would still get your one callback.

If you update or replace the asset being loaded remember to call the `Complete` method to indicate your intentions regarding:
 * Whether the remaining postfixes should be called.

In addition, if we take a look at the `Parameters` property of the context object, we will find the following definition:

```C#
/// <summary>
/// Class representing the original parameters of the load call.
/// </summary>
public class ResourceLoadedParameters
{
    /// <summary>
    /// Gets the name of the resource being loaded. Will not be the complete resource path if 'LoadByType' is used.
    /// </summary>
    public string Path { get; set; }

    /// <summary>
    /// Gets the type that passed to the resource load call.
    /// </summary>
    public Type Type { get; set; }

    /// <summary>
    /// Gets the type of call that loaded this asset. If 'LoadByType' is specified
    /// multiple assets may be returned if subscribed as 'OneCallbackPerLoadCall'.
    /// </summary>
    public ResourceLoadType LoadType { get; }
}

/// <summary>
/// Enum representing the different ways a resource may be loaded.
/// </summary>
public enum ResourceLoadType
{
    /// <summary>
    /// Indicates that this call is loading all assets of a specific type (below a specific path) in the Resources API.
    /// </summary>
    LoadByType,

    /// <summary>
    /// Indicates that this call is loading a single named asset in the Resources API.
    /// </summary>
    LoadNamed,

    /// <summary>
    /// Indicates that this call is loading a single named built-in asset in the Resources API.
    /// </summary>
    LoadNamedBuiltIn
}
```

It is also worth mentioning that these hooks handle both synchronous and asynchronous loading of resources.

Hooks subscribed through hook behaviour `OneCallbackPerLoadCall` will be called before hooks with the behaviour `OneCallbackPerResourceLoaded`.

### AssetBundle Load Methods
It is also possible to hook the loading of `AssetBundles` themselves. Only prefix hooks are supported when loading an asset bundle.

The event callback chain looks like this `[AssetBundleLoading/AsyncAssetBundleLoading hooks] => [Original Method]`.

#### AssetBundle Synchrous Load Methods
The method `RegisterAssetBundleLoadingHook( Action<AssetBundleLoadingContext> action )` is used to hook the synchronous AssetBundle load methods.

This API is a prefix to the `AssetBundle` API, which means that it is called before the AssetBundle is loaded.

The `AssetBundleLoadingContext` class has the following definition:

```C#
/// <summary>
/// The operation context surrounding the AssetBundleLoading hook (synchronous).
/// </summary>
public class AssetBundleLoadingContext : IAssetBundleLoadingContext
{
    /// <summary>
    /// Gets a normalized path to the asset bundle that is:
    ///  * Relative to the current directory
    ///  * Lower-casing
    ///  * Uses '\' as separators.
    /// </summary>
    /// <returns></returns>
    public string GetNormalizedPath();

    /// <summary>
    /// Indicate your work is done and if any other hooks to this asset bundle load should be called.
    /// </summary>
    /// <param name="skipRemainingPrefixes">Indicate if the remaining prefixes should be skipped.</param>
    /// <param name="skipOriginalCall">Indicate if the original call should be skipped. If you set the asset bundle, you likely want to set this to true.</param>
    public void Complete( bool skipRemainingPrefixes = true, bool? skipOriginalCall = true );

    /// <summary>
    /// Disables recursive calls if you make an asset/asset bundle load call
    /// from within your callback. If you want to prevent recursion this should
    /// be called before you load the asset/asset bundle.
    /// </summary>
    public void DisableRecursion();

    /// <summary>
    /// Gets the parameters of the call.
    /// </summary>
    public AssetBundleLoadingParameters Parameters { get; }

    /// <summary>
    /// Gets or sets the AssetBundle being loaded.
    /// </summary>
    public AssetBundle Bundle { get; set; }
}
```

Because this is a prefix API, the `Bundle` property will be null when the method is called and it is up to you to set it to a different value if you can handle the specified path.

If you update the `Bundle` property, remember to call the `Complete` method to indicate your intentions regarding:
 * Whether or not the remaining prefixes should be skipped.
 * Whether or not the original method should be skipped.

In addition, if we take a look at the `Parameters` property of the context object, we will find the following definition:

```C#
/// <summary>
/// Class representing the original parameters of the load call.
/// </summary>
public class AssetBundleLoadingParameters
{
    /// <summary>
    /// Gets or sets the loaded path. Only relevant for 'LoadFromFile'.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets or sets the crc. Only relevant for 'LoadFromFile'.
    /// </summary>
    public uint Crc { get; }

    /// <summary>
    /// Gets or sets the offset. Only relevant for 'LoadFromFile'.
    /// </summary>
    public ulong Offset { get; }

    /// <summary>
    /// Gets the type of call that is loading this asset bundle.
    /// </summary>
    public AssetBundleLoadType LoadType { get; }
}

/// <summary>
/// Enum representing the different ways an asset bundle may be loaded.
/// </summary>
public enum AssetBundleLoadType
{
    /// <summary>
    /// Indicates that the asset bundle is being loaded through a call to 'LoadFromFile' or 'LoadFromFileAsync'.
    /// </summary>
    LoadFromFile,
}
```

As can be seen, the current implementation only hooks the LoadFromFile/LoadFromFileAsync ways of loading AssetBundles, but this may be expanded in the future.

It may also be worth looking at the `GetNormalizedPath()` method instead of the `Path` property of the original call parameters. This is because the path passed to the method can take literally any form:
 * Absolute path
 * Relative path
 * Include a stray '..' in the middle of the path
 
Another way to change the result of the asset bundle load operation is to change the value of the `Path`, `Crc` and `Offset` properties in the `Parameters` property. If you do this, you likely will not want to call the Complete method, as you will want the original method to still be called.

#### AssetBundle Asynchronous Load Methods
The method `RegisterAsyncAssetBundleLoadingHook( Action<AsyncAssetBundleLoadingContext> action )` is used to hook the asynchronous AssetBundle load methods.

This API is a prefix to the `AssetBundle` API, which means that it is called before the `AssetBundleCreateRequest` is created.

The `AsyncAssetBundleLoadingContext` class has the following definition:

```C#
/// <summary>
/// The operation context surrounding the AsyncAssetBundleLoading hook (asynchronous).
/// </summary>
public class AsyncAssetBundleLoadingContext : IAssetBundleLoadingContext
{
    /// <summary>
    /// Gets a normalized path to the asset bundle that is:
    ///  * Relative to the current directory
    ///  * Lower-casing
    ///  * Uses '\' as separators.
    /// </summary>
    /// <returns></returns>
    public string GetNormalizedPath();

    /// <summary>
    /// Indicate your work is done and if any other hooks to this asset bundle load should be called.
    /// </summary>
    /// <param name="skipRemainingPrefixes">Indicate if the remaining prefixes should be skipped.</param>
    /// <param name="skipOriginalCall">Indicate if the original call should be skipped. If you set the request, you likely want to set this to true.</param>
    public void Complete( bool skipRemainingPrefixes = true, bool? skipOriginalCall = true );

    /// <summary>
    /// Disables recursive calls if you make an asset/asset bundle load call
    /// from within your callback. If you want to prevent recursion this should
    /// be called before you load the asset/asset bundle.
    /// </summary>
    public void DisableRecursion();

    /// <summary>
    /// Gets the parameters of the call.
    /// </summary>
    public AssetBundleLoadingParameters Parameters { get; }

    /// <summary>
    /// Gets or sets the AssetBundleCreateRequest being used to load the AssetBundle.
    /// </summary>
    public AssetBundleCreateRequest Request { get; set; }

    /// <summary>
    /// Gets or sets the AssetBundle being loaded.
    /// </summary>
    public AssetBundle Bundle { get; set; }

    /// <summary>
    /// Gets or sets how this load operation should be resolved.
    /// Setting the Bundle/Request property will automatically update this value.
    /// </summary>
    public AsyncAssetBundleLoadingResolve ResolveType { get; set; }
}
```

Because this is a prefix API, the `Request` property will be null when the method is called and it is up to you to set it to a different value if you can handle the specified path.

As you can see there is actually also a `Bundle` property available on the context object. Under normal circumstances, however, you cannot use the `Bundle` property on the `AsyncAssetBundleLoadingContext`. In order to be able to use these, you must first call `ResourceRedirection.EnableSyncOverAsyncAssetLoads` once during your initialization logic. This will allow you to set the bundle directly so you don't have to go through the standard `AssetBundle` API to obtain a request object.

It is, however, recommended that if you can that you set the `Request` property instead of the `Bundle` property as that will keep the operation asynchronous and not block the game while the asset is being loaded.

If you update the `Request` property, remember to call the `Complete` method to indicate your intentions regarding:
 * Whether or not the remaining prefixes should be skipped.
 * Whether or not the original method should be skipped.

In addition, if we take a look at the `Parameters` property of the context object, we will find the following definition:

```C#
/// <summary>
/// Class representing the original parameters of the load call.
/// </summary>
public class AssetBundleLoadingParameters
{
    /// <summary>
    /// Gets the loaded path. Only relevant for 'LoadFromFile'.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the crc. Only relevant for 'LoadFromFile'.
    /// </summary>
    public uint Crc { get; }

    /// <summary>
    /// Gets the offset. Only relevant for 'LoadFromFile'.
    /// </summary>
    public ulong Offset { get; }

    /// <summary>
    /// Gets the type of call that is loading this asset bundle.
    /// </summary>
    public AssetBundleLoadType LoadType { get; }
}

/// <summary>
/// Enum representing the different ways an asset bundle may be loaded.
/// </summary>
public enum AssetBundleLoadType
{
    /// <summary>
    /// Indicates that the asset bundle is being loaded through a call to 'LoadFromFile' or 'LoadFromFileAsync'.
    /// </summary>
    LoadFromFile,
}
```

As can be see, the current implementation only hooks the LoadFromFile/LoadFromFileAsync ways of loading AssetBundles, but this may be expanded in the future.

It may also be worth looking at the `GetNormalizedPath()` method instead of the `Path` property of the original call parameters. This is because the path passed to the method can take literally any form:
 * Absolute path
 * Relative path
 * Include a stray '..' in the middle of the path
 
Another way to change the result of the asset bundle load operation is to change the value of the `Path`, `Crc` and `Offset` properties in the `Parameters` property. If you do this, you likely will not want to call the Complete method, as you will want the original method to still be called.

An important additional way to subscribe to the prefix asset bundle loading operations are through the method `RegisterAsyncAndSyncAssetBundleLoadingHook( int priority, Action<IAssetBundleLoadingContext> action )`. This method will handle both async and sync asset bundle loading operations. The `IAssetLoadingContext` is an interface implemented by both the `AssetBundleLoadingContext` and `AsyncAssetBundleLoadingContext`.

Do note, that if you want to use this method you must first call the method `EnableSyncOverAsyncAssetLoads()` to enable the hooks required for this to work.

### About Recursion
As you may have noticed, all of the context classes shown in the previous sections had a method called `DisableRecursion` and that there is a method called `DisableRecursionPermanently` directly on the `ResourceRedirection` class.

The purpose of these method is, as it name states, to disable recursion. That only leaves the question, when does recursion occur?

Recursion will happen anytime you try to load an asset/resource/asset bundle from within your callback using the `AssetBundle` or `Resources` API. Essentially, what it means is that all callbacks (except the one loading the resource) will get a chance to modify the resource that is being loaded by your callback.

This may not always be desirable, so if you call the method `DisableRecursion` *before* you load your resource, this recursive behaviour is disabled. In many other cases, this behaviour is very desirable because it means that it is less important to set the *correct* priority, whatever that may be.

Recursion has an important side effect for other prefix/postfix callbacks, and that is that they will always be called if you make a recursive load call in your callback, even if you indicate through the `Complete` method that they should not be called. So if, in your scenario, it is important to avoid this, you must disable recursion.

`DisableRecursionPermanently` disables recursion permanently for all subscribers to the ResourceRedirection API no matter who calls it. This is a game-wide setting that should be decided upon between plugin developers rather than by the hand of the individual.

These options exists only because it is not currently known whether or not having recursion enabled gives the best experience to plugin developers.

### Implementing an Asset Redirector
Here's an example of how a resource redirection may be implemented to hook all `Texture2D` objects loaded through the `AssetBundle` API:

```C#
class TextureReplacementPlugin
{
    void Awake()
    {
        ResourceRedirection.RegisterAssetLoadedHook(
            behaviour: HookBehaviour.OneCallbackPerResourceLoaded,
            priority: 0,
            action: AssetLoaded );
    }

    public void AssetLoaded( AssetLoadedContext context )
    {
        if( context.Asset is Texture2D texture2d ) // also acts as a null check
        {
            // TODO: Modify, replace or dump the texture
                
            context.Asset = texture2d; // only need to update the reference if you created a new texture
            context.Complete(
                skipRemainingPostfixes: true );
        }
    }
}
```

### Implementing an AssetBundle Redirector
Here's an example of how a resource redirection may be implemented to redirect non-existing resources to a separate 'mods' directory.

```C#
class AssetBundleRedirectorPlugin
{
    void Awake()
    {
        ResourceRedirection.RegisterAssetBundleLoadingHook(
            priority: 1000,
            action: AssetBundleLoading );

        ResourceRedirection.RegisterAsyncAssetBundleLoadingHook(
            priority: 1000,
            action: AsyncAssetBundleLoading );
    }

    public void AssetBundleLoading( AssetBundleLoadingContext context )
    {
        if( !File.Exists( context.Parameters.Path ) )
        {
            // the game is trying to load a path that does not exist, lets redirect to our own resources
		    
            // obtain different resource path
            var normalizedPath = context.GetNormalizedPath();
            var modFolderPath = Path.Combine( "mods", normalizedPath );
		    
            // if the path exists, let's load that instead
            if( File.Exists( modFolderPath ) )
            {
                var bundle = AssetBundle.LoadFromFile( modFolderPath );
		    
                context.Bundle = bundle;
                context.Complete(
                    skipRemainingPrefixes: true,
                    skipOriginalCall: true );
            }
        }
    }

    public void AsyncAssetBundleLoading( AsyncAssetBundleLoadingContext context )
    {
        if( !File.Exists( context.Parameters.Path ) )
            {
            // the game is trying to load a path that does not exist, lets redirect to our own resources
		    
            // obtain different resource path
            var normalizedPath = context.GetNormalizedPath();
            var modFolderPath = Path.Combine( "mods", normalizedPath );
		    
            // if the path exists, let's load that instead
            if( File.Exists( modFolderPath ) )
            {
                var request = AssetBundle.LoadFromFileAsync( modFolderPath );
		    
                context.Request = request;
                context.Complete(
                    skipRemainingPrefixes: true,
                    skipOriginalCall: true );
            }
        }
    }
}
```

Here's a smart way to implement the same thing, by having a single method that hooks both the synchronous and asynchronous methods at the same time:

```C#
class AssetBundleRedirectorSyncOverAsyncPlugin
{
    void Awake()
    {
        ResourceRedirection.EnableSyncOverAsyncAssetLoads();
	    
        ResourceRedirection.RegisterAsyncAndSyncAssetBundleLoadingHook(
            priority: 1000,
            action: AssetBundleLoading );
    }

    public void AssetBundleLoading( IAssetBundleLoadingContext context )
    {
        if( !File.Exists( context.Parameters.Path ) )
        {
            // the game is trying to load a path that does not exist, lets redirect to our own resources
	    
            // obtain different resource path
            var normalizedPath = context.GetNormalizedPath();
            var modFolderPath = Path.Combine( "mods", normalizedPath );
	    
            // if the path exists, let's load that instead
            if( File.Exists( modFolderPath ) )
            {
                var bundle = AssetBundle.LoadFromFile( modFolderPath );
	    
                context.Bundle = bundle;
                context.Complete(
                    skipRemainingPrefixes: true,
                    skipOriginalCall: true );
            }
        }
    }
}
```

While this is clean it causes all asset bundles to be loaded synchronously, potentially locking up the game causing FPS lag. Another approach that also handles that could look like this:

```C#
class SmartAssetBundleRedirectorSyncOverAsyncPlugin
{
    void Awake()
    {
        ResourceRedirection.EnableSyncOverAsyncAssetLoads();
	    
        ResourceRedirection.RegisterAsyncAndSyncAssetBundleLoadingHook(
            priority: 1000,
            action: AssetBundleLoading );
    }

    public void AssetBundleLoading( IAssetBundleLoadingContext context )
    {
        if( !File.Exists( context.Parameters.Path ) )
        {
            // the game is trying to load a path that does not exist, lets redirect to our own resources
	    
            // obtain different resource path
            var normalizedPath = context.GetNormalizedPath();
            var modFolderPath = Path.Combine( "mods", normalizedPath );
	    
            // if the path exists, let's load that instead
            if( File.Exists( modFolderPath ) )
            {
                if( context is AsyncAssetBundleLoadingContext asyncContext )
                {
                    var request = AssetBundle.LoadFromFileAsync( modFolderPath );
                    asyncContext.Request = request;
                }
                else
                {
                    var bundle = AssetBundle.LoadFromFile( modFolderPath );
                    context.Bundle = bundle;
                }
	    
                context.Complete(
                    skipRemainingPrefixes: true,
                    skipOriginalCall: true );
            }
        }
    }
}
```

Here's the redirector that is activated if the `EmulateAssetBundles` option is enabled in the plugin, which allows loading asset bundles from a different location than the game is requesting the bundle from:

```C#
/// <summary>
/// Creates an asset bundle hook that attempts to load asset bundles in the emulation directory
/// over the default asset bundles if they exist.
/// </summary>
/// <param name="hookPriority">Priority of the hook.</param>
/// <param name="emulationDirectory">The directory to look for the asset bundles in.</param>
public static void EnableEmulateAssetBundles( int hookPriority, string emulationDirectory )
{
    RegisterAssetBundleLoadingHook( hookPriority, ctx => HandleAssetBundleEmulation( ctx, SetBundle ) );
    RegisterAsyncAssetBundleLoadingHook( hookPriority, ctx => HandleAssetBundleEmulation( ctx, SetRequest ) );
		    
    // define base callback
    void HandleAssetBundleEmulation<T>( T context, Action<T, string> changeBundle )
        where T : IAssetBundleLoadingContext
    {
        if( context.Parameters.LoadType == AssetBundleLoadType.LoadFromFile )
        {
            var normalizedPath = context.GetNormalizedPath();
            var emulatedPath = Path.Combine( emulationDirectory, normalizedPath );
            if( File.Exists( emulatedPath ) )
            {
                changeBundle( context, emulatedPath );
		    
                context.Complete(
                    skipRemainingPrefixes: true,
                    skipOriginalCall: true );
            }
        }
    }
		    
    // synchronous specific code
    void SetBundle( AssetBundleLoadingContext context, string path )
    {
        context.Bundle = AssetBundle.LoadFromFile( path, context.Parameters.Crc, context.Parameters.Offset );
    }
		    
    // asynchronous specific code
    void SetRequest( AsyncAssetBundleLoadingContext context, string path )
    {
        context.Request = AssetBundle.LoadFromFileAsync( path, context.Parameters.Crc, context.Parameters.Offset );
    }
}
```

Here's the redirector that is activated if the `RedirectMissingAssetBundles` option is enabled in the plugin, which essentially simply loads an empty asset bundle if an asset bundle cannot be found:

```C#
/// <summary>
/// Creates an asset bundle hook that redirects asset bundles loads to an empty
/// asset bundle if the file that is being loaded does not exist.
/// </summary>
/// <param name="hookPriority">Priority of the hook.</param>
public static void EnableRedirectMissingAssetBundlesToEmptyAssetBundle( int hookPriority )
{
    RegisterAssetBundleLoadingHook( hookPriority, ctx => HandleMissingBundle( ctx, SetBundle ) );
    RegisterAsyncAssetBundleLoadingHook( hookPriority, ctx => HandleMissingBundle( ctx, SetRequest ) );
	    
    // define base callback
    void HandleMissingBundle<TContext>( TContext context, Action<TContext, byte[]> changeBundle )
        where TContext : IAssetBundleLoadingContext
    {
        if( context.Parameters.LoadType == AssetBundleLoadType.LoadFromFile
            && !File.Exists( context.Parameters.Path ) )
        {
            var buffer = Properties.Resources.empty;
            CabHelper.RandomizeCab( buffer );
	    
            changeBundle( context, buffer );
	    
            context.Complete(
                skipRemainingPrefixes: true,
                skipOriginalCall: true );
	    
            XuaLogger.ResourceRedirector.Warn( "Tried to load non-existing asset bundle: " + context.Parameters.Path );
        }
    }
	    
    // synchronous specific code
    void SetBundle( AssetBundleLoadingContext context, byte[] assetBundleData )
    {
        var bundle = AssetBundle.LoadFromMemory( assetBundleData );
        context.Bundle = bundle;
    }
	    
    // asynchronous specific code
    void SetRequest( AsyncAssetBundleLoadingContext context, byte[] assetBundleData )
    {
        var request = AssetBundle.LoadFromMemoryAsync( assetBundleData );
        context.Request = request;
    }
}
```

### Implementing an Asset/Resource Handler (Auto Translator)
This section shows how to implement an asset/resource redirector that respects the Auto Translator configuration.

The `XUnity.AutoTranslator.Core.Plugin.dll` assembly has a base class that can be used to implement a plugin that dumps resources for the purposes of translation.

This class simply hooks the postfix to the load of assets from the `AssetBundle` and the `Resources` API. Here's how the base class looks:

```C#
/// <summary>
/// Base implementation of resource redirect handler that takes care of the plumming for a
/// resource redirector that is interested in either updating or dumping redirected resources.
/// </summary>
/// <typeparam name="TAsset">The type of asset being redirected.</typeparam>
public abstract class AssetLoadedHandlerBaseV2<TAsset>
    where TAsset : UnityEngine.Object
{
    /// <summary>
    /// Method invoked when an asset should be updated or replaced.
    /// </summary>
    /// <param name="calculatedModificationPath">This is the modification path calculated in the CalculateModificationFilePath method.</param>
    /// <param name="asset">The asset to be updated or replaced.</param>
    /// <param name="context">This is the context containing all relevant information for the resource redirection event.</param>
    /// <returns>A bool indicating if the event should be considered handled.</returns>
    protected abstract bool ReplaceOrUpdateAsset( string calculatedModificationPath, ref TAsset asset, IAssetOrResourceLoadedContext context );

    /// <summary>
    /// Method invoked when an asset should be dumped.
    /// </summary>
    /// <param name="calculatedModificationPath">This is the modification path calculated in the CalculateModificationFilePath method.</param>
    /// <param name="asset">The asset to be updated or replaced.</param>
    /// <param name="context">This is the context containing all relevant information for the resource redirection event.</param>
    /// <returns>A bool indicating if the event should be considered handled.</returns>
    protected abstract bool DumpAsset( string calculatedModificationPath, TAsset asset, IAssetOrResourceLoadedContext context );

    /// <summary>
    /// Method invoked when a new resource event is fired to calculate a unique path for the resource.
    /// </summary>
    /// <param name="asset">The asset to be updated or replaced.</param>
    /// <param name="context">This is the context containing all relevant information for the resource redirection event.</param>
    /// <returns>A string uniquely representing a path for the redirected resource.</returns>
    protected abstract string CalculateModificationFilePath( TAsset asset, IAssetOrResourceLoadedContext context );

    /// <summary>
    /// Method to be invoked to indicate if the asset should be handled or not.
    /// </summary>
    /// <param name="asset">The asset to be updated or replaced.</param>
    /// <param name="context">This is the context containing all relevant information for the resource redirection event.</param>
    /// <returns>A bool indicating if the asset should be handled.</returns>
    protected abstract bool ShouldHandleAsset( TAsset asset, IAssetOrResourceLoadedContext context );
}
```

The Auto Translation includes one default implementation of this class for TextAssets. It looks like this:

```C#
internal class TextAssetLoadedHandler : AssetLoadedHandlerBaseV2<TextAsset>
{
    protected override string CalculateModificationFilePath( TextAsset asset, IAssetOrResourceLoadedContext context )
    {
        return context.GetPreferredFilePath( asset, ".txt" );
    }

    protected override bool DumpAsset( string calculatedModificationPath, TextAsset asset, IAssetOrResourceLoadedContext context )
    {
        Directory.CreateDirectory( new FileInfo( calculatedModificationPath ).Directory.FullName );
        File.WriteAllBytes( calculatedModificationPath, asset.bytes );

        return true;
    }

    protected override bool ReplaceOrUpdateAsset( string calculatedModificationPath, ref TextAsset asset, IAssetOrResourceLoadedContext context )
    {
        RedirectedResource file;

        var files = RedirectedDirectory.GetFile( calculatedModificationPath ).ToList();
        if( files.Count == 0 )
        {
		    return false;
        }
        else
        {
            if( files.Count > 1 )
            {
                XuaLogger.AutoTranslator.Warn( "Found more than one resource file in the same path: " + calculatedModificationPath );
            }
		    
            file = files.FirstOrDefault();
        }

        if( file != null )
        {
            using( var stream = file.OpenStream() )
            {
                var data = stream.ReadFully( (int)stream.Length );
                var text = Encoding.UTF8.GetString( data );
		    
                var ext = asset.GetOrCreateExtensionData<TextAssetExtensionData>();
                ext.Data = data;
                ext.Text = text;
		    
                return true;
            }
        }
        return false;
    }

    protected override bool ShouldHandleAsset( TextAsset asset, IAssetOrResourceLoadedContext context )
    {
        return !context.HasReferenceBeenRedirectedBefore( asset );
    }
}
```

Note that when accessing the resource file, we do not use the standard file API to obtain a stream to get the data in the file. Instead we use the RedirectedDirectory facade. This will also look in ZIP files and simply treat a ZIP file as a directory when making the lookup.

Another examples of an implementation of this class would be for Koikatsu that enables replacing its custom resources:

```C#
public class ScenarioDataResourceRedirector : AssetLoadedHandlerBaseV2<ScenarioData>
{
   public ScenarioDataResourceRedirector()
   {
      CheckDirectory = true;
   }

   protected override string CalculateModificationFilePath( ScenarioData asset, IAssetOrResourceLoadedContext context )
   {
      return context.GetPreferredFilePathWithCustomFileName( asset, null )
         .Replace( ".unity3d", "" );
   }

   protected override bool DumpAsset( string calculatedModificationPath, ScenarioData asset, IAssetOrResourceLoadedContext context )
   {
      var defaultTranslationFile = Path.Combine( calculatedModificationPath, "translation.txt" );
      var cache = new SimpleTextTranslationCache(
         file: defaultTranslationFile,
         loadTranslationsInFile: false );

      foreach( var param in asset.list )
      {
         if( param.Command == Command.Text )
         {
            for( int i = 0; i < param.Args.Length; i++ )
            {
               var key = param.Args[ i ];

               if( !string.IsNullOrEmpty( key ) && LanguageHelper.IsTranslatable( key ) )
               {
                  cache.AddTranslationToCache( key, key );
               }
            }
         }
      }

      return true;
   }

   protected override bool ReplaceOrUpdateAsset( string calculatedModificationPath, ref ScenarioData asset, IAssetOrResourceLoadedContext context )
   {
      var defaultTranslationFile = Path.Combine( calculatedModificationPath, "translation.txt" );
      var redirectedResources = RedirectedDirectory.GetFilesInDirectory( calculatedModificationPath, ".txt" );
      var streams = redirectedResources.Select( x => x.OpenStream() );
      var cache = new SimpleTextTranslationCache(
         outputFile: defaultTranslationFile,
         inputStreams: streams,
         allowTranslationOverride: false,
         closeStreams: true );

      foreach( var param in asset.list )
      {
         if( param.Command == Command.Text )
         {
            for( int i = 0; i < param.Args.Length; i++ )
            {
               var key = param.Args[ i ];
               if( !string.IsNullOrEmpty( key ) )
               {
                  if( cache.TryGetTranslation( key, true, out var translated ) )
                  {
                     param.Args[ i ] = translated;
                  }
                  else if( AutoTranslatorSettings.IsDumpingRedirectedResourcesEnabled && LanguageHelper.IsTranslatable( key ) )
                  {
                     cache.AddTranslationToCache( key, key );
                  }
               }
            }
         }
      }

      return true;
   }

   protected override bool ShouldHandleAsset( ScenarioData asset, IAssetOrResourceLoadedContext context )
   {
      return !context.HasReferenceBeenRedirectedBefore( asset );
   }
}
```

Note that this implementation uses a `SimpleTextTranslationCache` to lookup translations. Using this class for translation lookups have the following benefits:
 * Whitespace doesn't have to match exactly.
 * It respects the `RedirectedResourceDetectionStrategy` configuration. If this is not respected the plugin may double translate certain texts.
 * When loading text translation files, it supports the same text format that is otherwise used by the plugin.

Once you have implemented one of these classes, you just need to instantiate it and it will do it's magic. 
