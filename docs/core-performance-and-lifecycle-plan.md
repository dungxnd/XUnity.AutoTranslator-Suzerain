# XUAT Core — Performance, Large-File, Bug/Leak Analysis and Remediation Plan

Status: analysis + implementation proposal  
Scope: `src/XUnity.AutoTranslator.Plugin.Core` (non-provider code), plus the `src/XUnity.Common` pieces the core depends on  
Date: 2026-10-18  
Method: source review, targeted compile/run checks, and static scans. A full solution build was not possible because no .NET SDK was installed at analysis time.

---

## 0. Executive summary

1. **The translation file format is not the main bottleneck.** The per-lookup data structure is already dictionary-based. The costs are in loading, deriving variations/tokens, pruning, and reloading.
2. **Do not switch the source format to JSON or SQLite.** `key=value` `.txt` is a de-facto public contract: users hand-edit it and community packs depend on it. JSON is larger/slower and loses comments/directives; SQLite adds native/IL2CPP dependencies and produces unmergeable files. If cold start remains a problem, add an optional compiled sidecar while keeping `.txt` as the source of truth.
3. **The highest-impact large-file fixes are:** stream the parser, pre-size dictionaries, add cheap guards before the eager variation/token passes, stop pruning on every reload, and avoid repeated full-dictionary copies.
4. **The highest-risk correctness issues are:** the `//` escape round-trip bug, destructive/non-atomic prune, the permanent `Settings.IsShutdown` latch, unsynchronized `TextTranslationCache` reads, and lifecycle leaks (maintenance actions, scene callbacks, native Unity assets).
5. **Modern C# is not the problem.** The compiler is set to `latest`, but net35's BCL limits runtime features. The bigger maintainability issue is duplicated parsing/lookup logic and very large classes/methods.

---

## 1. Current data flow (relevant to this analysis)

### 1.1 Startup / load

```
AutoTranslationPlugin.LoadTranslations()
  -> SettingsTranslationsInitializer.LoadTranslations()
  -> TextCache.LoadTranslationFiles()
       -> LoadTranslationsInFile(main file)
       -> LoadTranslationsInFile(every other .txt/.zip)
       -> LoadTranslationsInStream(registered stream packages)
       -> LoadTranslationsInKeyValuePairs(registered packages)
       -> variation pass   (externally/internally trimmed variants)
       -> partial pass      (optional)
       -> token pass        (rich-text tokens)
```

Parsing is line-based:

- `key=value`, first unescaped `=` separates.
- Escapes: `\=`, `\\`, `\n`, `\r`, `\uXXXX`.
- `//` starts an inline comment anywhere in the line.
- Legacy `%3D` decodes to `=`.
- Prefixes `r:"..."=...` (regex) and `sr:"..."=...` (splitter regex).
- Directives `#set level`, `#set exe`, `#set required-resolution`, `#enable fallback`.
- Files are UTF-8 `.txt`; `.zip` archives are also supported.

### 1.2 Lookup

```
TryGetTranslation(UntranslatedText key, allowRegex, allowToken, scope)
  -> scoped dictionary probes (token + full, original/trimmed variants)
  -> unscoped dictionary probes (token + full)
  -> regex scan (user regexes, reverse order)
  -> static translations
  -> optional AddTranslationToCache(...) on whitespace hits
```

`TextTranslationCache.TryGetTranslation` is approximately 530 lines (line 1089 through 1622), with the lookup permutations hand-unrolled and labelled `c1`..`c12` in comments/logs.

### 1.3 Save / reload

```
New translation
  -> AddTranslationToCache(...)
  -> QueueNewTranslationForDisk(...) under _writeToFileSync
  -> MaintenanceHelper thread: SaveNewTranslationsToDisk()
  -> File.Open(..., FileMode.Append) + StreamWriter(Encoding.UTF8)

Reload
  -> TextCache.PruneMainTranslationFile()
       -> load every other translation file to collect regexes
       -> read main file, write surviving lines to MemoryStream
       -> File.Move(main, backup)
       -> FileMode.Create(main) and copy MemoryStream
  -> LoadTranslations(true)
       -> full parse of everything again
```

---

## 2. Large translation files

### 2.1 Load-path costs

