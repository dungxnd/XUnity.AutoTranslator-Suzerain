# Target Framework Support

## Matrix

| Target | Runtime path | Status | Intended use |
|---|---|---|---|
| `net35` | Managed Unity/Mono (`MANAGED`) | Supported, default distribution | Legacy and broad Unity compatibility |
| `net471` | Managed Unity/Mono (`MANAGED`) | Opt-in, compile-verified | Unity 2018.1+ / .NET 4.x compatibility level |
| `net6.0` | IL2CPP (`IL2CPP`) | Supported | IL2CPP games via the proxy assemblies |

`net35` and `net471` build the same managed code path. `net6.0` is a separate IL2CPP code path and does not replace either managed target.

Shared source must remain `net35`-compatible. `net471` is an additive target, not permission to use .NET 4.x-only APIs in shared code; use a `net471`-only conditional block if a specific feature needs them.

## Why `net471` and not `netstandard2.0`

The bundled loader and utility assemblies (`MelonLoader` 0.5.2, `BepInEx` 5.0, `ExIni`, SharpZipLib, Harmony, MonoMod) are `net35` assemblies. A `netstandard2.0` project cannot reference them. `net471` can reference `net35` assemblies and targets the .NET 4.x compatibility level available in Unity 2018.1+.

Switch to `netstandard2.0` only after the bundled dependencies themselves move to `netstandard2.0` or are replaced.

## Build

Managed legacy (default):

```powershell
msbuild src/XUnity.AutoTranslator.Plugin.Core/XUnity.AutoTranslator.Plugin.Core.csproj /p:Configuration=Release
```

Modern managed (compile verification):

```powershell
msbuild src/XUnity.AutoTranslator.Plugin.Core/XUnity.AutoTranslator.Plugin.Core.csproj /t:Build /p:Configuration=Release /p:TargetFramework=net471 /p:SkipILRepack=true
```

This requires the .NET Framework 4.7.1 targeting pack. CI supplies it through the `Microsoft.NETFramework.ReferenceAssemblies` package; locally you can either install the targeting pack or pass `/p:CI=true` to use the package.

IL2CPP:

```powershell
msbuild src/XUnity.AutoTranslator.Plugin.Core/XUnity.AutoTranslator.Plugin.Core.csproj /t:Build /p:Configuration=Release /p:TargetFramework=net6.0
```

CI builds the whole solution (all target frameworks), runs the focused `net471` compile check so the modern managed target cannot silently rot, and runs the framework-agnostic text-pipeline tests on both `net471` and `net8.0`.

The `net8.0` runner is a modern test host only. It is not a shipped Unity target; it references the `net6.0` core assembly and exercises code paths that do not touch Unity APIs.

## Packaging and release policy

- Release packaging currently continues to use the `net35` managed artifact.
- The `net471` core stack plus the managed BepInEx and MelonLoader plugins are compile-verified.
- The `net471` artifacts are opt-in and not copied into the default `dist` layouts yet.
- Do not remove `net35` in a minor release. Revisit after collecting user/Unity-version demand and after the modern managed artifact has been validated in real games.
- If a minimum Unity version is adopted later, make the removal in a major release and update this document and the README at the same time.