| Stage | Cost | Evidence |
|---|---|---|
| Whole file read | `ReadToEnd()` materialises the entire file as UTF-16 | `TextTranslationCache.cs:588`; `SimpleTextTranslationCache.cs:162` |
| Line splitting | `Split('\r','\n')` allocates one string per line plus the array | same lines |
| Per-line decode | one `StringBuilder` and two `ToString()` results, plus resizes | `TextHelper.cs:24,74,124` |
| Dictionary insert | forward and reverse maps, no capacity pre-sizing on first load | `TextTranslationCache.cs:36-46, 964-965` |
| Variation pass | two `UntranslatedText` objects per entry; up to two extra dictionary entries per key | `TextTranslationCache.cs:280-314` |
| Partial pass (optional) | per-character token list, repeated string concatenation | `TextTranslationCache.cs:321-333, 413-470` |
| Token pass | `RichTextParser.Parse` on key and value for every entry when rich text is enabled | `TextTranslationCache.cs:335-385`; `Parsing/RichTextParser.cs:47-56` |

`ReadToEnd().Split` means peak memory is roughly the file bytes + the UTF-16 copy + all line strings: commonly 2–3× the file size before the dictionaries are even populated.

### 2.2 Resident memory estimate

Not a measured figure; use it for planning and then benchmark:

- Each key and value is a separate string object.
- Each plain entry occupies a slot in `_translations` and `_reverseTranslations`.
- Variation entries add more keys (and possibly more values).
- Scoped entries duplicate the structure per scope; `TranslationDictionaries` creates nine collections per scope (`TextTranslationCache.cs:1645-1668`), though they stay empty until a scoped entry is added.

A rough estimate for 60-character entries is a few hundred bytes each. 100k lines is manageable; 1M lines can plausibly reach several hundred MB to ~1 GB and is a serious risk on 32-bit Mono. Measure with a generated corpus before promising a limit.

### 2.3 Reload and prune

`AutoTranslationPlugin.ReloadTranslations()` always does:

```csharp
TextCache.PruneMainTranslationFile();
LoadTranslations( true );
```

Evidence: `AutoTranslationPlugin.cs:3195-3201`.

`PruneMainTranslationFile` / `PruneTranslationFile`:

1. Loads every other translation file to collect `r:` regexes (`TextTranslationCache.cs:518-527`).
2. Reads the main file, splits it, and writes surviving lines to a `MemoryStream` (`:540-568`).
3. If any line was pruned, moves the original aside and recreates it (`:154-165`).
4. Then the normal loader parses everything again (`AutoTranslationPlugin.cs:585`).

Problems:

- At least three full passes over the corpus per reload.
- `regexes.Any(x => x.CompiledRegex.IsMatch(key))` for every plain key (`:553`) is O(keys × regex count).
- If any line is pruned, the rewrite writes only non-regex `key=value` lines. Comments, directives, and any `r:` / `sr:` lines in the main file are dropped (`:551-562`).
- The rewrite re-encodes values through `TextHelper.Encode`, which triggers the `//` bug below.
- `File.Move` + `FileMode.Create` is not atomic; a crash between them loses the main file.
- With `ReloadTranslationsOnFileChange=True`, every watcher event can trigger this whole sequence. The setting defaults to `False` (`Settings.cs:290`), which is good; do not enable it for very large files until this path is fixed.

### 2.4 Lookup costs

`TryGetTranslation` can perform roughly eight or more dictionary probes for a miss (scoped token/full variants, unscoped token/full variants, static lookups), and more for templated keys. Additional costs:

- `key.Untemplate(...)` is done before knowing whether a lookup will hit; each call is a `Replace` over the full string.
- User regexes are scanned linearly in reverse order (`:1524-1586`).
- The negative regex cache is reset wholesale at 10,000 entries (`:1548-1552`, `:1581-1585`), after which previously failed strings are scanned again.
- Whitespace-difference hits call `AddTranslationToCache`, which constructs two more `UntranslatedText` objects (`:1015-1024`).
- `TextTranslationCache` does **not** lock its dictionaries during lookup; `SimpleTextTranslationCache` does (`SimpleTextTranslationCache.cs:384`). `LoadTranslationFiles` clears/refills under `_writeToFileSync` (`TextTranslationCache.cs:187-212`), but readers do not take that lock. This is safe only as long as every lookup is on the main thread and no reload overlaps it. Treat it as not thread-safe by construction.

### 2.5 Format bugs and edge cases

| ID | Severity | Finding | Evidence |
|---|---|---|---|
| F-01 | Critical | `//` does not round-trip: `Encode` writes `\/\/`, but the decoder has no `case '/'` and keeps the backslashes. A key/value containing `//` becomes corrupted/unreachable after save + reload. | `TextHelper.cs:55-58, 144-158`; verified with a compiled round-trip test |
| F-02 | High | Prune rewrite can drop comments, directives, and `r:`/`sr:` lines from the main file when any line is pruned. | `TextTranslationCache.cs:551-562` |
| F-03 | High | Prune is non-atomic (`File.Move` then `FileMode.Create`) and can lose the main file on a crash. | `TextTranslationCache.cs:154-165` |
| F-04 | Medium | Malformed lines are silently ignored (`ReadTranslationLineAndDecode` returns null; caller does not count/log). A large broken file can “load successfully” with dead entries. | `TextTranslationCache.cs:607-613`; `TextHelper.cs:69-72, 119-122` |
| F-05 | Medium | Legacy `%3D` decode is lossy: a literal `%3D` is converted to `=`. | `TextHelper.cs:77-90` |
| F-06 | Low | Empty translations cannot be stored; the loader rejects empty values. | `TextTranslationCache.cs:613` |
| F-07 | Low | Plain keys are last-wins, while regexes are first-wins by pattern; the precedence is inconsistent and undocumented. | `TextTranslationCache.cs:964`; regex registration at `:845-892` |
| F-08 | Low | Static translation split is CRLF-only; an LF-normalised build would produce one giant line. | `TextTranslationCache.cs:786` |

Ruled out: a theory that each append writes a UTF-8 BOM into the middle of the file. I tested `StreamWriter(FileMode.Append, Encoding.UTF8)` on .NET Framework; no BOM is written when the stream is non-empty. This is not a bug.

### 2.6 Should the format change?

**Recommendation: keep `*.txt` as the canonical source format for now.**

Reasons:

- It is human-editable, diffable, mergeable, and already deployed.
- `r:` / `sr:` / directives are part of the contract.
- Per-lookup cost is not a format problem.
- A new format would multiply the parser compatibility surface (there are already four readers).

Do this instead:

1. Fix the `\/` escaping and the prune preservation/atomicity.
2. Extract a single `TranslationFileReader` used by all four readers.
3. Stream with `ReadLine()` and pre-size dictionaries.
4. If cold start is still unacceptable on 1M-line packs, generate an optional binary sidecar next to the text file (length-prefixed keys/values plus an offset index, validated by size + mtime + hash). Keep the text file as the source of truth so editing and git workflows are unchanged.

Do not use JSON (larger, slower, no comments), and do not use SQLite as the primary store (native provider, binary merge conflicts, incompatible with hand editing).

---

## 3. Hot-path performance

| ID | Severity | Finding | Evidence |
|---|---|---|---|
| H-01 | Medium | `SpamChecker.Update` runs every frame and `ResetThresholdTimerIfRequired` does a LINQ `.Sum()` over the rolling window every frame. | `AutoTranslationPlugin.cs:2845`; `SpamChecker.cs:46-50, 224` (also `:191`) |
| H-02 | Medium | `UpdateSpriteRenderers` performs a full-scene `FindObjectsOfType<SpriteRenderer>` when a texture hook requests a refresh. It is one-shot per trigger, but a large spike in big scenes. | `AutoTranslationPlugin.cs:676-705, 686` |
| H-03 | Medium | `GetCacheKey` builds a fresh `UntranslatedText` for every non-spam text change; the constructor performs multiple scans/substrings. | `AutoTranslationPlugin.cs:3179-3194`; `UntranslatedText.cs:195-330` |
| H-04 | Medium | `KickoffTranslations` drains all unstarted jobs in a single frame. | `TranslationManager.cs:200-227` |
| H-05 | Medium | `TranslateByEndpoint` starts an uncapped stabilization coroutine per text change and allocates a new `WaitForSecondsRealtime` per retry. | `AutoTranslationPlugin.cs:2377-2432, 2541` |
| H-06 | Medium | The rich-text parser runs `TagRegex.Split` for every entry, even when the text contains no `<`. | `Parsing/RichTextParser.cs:47-56` |
| H-07 | Medium | The variation pass constructs two `UntranslatedText` objects for every entry, even when there is no whitespace to trim. | `TextTranslationCache.cs:280-314` |
| H-08 | Low | Multiple full-dictionary snapshots are created for read-only passes (`ToList()`). | `TextTranslationCache.cs:280, 338`, plus scoped equivalents |
| H-09 | Low | The regex scan is linear and the negative cache is reset wholesale at 10k entries. | `TextTranslationCache.cs:1524-1586` |
| H-10 | Low | `LoadTranslationFiles` walks the translation directory tree on every load/prune. | `TextTranslationCache.cs:114-128` |

Practical optimizations, in order:

1. Replace `ReadToEnd().Split` with `StreamReader.ReadLine()`.
2. Pre-size the forward/reverse dictionaries on the first load (subsequent `Clear()` calls retain capacity).
3. Add a cheap whitespace check before constructing `UntranslatedText` in the variation pass.
4. Add `if (input.IndexOf('<') < 0) return null;` at the top of `RichTextParser.Parse`.
5. Reuse one snapshot of `_translations` for the read-only passes.
6. Replace the spam-checker `.Sum()` with a running total.
7. Memoize the last `UntranslatedText` on `TextTranslationInfo` for repeated hook events.
8. Budget `KickoffTranslations` per frame instead of draining everything at once.
9. Hoist `WaitForSecondsRealtime` out of retry loops and cap concurrent stabilization coroutines.
10. If regex packs are large, cap `RegexOptions.Compiled` after a few hundred patterns or prefilter by first character/length.

---

## 4. Lifecycle, bugs, and leaks

Severity legend: Critical = data loss/crash/feature permanently broken; High = leak or user-visible failure; Medium = correctness/robustness risk; Low = cleanup/polish.

| ID | Severity | Finding | Evidence |
|---|---|---|---|
| L-01 | Critical | Plugin shutdown does not dispose `PluginTextCaches`; each cache owns a file watcher and a maintenance action. | `AutoTranslationPlugin.cs:67, 3615-3655` |
| L-02 | Critical | `MaintenanceHelper` has no unregister API. Every `TextTranslationCache` registers `SaveNewTranslationsToDisk`; disposed caches can keep writing from the maintenance thread. | `MaintenanceHelper.cs:24-37, 47-75`; `TextTranslationCache.cs:80` |
| L-03 | Critical | `TextTranslationCache.Dispose` only disposes the file watcher; it does not clear caches or unsubscribe `TextTranslationFileChanged`. | `TextTranslationCache.cs:1671-1683` |
| L-04 | Critical | `SceneManager.sceneLoaded` is subscribed with a closure and never unsubscribed; the static callback keeps the plugin instance alive and fires after destruction. | `TranslationScopeHelper.cs:92` (MANAGED), `:74` (IL2CPP) |
| L-05 | Critical | Native Unity assets are created without an owner and never destroyed: a full `Instantiate(ui)` clone per font change, translated/original `Texture2D`s, and generated sprites. No `Resources.UnloadUnusedAssets()` exists in the project. | `TextTranslationInfo.cs:156`; `TextureTranslationInfo.cs:69,85` (author `#warning HideAndDontSave?` at `:70`); grep finds no `Resources.UnloadUnusedAssets` |
| L-06 | High | `SafeFileWatcher` blocks a callback thread in an unbounded `while (IsFileLocked) Thread.Sleep(100)`, has no `Error` handler, and `Dispose` does not unsubscribe `DirectoryUpdated` or guard callbacks against a disposed timer. | `SafeFileWatcher.cs:99-107, 127-146` |
| L-07 | High | `HttpEndpoint` allocates a new `XUnityWebClient` per translation and does not dispose it; timed-out requests can keep handlers/thread-pool work alive. | `Endpoints/Http/HttpEndpoint.cs:84-89` |
| L-08 | High | `ExtProtocolEndpoint.Dispose` calls `Thread.Abort()` and can dereference a null `_thread`. | `Endpoints/ExtProtocol/ExtProtocolEndpoint.cs:385-390` |
| L-09 | High | `TranslationAggregatorWindow` indexes `_translationViews[i]` using a different collection; endpoints can diverge and throw `IndexOutOfRangeException` from `OnGUI`. | `UI/TranslationAggregatorWindow.cs:108-125` |
| L-10 | High | `ComponentExtensions.TryGetTextFromGUIContentSafe` sets `_guiContentCheckFailed = false` in its catch, re-enabling a failing path instead of latching it. | `Extensions/ComponentExtensions.cs:314-323` |
| L-11 | High | `Settings.IsShutdown` is a one-way latch; `RebootPlugin` only resets endpoint error counters, so a spam shutdown cannot be recovered without restarting the game. | `Configuration/Settings.cs:61`; `AutoTranslationPlugin.cs:2947-2956`; no `IsShutdown = false` elsewhere |
| L-12 | Medium | `SimpleTextTranslationCache._shouldOverrideEntireFile` is set only when `hadTranslated` is true, but the only caller enters that path when `hadTranslated` is false. The full-rewrite branch is dead. | `SimpleTextTranslationCache.cs:270-296, 320-344` |
| L-13 | Medium | Zip entry streams from `ZipFile.GetInputStream` are not disposed; only the outer `ZipFile` is closed. | `TextTranslationCache.cs:757-770` |
| L-14 | Medium | `PluginTextCaches` is never evicted when a plugin translation folder disappears; stale caches and their maintenance actions remain. | `AutoTranslationPlugin.cs:600-623` |
| L-15 | Medium | `TranslationManager` mutates `_endpointsWithUnstartedJobs` while iterating it in reverse and has no duplicate-scheduling guard. | `TranslationManager.cs:200-238` |
| L-16 | Medium | `UntranslatedText` computes whitespace counts from the templated string and applies them to the original; the author left a `#warning`. Needs tests for templating that changes length. | `UntranslatedText.cs:230, 268-282` |
| L-17 | Medium | `TextTranslationCache` is not thread-safe for lookups while `SimpleTextTranslationCache` locks; reloads clear/refill under a lock that readers ignore. | `TextTranslationCache.cs:187-212, 1089`; `SimpleTextTranslationCache.cs:384` |
| L-18 | Low | `SafeFileWatcher` uses an unbalanced `Interlocked` counter; an exception between `Disable()` and `Enable()` can leave the watcher permanently disabled. | `SafeFileWatcher.cs:39-64` |
| L-19 | Low | `TextureTranslationInfo.NameToHash`, `AutoTranslationPlugin.CachedKeys`, `UtageHelper` fields, and similar static state are never reset on scene load/shutdown. | `TextureTranslationInfo.cs:18`; `AutoTranslationPlugin.cs:72, 2832-2835`; `Utilities/UtageHelper.cs:17-18` |
| L-20 | Low | Test coverage is minimal for the core: no tests for `TextTranslationCache`, `TextHelper`, the parser, directives, or prune. | `test/XUnity.AutoTranslator.Plugin.Core.Tests` |

### 4.1 Notes on certainty

- L-01/L-02/L-03 are structural and directly visible in the source.
- L-06 is only active when `ReloadTranslationsOnFileChange=True` (default `False`), but it is severe when enabled.
- L-05 applies when font overrides/texture translation are used; the `#warning` confirms the author’s awareness.
- L-17 depends on whether any lookup runs off the main thread. All current call sites are in `AutoTranslationPlugin`; treat off-main-thread hooks/asset loaders as the trigger to fix this.
- F-01 and the no-BOM result were verified with compiled tests.

---

## 5. Modern C# and maintainability

### 5.1 Constraints

- Target frameworks: `net35;net6.0`.
- `LangVersion` is `latest`, so compiler-only features are available even on net35.
- The real constraint is the net35 BCL: no `Span<T>`, no `ValueTuple`, no records/`init` without polyfills, no `IAsyncDisposable`.
- The code already uses `using var` (`TextTranslationCache.cs:147`, `SafeFileWatcher.cs:113`) and `??=` / collection expressions in IL2CPP-only code.

### 5.2 Safe modernizations

- `switch` expressions and pattern matching for directive/type switches.
- `??=` for the repeated `x ?? (x = ...)` lookup-ladder idiom.
- `readonly struct` for small value types such as `TranslationCharacterToken`.
- local/static local functions to break up `PerformInternalTrimming` and `TryGetTranslation`.
- `is null` / `is not null` in the lookup ladder.
- Target-typed `new()` is compiler-only and safe, but not worth a mass rewrite.

### 5.3 Avoid

- Records, `init`, tuples, `Span`/`Memory`, ranges/indices on net35.
- Nullable-reference-type churn before golden-file tests exist.
- Mass syntax-only rewrites: they add review noise and risk on a codebase with two runtimes.

### 5.4 Structural maintainability

- `AutoTranslationPlugin.cs` is ~3,657 lines; `TextTranslationCache.cs` is ~1,692; `TryGetTranslation` alone is ~530 lines.
- The file format is parsed in four places: `TextTranslationCache` (main + stream + KVP), `SimpleTextTranslationCache`, `SettingsTranslationsInitializer`, and `UIResizeCache`.
- The variation lookup ladder is duplicated between `TextTranslationCache` and `SimpleTextTranslationCache`.
- There are numerous broad `catch (Exception)` blocks and preprocessor branches. These make regressions hard to localise.
- Tests cover `UntranslatedText`, `TemplatedString`, `RomanizationHelper`, and one regex test; the cache/format/parser paths are untested.

Recommended refactors (after the safety tests):

1. Extract `TranslationFileReader` and make all four readers use it.
2. Extract a lookup-candidate generator shared by `TextTranslationCache` and `SimpleTextTranslationCache`.
3. Split `AutoTranslationPlugin` into partial files by responsibility (bootstrap, hooks, translation, texture, debug) without signature changes.
4. Replace the hand-rolled directive switches with one dictionary-based dispatch table.
5. Add golden-file tests before changing behaviour.

---

## 6. Implementation plan

### Phase 0 — Safety net (do first)

**Goal:** lock down the current format behaviour before changing it.

- [ ] Add `.txt`/`.zip` fixtures covering: `\=`, `\\`, `\n`, `\r`, `\uXXXX`, `%3D`, `//`, `r:`, `sr:`, `#set level`, `#set exe`, `#enable fallback`, CRLF/LF, BOM/no BOM, malformed lines.
- [ ] Add a round-trip test: for a corpus including URLs and `//`, assert `Decode(Encode(s)) == s`.
- [ ] Add a small cache test that loads a fixture and asserts forward/reverse/scoped lookups.
- [ ] Add a prune test that asserts regexes/comments/directives survive a rewrite.
- [ ] Decide whether the test project should get a `net6.0` target so the IL2CPP build of the core is compiled in CI.

**Exit criteria:** tests fail on the current `//` bug and prune data loss; they pass after Phase 1.

### Phase 1 — Format correctness

- [ ] Add `case '/': builder.Append('/'); break;` to `ReadTranslationLineAndDecode` (`TextHelper.cs`).
- [ ] Add a skipped-line/error counter and log a summary after load.
- [ ] Make prune preserve unrecognised lines and `r:`/`sr:` lines; only remove plain entries that are genuinely covered by a regex.
- [ ] Write prune output to a temp file and use `File.Replace`/atomic rename instead of `File.Move` + `FileMode.Create`.
- [ ] Do not prune automatically on every reload; gate it behind an explicit maintenance action or a `(length, mtime)` change check.
- [ ] Add tests for F-01..F-05.

**Exit criteria:** no data loss in prune tests; `//` round-trips; malformed lines are visible in the log.

### Phase 2 — Loader and large-file performance

- [ ] Replace `ReadToEnd().Split` in the core loaders with `ReadLine()` streaming.
- [ ] Pre-size `_translations` / `_reverseTranslations` on first load; keep capacity across reloads.
- [ ] Use one snapshot of `_translations` for the read-only derivation passes; remove redundant `ToList()` copies.
- [ ] Add a cheap whitespace check before constructing `UntranslatedText` in the variation pass.
- [ ] Add `if (input.IndexOf('<') < 0) return null;` to `RichTextParser.Parse`.
- [ ] Add a first-character/length prefilter or compiled-regex cap for large regex packs.
- [ ] Add a 100k-line and (manually) 1M-line benchmark with time, allocated bytes, and peak working set.

**Exit criteria:** no `ReadToEnd` in the core loaders; measured improvement on the benchmark; no behavioural change in golden-file tests.

### Phase 3 — Lifecycle and leaks

- [ ] Dispose every `PluginTextCaches` value in `AutoTranslationPlugin.OnDestroy`; clear the dictionary.
- [ ] Add `MaintenanceHelper.RemoveMaintenanceFunction` (or return an `IDisposable` registration) and use it from `TextTranslationCache.Dispose`.
- [ ] Unsubscribe `TextTranslationFileChanged` and clear dictionaries/collections in `TextTranslationCache.Dispose`.
- [ ] Unsubscribe the scene-loaded delegate in `TranslationScopeHelper` (store the delegate and remove it on shutdown).
- [ ] Make `SafeFileWatcher` disposable/cancellable: bounded wait, `Error` handler, unsubscribe `DirectoryUpdated`, guard callbacks after `_disposed`, coalesce events.
- [ ] Destroy superseded `Texture2D`, `Sprite`, and material copies; remove the stray `Instantiate(ui)` clone in `TextTranslationInfo.ChangeFont`.
- [ ] Call `Resources.UnloadUnusedAssets()` after reload and after texture scans.
- [ ] Dispose `XUnityWebClient`/`XUnityWebResponse` on timeout/completion; remove `Thread.Abort` and use a cooperative stop.
- [ ] Reset `Settings.IsShutdown` in `RebootPlugin` and re-enable the plugin.
- [ ] Reset/clear per-scene static caches on scene load/shutdown.

**Exit criteria:** a start/reload/shutdown cycle leaves no watcher, maintenance action, scene callback, or generated Unity object behind (verify with logs and, where possible, Unity play-mode checks).

### Phase 4 — Hot paths and correctness

- [ ] Replace the spam checker's per-frame `.Sum()` with a running total.
- [ ] Memoize the last `UntranslatedText` on `TextTranslationInfo`.
- [ ] Budget `KickoffTranslations` per frame.
- [ ] Hoist `WaitForSecondsRealtime` and cap stabilization coroutines.
- [ ] Fix `TranslationAggregatorWindow` indexing.
- [ ] Fix `_guiContentCheckFailed` assignment (should be `true`, not `false`).
- [ ] Add guards/logging for the dead `_shouldOverrideEntireFile` path; either implement override semantics or remove it.
- [ ] Dispose zip entry streams.
- [ ] Evict stale plugin caches.
- [ ] Add a duplicate guard in `ScheduleUnstartedJobs`; make iteration snapshot-based.
- [ ] Add `UntranslatedText` edge-case tests for templating + whitespace.

**Exit criteria:** no per-frame LINQ allocation in the steady state; UI index bug reproducible and fixed; no stale plugin cache after folder removal.

### Phase 5 — Optional sidecar / format evolution

Only if Phase 2 measurements still show unacceptable cold-start time:

- [ ] Define a sidecar format: header/version, source file size, mtime, hash, entry count, length-prefixed UTF-8 key/value blob, offset index.
- [ ] Load the sidecar only when it validates; otherwise fall back to the text parser and regenerate.
- [ ] Never make the sidecar authoritative; keep `.txt` editable.
- [ ] Add a command/tool to rebuild or delete the sidecar.

Do not replace `.txt` as the source of truth in this phase.

---

## 7. Test plan

### Unit tests

- Escape round-trip (`Encode`/`Decode`) including `//`, `=`, `\`, newlines, Unicode, `%3D`.
- `ReadTranslationLineAndDecode` malformed cases: missing `=`, extra `=`, comment in key, truncated `\u`.
- Directive parsing: `#set level`, `#set exe`, `#set required-resolution`, `#enable fallback`, unknown directives.
- Regex and splitter parsing, including malformed quotes.
- Cache lookup: original, externally trimmed, internally trimmed, fully trimmed, token, scoped, static, regex.
- Prune preservation: regexes, comments, directives, plain entries covered/not covered by regex.
- `SimpleTextTranslationCache` override/append semantics.

### Integration/benchmark tests

- 100k-line load: time, allocations, peak memory; assert a budget chosen from the first measurement.
- 100k-line reload with `ReloadTranslationsOnFileChange` off; assert no prune unless requested.
- A reload while a lookup is in progress (thread test) to expose L-17.
- Start/stop/start cycle: assert no duplicate maintenance actions or scene callbacks.

### Manual checks (Unity/IL2CPP)

- Texture translation on/off: verify replaced textures/sprites are released and the scene does not grow over time.
- TMP font override: verify the clone is gone and materials are bounded.
- Spam shutdown → reboot: verify `IsShutdown` resets and translation resumes.
- Large file + reload hotkey: verify no multi-second freeze and no regex/comment loss.

---

## 8. Risks and non-goals

### Non-goals

- Do not change the `.txt` source format in this plan.
- Do not break `SimpleTextTranslationCache` / `IReadOnlyTextTranslationCache` public API.
- Do not mass-modernise syntax without tests.
- Do not add `Span<T>`/records/tuples to shared code that must run on net35.

### Risks

- `ReadLine()` changes handling of `\r`-only files; add a fixture before swapping.
- Pre-sizing uses an estimate; benchmark to avoid over-allocating on small files.
- Prune preservation changes the meaning of “prune”; document the new behaviour.
- Unity asset destruction must not destroy objects still referenced by the game; only destroy objects created by XUAT.
- Thread-safety fixes can introduce main-thread stalls; prefer atomic reference swaps over coarse locks.
- Some findings are conditional (file watcher only when enabled; texture/font leaks only when those features are on). Validate the feature matrix before declaring a fix complete.

---

## Appendix A — Evidence index

### Format / large files

- `TextTranslationCache.cs:588` — `ReadToEnd().Split`
- `SimpleTextTranslationCache.cs:162` — same
- `TextHelper.cs:16-127` — line decode
- `TextHelper.cs:129-179` — encode/escape
- `TextTranslationCache.cs:280-314` — variation pass
- `TextTranslationCache.cs:321-385` — partial/token passes
- `TextTranslationCache.cs:518-568` — prune loads/rewrite
- `TextTranslationCache.cs:154-165` — backup/recreate
- `TextTranslationCache.cs:551-562` — regex/comment/directive drop
- `TextTranslationCache.cs:1089-1622` — lookup ladder
- `TextTranslationCache.cs:1524-1586` — regex scan and negative cache
- `TextTranslationCache.cs:1548-1552, 1581-1585` — negative-cache reset
- `AutoTranslationPlugin.cs:3195-3201` — always prune + reload

### Hot paths

- `AutoTranslationPlugin.cs:2845` — `SpamChecker.Update` per frame
- `SpamChecker.cs:224` — `.Sum()` per frame
- `AutoTranslationPlugin.cs:686` — full-scene `FindObjectsOfType`
- `AutoTranslationPlugin.cs:3179-3194` — per-hook `UntranslatedText`
- `TranslationManager.cs:200-227` — full job drain
- `AutoTranslationPlugin.cs:2541` — wait-object allocation per retry
- `Parsing/RichTextParser.cs:47-56` — regex split/TagRegex
- `TextTranslationCache.cs:280,338` — full `ToList()` snapshots

### Lifecycle / leaks

- `AutoTranslationPlugin.cs:67` — `PluginTextCaches`
- `AutoTranslationPlugin.cs:3615-3655` — `OnDestroy`
- `TextTranslationCache.cs:80` — maintenance registration
- `MaintenanceHelper.cs:24-37,47-75` — no unregister
- `TextTranslationCache.cs:1671-1683` — `Dispose` only watcher
- `TranslationScopeHelper.cs:74,92` — scene callback
- `TextTranslationInfo.cs:156` — `Instantiate(ui)` clone
- `TextureTranslationInfo.cs:69,85` — texture creation
- `SafeFileWatcher.cs:99-107,127-146` — unbounded wait/dispose
- `Endpoints/Http/HttpEndpoint.cs:84` — per-translation client
- `Endpoints/ExtProtocol/ExtProtocolEndpoint.cs:385-390` — `Thread.Abort`
- `UI/TranslationAggregatorWindow.cs:108-125` — index mismatch
- `Extensions/ComponentExtensions.cs:314-323` — failure flag
- `Configuration/Settings.cs:61` — `IsShutdown`
- `AutoTranslationPlugin.cs:2947-2956` — reboot
- `TextTranslationCache.cs:757-770` — zip streams
- `AutoTranslationPlugin.cs:600-623` — stale plugin caches
- `TranslationManager.cs:200-238` — list mutation/reentrancy
- `UntranslatedText.cs:230,268-282` — whitespace/templating warning

### Verified checks

- `//` round-trip: compiled `TextHelper` + test program; `http://a//b` encoded to `http:\/\/a\/\/b` and decoded unchanged (bug confirmed).
- Append BOM: compiled .NET Framework test; `StreamWriter` on a non-empty `FileMode.Append` stream wrote no BOM (theory ruled out).
- `HookingHelper` changes from the previous task were compiled against stubs for both `IL2CPP` and managed configurations.

---

## Appendix B — Suggested first pull request

Keep the first PR small and reviewable:

1. `TextHelper.cs`: add `case '/'` and a round-trip test.
2. `TextTranslationCache.cs`: preserve regexes/comments/directives during prune; write via temp file + atomic replace; add a skipped-line counter.
3. Tests: fixture-based round-trip, prune, and malformed-line tests.

This addresses the highest-risk data corruption without requiring a format change or touching the public API.
