# XUAT Core — Performance, Large-File, Bug/Leak Analysis and Remediation Plan

- **Status:** analysis + implementation proposal (v2 — deeper live-edit, storage, lookup, threading, and memory coverage)
- **Revision:** v2, 2026-10-18
- **Scope:** `src/XUnity.AutoTranslator.Plugin.Core` (non-provider code), plus the `src/XUnity.Common` pieces the core depends on
- **Date:** 2026-10-18
- **Method:** source review, targeted compile/run checks, static scans, and a follow-up review of reload, storage, lookup, threading, and memory paths. No benchmark numbers have been measured yet; the estimates below are planning inputs.

Implementation status (updated 2026-10-18): the first implementation pass landed the following core work:

- **Phase 1 (format correctness):** `//` escape round-trip (F-01), malformed-line counters/logging (F-04), lossless prune that preserves comments/directives/`r:`/`sr:` lines (F-02), and atomic temp-file + `File.Replace` rewrites with an external-edit guard (F-03). `%3D` remains intentionally legacy-lossy (F-05).
- **Phase 2 (loader):** shared `ReadLine()` streaming reader used by all four text readers, first-load dictionary pre-sizing, whitespace guard before variation construction, rich-text `<` guard, and LF-safe static translations (F-08).
- **Phase 2.5 L1:** per-file `TranslationFileSet` cache keyed by path + length + mtime; unchanged files are not re-parsed. L2 delta application and L3 background snapshot publishing are **not** implemented.
- **Phase 3 (lifecycle):** `MaintenanceHelper.RemoveMaintenanceFunction`, cache disposal/unsubscription/clearing, plugin-cache disposal and stale-cache eviction, removable scene-load callback, debounced subdirectory watcher with error recovery/own-write suppression, full `Settings.IsShutdown` / translation-count / spam-counter reset on reboot, and `Resources.UnloadUnusedAssets()` after reload/texture scans.
- **Phase 3.5 (writer):** shared `TranslationWriter` for append/rewrite, atomic replace with rotating backups, BOM/newline preservation, external-edit detection, partial-line repair before append, own-write watcher suppression, removal of the dead `_shouldOverrideEntireFile` path, and zip-entry stream disposal.
- **Phase 4 (selected hot paths):** spam-checker running total, per-frame job-start budget, duplicate unstarted-job guard/snapshot iteration, a bounded two-generation negative regex cache, and the `_guiContentCheckFailed` latch fix.

A modern managed target (`net471`) was added alongside the existing `net35` and `net6.0` targets, with a CI compile check and a documented support policy (`docs/target-framework-support.md`). `net35` remains the default release artifact.

Not implemented in this pass: L2/L3 incremental snapshots, benchmark harness and measurements, the lookup-candidate loop and regex index, speculative Unity asset destruction, sidecar format, and the provider-throughput track. The 5.6.4 UI pass fixed L-09; UI work is tracked in `docs/ui-scaling-and-redesign-plan.md`. The original checkboxes below describe the full proposal and are not individually re-marked here.

What changed in v2: added the goal-coverage verdict (§0.1), the ranked remaining bottlenecks (§0.2, §9), a concrete incremental live-reload design (§2.7), the memory-vs-sidecar decision (§2.8), storage/conflict semantics (§2.9), lookup/threading/provider analysis (§3.1–3.3), a target architecture (§5.5), benchmark/test gates (§6, §7), and refreshed line references/status (§4.1, Appendix A).

---

## 0. Executive summary

1. **The translation file format is not the main bottleneck.** The per-lookup data structure is already dictionary-based. The costs are in loading, deriving variations/tokens, pruning, and reloading.
2. **Do not switch the source format to JSON or SQLite.** `key=value` `.txt` is a de-facto public contract: users hand-edit it and community packs depend on it. JSON is larger/slower and loses comments/directives; SQLite adds native/IL2CPP dependencies and produces unmergeable files. If cold start remains a problem, add an optional compiled sidecar while keeping `.txt` as the source of truth.
3. **The highest-impact large-file fixes are:** stream the parser, pre-size dictionaries, add cheap guards before the eager variation/token passes, stop pruning on every reload, and avoid repeated full-dictionary copies.
4. **The highest-risk correctness issues are:** the `//` escape round-trip bug, destructive/non-atomic prune, the permanent `Settings.IsShutdown` latch, unsynchronized `TextTranslationCache` reads, and lifecycle leaks (maintenance actions, scene callbacks, native Unity assets).
5. **Modern C# is not the problem.** The compiler is set to `latest`, but net35's BCL limits runtime features. The bigger maintainability issue is duplicated parsing/lookup logic and very large classes/methods.

---

## 0.1 Verdict: what this plan optimizes, and what it does not

The plan is a **core text-pipeline remediation plan**, not a complete “make the app fast” plan. It is strongest on format correctness, storage safety, and lifecycle bugs. It only partially covers fast loading, and it does **not** yet contain a real incremental live-edit design.

| Goal | Coverage | What the plan covers | What is still missing |
|---|---|---|---|
| **Fast text loading** | Partial | Phase 2: `ReadLine()` streaming, dictionary pre-sizing, fewer `ToList()` snapshots, whitespace/`<` guards, regex prefilter | O(N) variation/partial/token passes still run; every reload reparses everything; all strings remain resident; the sidecar is optional and unmeasured |
| **Live editing** | Weak | Phase 1 gates prune; Phase 3 fixes watcher/lifecycle bugs | No per-file diff, no incremental index update, no atomic snapshot publish. A change still becomes `PruneMainTranslationFile()` + full `LoadTranslations(true)`. The watcher misses subdirectories, and plugin caches have no watcher |
| **Storing** | Partial | Phase 1 fixes `//`, preserves comments/directives/regexes during prune, uses atomic replace; Phase 3.5 adds a unified writer | No external-edit conflict detection, no crash/partial-append recovery, no unified writer yet; `SimpleTextTranslationCache` override path is dead; zip entry streams are not disposed |
| **Lookup** | Partial | Phase 4 adds cheap guards; Phase 2 adds a regex prefilter | The ~530-line lookup ladder and linear regex scan remain; the negative cache resets wholesale; lookups and reloads do not use a shared snapshot/threading model |
| **Lifecycle / leaks** | Strong on paper | Phase 3 covers caches, watchers, scene callbacks, Unity object clones, endpoints, and the `IsShutdown` latch | Not implemented. L-09 was fixed separately by the UI pass |
| **Whole-app speed** | No | — | Provider/network latency, batching/concurrency, texture translation throughput, resource loading, and UI are separate workstreams |

**Bottom line:** implement Phase 0–2 for safer, faster loading; implement the new Phase 2.5 for live editing; do **not** expect the format or a sidecar to solve reload latency by themselves.

## 0.2 Biggest remaining bottleneck

1. **Reload = prune + full reparse.** `TextCache_TextTranslationFileChanged` only sets `_translationReloadRequest` (`AutoTranslationPlugin.cs:276-279`); the next `Update` calls `ReloadTranslations()` (`:2873-2877`), which always runs `TextCache.PruneMainTranslationFile()` followed by `LoadTranslations(true)` (`:3241-3247`). `LoadTranslations` then clears and refills every cache (`TextTranslationCache.cs:187-239`) and repeats the variation/token passes. Nothing is incremental.
2. **Load-time derivation and allocations.** Even with `ReadLine()`, the variation pass (`TextTranslationCache.cs:276-318`), optional partial pass (`:321-333`), token pass (`:335-387`), and the per-line `StringBuilder`/`ToString()` work in `TextHelper` remain O(N) with significant allocation churn.
3. **Resident memory.** Forward + reverse maps, variation entries, token/partial entries, scoped dictionaries, and duplicate string data keep growing. A sidecar improves cold start only; it does not reduce the resident set.
4. **Lookup and regex worst cases.** `TryGetTranslation` is ~530 lines (`:1089-1622`), regexes are scanned linearly in reverse order (`:1519-1587`), and the negative cache is discarded wholesale at 10k entries (`:1548-1552, 1581-1585`).
5. **Storage rewrite and external-edit behavior.** Append is cheap, but prune is a read-all/rewrite-all operation and is not atomic (`TextTranslationCache.cs:154-165`); concurrent user edits and crash/partial-append recovery are not modeled.
6. **Provider latency.** Outside this plan, but often the dominant user-perceived delay. `KickoffTranslations` drains work per frame (`TranslationManager.cs:200-227`), while endpoint latency, rate limits, and batching dominate wall-clock time.

If only one architectural change is made for live editing, make reload **incremental**. If only one change is made for cold start, stream and pre-size. If memory is the constraint, do not start with a sidecar; start with measurement and a compact/lazy-derived store.

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

Evidence: `AutoTranslationPlugin.cs:3241-3247`.

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

### 2.7 Live-edit / reload architecture (the missing piece)

Current live-edit path:

```
FileSystemWatcher
  -> TextCache_TextTranslationFileChanged()       sets _translationReloadRequest
  -> AutoTranslationPlugin.Update()
  -> ReloadTranslations()
       -> TextCache.PruneMainTranslationFile()
       -> LoadTranslations(true)
            -> TextCache.LoadTranslationFiles()    clears and refills everything
            -> every PluginTextCaches value.LoadTranslationFiles()
            -> TextureCache.LoadTranslationFiles()
```

Evidence:

- `AutoTranslationPlugin.cs:257` subscribes `TextCache.TextTranslationFileChanged`; the subscription is never removed in `OnDestroy` (`:3661-3701`).
- `AutoTranslationPlugin.cs:276-279` only sets a flag; `:2873-2877` invokes `ReloadTranslations`; `:3241-3247` always prunes and reloads.
- The watcher is created only in the parameterless `TextTranslationCache` constructor (`TextTranslationCache.cs:57-81`). Plugin caches constructed with a `DirectoryInfo` (`:88-104`) have no watcher.
- `SafeFileWatcher` creates `new FileSystemWatcher(_directory)` without `IncludeSubdirectories` (`SafeFileWatcher.cs:31`), so changes under `Translation/{lang}/Text/plugins/...` are missed.
- `SafeFileWatcher` has no `Error` handler, no disposed guard in `RaiseEvent` (`:49-52`), and `WaitForFile` blocks unboundedly in 100 ms sleeps (`:99-107`).
- `LoadTranslationFiles` clears every dictionary and reparses every file (`TextTranslationCache.cs:187-239`); the loader uses `ReadToEnd().Split` (`:588`).
- `PruneMainTranslationFile` is called even when the event was a pure edit, not a prune request.

**Target design: per-file sets + an incrementally updated snapshot.**

```
watcher (coalesced, debounced)
  -> changed paths
  -> parse only changed files into TranslationFileSet
  -> apply delta to TranslationIndex (source refs / precedence)
  -> publish immutable TranslationSnapshot atomically
  -> lookups read the current snapshot
```

Data model:

- `TranslationFileSnapshot`: path, size, last-write time, hash, load order, kind (txt/zip).
- `TranslationFileSet`: plain entries, regexes, splitter regexes, directives, scopes, raw lines for lossless rewrite.
- `SourceRef`: file id, load order, line, entry kind. Plain keys are last-wins by file order; regexes are first-wins by pattern; directives/scopes are per-file. Preserve the existing precedence exactly.
- `TranslationIndex`: merged lookup maps, reverse/token/partial maps, regex lists, scoped maps. Each entry carries the winning `SourceRef`.
- `TranslationSnapshot`: the published, read-only view. Lookups capture the snapshot once per call.

Apply-delta rules:

1. For every key/regex in the **old** file set, remove its source reference. If it was the winning source, select the next source by precedence; if none, remove the key.
2. For every key/regex in the **new** file set, add its reference and recompute the winner only when its precedence beats the current winner.
3. Regenerate derived variation/token/partial entries only for keys whose winner changed; remove derived entries whose source disappeared.
4. Rebuild regex/splitter lists only when their file set changed; these lists are usually much smaller than the plain corpus.
5. Reparse directives/scopes for the changed file; update the scoped maps for that file.
6. Publish the new snapshot with `Interlocked.Exchange` (or a very short lock around the reference swap). Parse and build off the lookup path; never publish a partially applied index.

**Staged implementation (avoid a big-bang rewrite):**

- **L1 — changed-file parse cache:** skip files whose `(size, mtime, hash)` did not change; parse only changed files into per-file sets; rebuild the merged index from the per-file sets. This removes file I/O and parsing for unchanged files, but the merge is still O(total entries). Measure this first.
- **L2 — delta apply:** add source refs and apply the per-key delta above, so reload cost is O(changed entries + affected derived entries + regex-list rebuild).
- **L3 — background parse + atomic publish:** parse changed files on a worker, publish a snapshot, and coalesce watcher events. No Unity APIs on the worker; use `Stopwatch`, not `Time.realtimeSinceStartup`.

Do not attempt L2/L3 until L1 is benchmarked; L1 may be sufficient for typical community packs.

**Watcher requirements:**

- `IncludeSubdirectories = true`; filter `.txt`/`.zip`; ignore `.bak`, temp files, and the plugin's own writes.
- Handle `Renamed`, `Deleted`, and `Error`; recreate the watcher after an overflow.
- Debounce/coalesce for 250–500 ms; parse only after `(size, mtime)` is stable; bound `WaitForFile` with a timeout and retry.
- Route events to the owning cache (global vs plugin folder) or use one watcher service with a path→cache map.
- Plugin caches need their own watcher or must be registered with the global router.
- Never call `PruneMainTranslationFile` on a watcher-driven reload; prune is an explicit maintenance action.

**Failure behavior:** if parsing or delta application fails, keep the old snapshot, log the file and line, and retry on the next event. Never clear the lookup index before the replacement is ready.

### 2.8 Memory budget and the sidecar decision

The sidecar is often discussed as if it solves large-file performance. It solves **cold-start parse time**, not resident memory, lookup, or live-edit latency.

| Constraint | Sidecar helps? | What actually helps |
|---|---|---|
| Cold start / first load | Yes | Sidecar plus streaming fallback |
| Resident memory | No | Compact store, fewer derived entries, lazy token/partial generation, per-file caches, memory budget |
| Lookup latency | No (usually worse if on disk) | In-memory dictionaries, candidate generator, regex prefilter |
| Live editing | Only per changed file | Incremental reload; a whole-file sidecar is invalidated on every edit |
| 32-bit Mono | No | Budget/limit, compact store, avoid duplicate maps and eager variations |

Rough planning estimate (must be measured):

- Each plain entry contributes a key and a value string, one forward dictionary slot, and one reverse dictionary slot.
- Variation entries add extra keys and possibly extra values; token/partial entries add another set.
- Scoped dictionaries create nine collections per scope (empty until used).
- 100k lines is usually manageable; 1M lines can plausibly reach several hundred MB to ~1 GB and is a serious 32-bit risk.

If a sidecar is added later:

- Keep `.txt` authoritative; the sidecar is a validated read cache only.
- Per source file, not one global file: header/version, source size, mtime ticks, hash, entry count, flags, length-prefixed UTF-8 key/value blob, sorted offset index.
- Validate size + mtime + hash; on mismatch, fall back to the text parser and regenerate.
- Skip the sidecar for small files and for files edited frequently; it is a cold-start optimization.
- Delete the sidecar when the source file is deleted; rebuild it on the writer's atomic rewrite.
- Never serialize .NET object graphs; a versioned, little-endian binary format is enough.

If resident memory is the real constraint, prioritize: pre-size dictionaries, avoid full `ToList()` snapshots, generate variations/tokens lazily or only when enabled, avoid duplicate reverse maps where a `HashSet<string>` suffices, cap plugin caches, and add a measured memory budget. A sidecar does not help here.

### 2.9 Storage writer, prune, and external edits

Current write paths:

- `TextTranslationCache.SaveNewTranslationsToDisk` appends under `_writeToFileSync`, disables the watcher during the append, and writes `TextHelper.Encode(key) + '=' + TextHelper.Encode(value)` (`TextTranslationCache.cs:814-843`). Append is cheap and generally safe for small writes, but a crash can leave a partial line and there is no external-edit check.
- `PruneMainTranslationFile` reads/writes through a `MemoryStream`, moves the original to a timestamped backup, then recreates the file (`:140-169`). `PruneTranslationFile` (`:500-571`) loads every other translation file for regexes, drops `r:`/`sr:`/comment/directive lines, and re-encodes surviving lines. It is not atomic and can lose the file on a crash between `File.Move` and `FileMode.Create`.
- `SimpleTextTranslationCache` has a second writer (`SimpleTextTranslationCache.cs:270-361`): a coroutine delay, a thread-pool write, a static `_writeToFileSync`, and a `_shouldOverrideEntireFile` branch that is set only when `hadTranslated` is true but only entered when it is false (`:282-344`). The override path is dead.

Target writer:

- One `TranslationWriter` used by both caches; no per-cache writer logic.
- Coalesce writes for ~0.5–1 s; append complete lines only; `Flush` before close; keep the file handle short-lived.
- Before append/rewrite, compare `(size, mtime, hash)` with the last known snapshot. If an external edit changed the file, reload/merge or abort with a warning; do not silently clobber.
- Prune/rewrite: write a temp file in the same directory, flush, then `File.Replace(temp, target, backup)`; preserve the original BOM and newline style; keep a rotating backup; validate the result before deleting the temp.
- Preserve unknown lines, comments, directives, and `r:`/`sr:` lines; only remove plain entries that are genuinely covered by a regex. Log a summary and support a dry run.
- Detect and report partial/malformed lines; optionally repair a trailing incomplete line at startup, but never silently drop it.
- Suppress the watcher for the plugin's own writes, or tag events so they cannot trigger a reload loop.
- Zip files are read-only sources; dispose `ZipFile.GetInputStream` streams (`TextTranslationCache.cs:757-770`).

Multi-process caveat: the plugin cannot fully coordinate with an external text editor. A per-file lock plus reload-before-write is the practical mitigation; a full transaction/journal is unnecessary for a text source of truth.

---

## 3. Hot-path performance

| ID | Severity | Finding | Evidence |
|---|---|---|---|
| H-01 | Medium | `SpamChecker.Update` runs every frame and `ResetThresholdTimerIfRequired` does a LINQ `.Sum()` over the rolling window every frame. | `AutoTranslationPlugin.cs:2864`; `SpamChecker.cs:46-50, 224` (also `:191`) |
| H-02 | Medium | `UpdateSpriteRenderers` performs a full-scene `FindObjectsOfType<SpriteRenderer>` when a texture hook requests a refresh. It is one-shot per trigger, but a large spike in big scenes. | `AutoTranslationPlugin.cs:676-705, 686` |
| H-03 | Medium | `GetCacheKey` builds a fresh `UntranslatedText` for every non-spam text change; the constructor performs multiple scans/substrings. | `AutoTranslationPlugin.cs:3225-3238`; `UntranslatedText.cs:195-330` |
| H-04 | Medium | `KickoffTranslations` drains all unstarted jobs in a single frame. | `TranslationManager.cs:200-227` |
| H-05 | Medium | `TranslateByEndpoint` starts an uncapped stabilization coroutine per text change and allocates a new `WaitForSecondsRealtime` per retry. | `AutoTranslationPlugin.cs:2377-2432, 2533, 2582` |
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

### 3.1 Lookup and index design

The lookup ladder is correct but pessimistic: every miss walks every variation, and every regex miss scans the full regex list. Treat it as a candidate-generation problem.

Current costs:

- ~8+ dictionary probes for a miss across scoped/unscoped token and full variants.
- `key.Untemplate(...)` is recomputed for each variation; each call is a `Replace` over the full string.
- Whitespace-difference hits call `AddTranslationToCache`, which constructs two more `UntranslatedText` objects (`TextTranslationCache.cs:1015-1024`).
- Regexes are scanned linearly in reverse order; the negative cache is a `HashSet` reset wholesale at 10k entries (`:1519-1587`).
- `TryGetTranslation` is ~530 lines with hand-unrolled permutations (`:1089-1622`).

Target:

1. Build an ordered `LookupCandidate` list once per `UntranslatedText`: scoped token, scoped full, unscoped token, unscoped full, static, regex. Include the untemplated variants in the candidate object so `Untemplate` is not repeated.
2. Replace the hand-unrolled ladder with a single loop over candidates. Keep the existing order and logging labels (`c1`..`c12`) in tests so behavior is provably unchanged.
3. Regex index: partition patterns into literal-prefix buckets, first-character buckets, and a generic list; test only the buckets that can match. Cap `RegexOptions.Compiled` for very large packs; keep a count and scan-time metric.
4. Negative cache: use a segmented/bounded LRU (or generation-stamped buckets) instead of `new HashSet` at 10k. Do not let a miss repeatedly pay the full regex scan.
5. Reverse maps: `IsTranslation` only needs `ContainsKey`; `TryGetReverseTranslation` needs the key. Keep the reverse dictionary where needed, but do not add a second full map for data that is only membership-tested.
6. Scoped dictionaries: keep lazy creation; measure scoped hit/miss cost. Nine empty collections per scope are acceptable only if scope count is small.
7. Add metrics: hit/miss by candidate, regex scans per lookup, time in regex, negative-cache size, and whitespace-difference writes.

Do this only after golden-file tests exist; reordering candidates changes subtle precedence.

### 3.2 Threading and concurrency model

Current state:

- `TextTranslationCache` lookups do not take `_writeToFileSync`; reloads clear/refill under that lock (`TextTranslationCache.cs:187-239`). This is safe only while every lookup is on the main thread.
- `SimpleTextTranslationCache.TryGetTranslation` uses `lock(_translations)` (`SimpleTextTranslationCache.cs:384`); the two caches have different threading contracts.
- The maintenance thread writes new translations (`TextTranslationCache.cs:80, 814-843`); the watcher callback and `AutoTranslationPlugin.Update` can request reloads; `Dispose` can run during a write.
- `AutoTranslationPlugin` subscribes to `TextTranslationCache.TextTranslationFileChanged` but never unsubscribes (`AutoTranslationPlugin.cs:257, 3661-3701`).
- `MaintenanceHelper` has no remove API and holds its global `Sync` lock while invoking every action (`XUnity.Common/Utilities/MaintenanceHelper.cs:24-75`).

Target:

- Publish an immutable `TranslationSnapshot`; lookups do `var snapshot = _snapshot;` once and never lock. Build the new snapshot off the lookup path; swap with `Interlocked.Exchange`.
- Parse changed files on a worker thread; do not call Unity APIs there. Use `Stopwatch` instead of `Time.realtimeSinceStartup`.
- Writers go through one queue; the writer owns file handles, watcher suppression, conflict checks, and retry/backoff.
- `Dispose` should stop the watcher, unregister maintenance, clear the event, cancel/drain the writer, and release the snapshot; no callback may fire after disposal.
- Add a concurrency test: reload while lookups run; dispose while a write is queued; watcher event after dispose.
- Keep a documented main-thread-only mode if a full snapshot model is too large for the first PR: assert the caller thread in debug builds and keep reload on the main thread until Phase 2.5 lands.

### 3.3 Provider throughput (outside the core text plan)

The text pipeline is not the only source of perceived latency. `TranslationManager.KickoffTranslations` drains all unstarted jobs for each endpoint in one frame (`TranslationManager.cs:200-227`); `HandleNextJob`/`HandleNextBatch` start work until the endpoint is busy. Endpoint latency, rate limits, retry/backoff, batching, and connection setup often dominate the user-visible wait.

A separate throughput plan should cover:

- Per-endpoint concurrency limits and a per-frame start budget.
- Batching and rate-limit-aware scheduling; retry/backoff with jitter.
- Cancellation and shutdown without `Thread.Abort` (`Endpoints/ExtProtocol/ExtProtocolEndpoint.cs:385-390`).
- HTTP client reuse/disposal (`Endpoints/Http/HttpEndpoint.cs:84-89`).
- Metrics: queue depth, in-flight requests, p50/p95 latency, error rate, rate-limit waits.
- A budget for `SpamChecker.Update` and other per-frame work (`AutoTranslationPlugin.cs:2864`; `SpamChecker.cs:46-50, 224`).

Do not fold this into the text-cache phases; it has different risks and its own benchmark.

---

## 4. Lifecycle, bugs, and leaks

Severity legend: Critical = data loss/crash/feature permanently broken; High = leak or user-visible failure; Medium = correctness/robustness risk; Low = cleanup/polish.

| ID | Severity | Finding | Evidence |
|---|---|---|---|
| L-01 | Critical | Plugin shutdown does not dispose `PluginTextCaches`; each cache owns a file watcher and a maintenance action. | `AutoTranslationPlugin.cs:67, 3661-3701` |
| L-02 | Critical | `MaintenanceHelper` has no unregister API. Every `TextTranslationCache` registers `SaveNewTranslationsToDisk`; disposed caches can keep writing from the maintenance thread. | `XUnity.Common/Utilities/MaintenanceHelper.cs:24-75`; `TextTranslationCache.cs:80` |
| L-03 | Critical | `TextTranslationCache.Dispose` only disposes the file watcher; it does not clear caches or unsubscribe `TextTranslationFileChanged`. | `TextTranslationCache.cs:1671-1683` |
| L-04 | Critical | `SceneManager.sceneLoaded` is subscribed with a closure and never unsubscribed; the static callback keeps the plugin instance alive and fires after destruction. | `Utilities/TranslationScopeHelper.cs:71-93` |
| L-05 | Critical | Native Unity assets are created without an owner and never destroyed: a full `Instantiate(ui)` clone per font change, translated/original `Texture2D`s, and generated sprites. No `Resources.UnloadUnusedAssets()` exists in the project. | `TextTranslationInfo.cs:156`; `TextureTranslationInfo.cs:69,85` (author `#warning HideAndDontSave?` at `:70`); grep finds no `Resources.UnloadUnusedAssets` |
| L-06 | High | `SafeFileWatcher` blocks a callback thread in an unbounded `while (IsFileLocked) Thread.Sleep(100)`, has no `Error` handler, and `Dispose` does not unsubscribe `DirectoryUpdated` or guard callbacks against a disposed timer. | `SafeFileWatcher.cs:99-107, 127-146` |
| L-07 | High | `HttpEndpoint` allocates a new `XUnityWebClient` per translation and does not dispose it; timed-out requests can keep handlers/thread-pool work alive. | `Endpoints/Http/HttpEndpoint.cs:84-89` |
| L-08 | High | `ExtProtocolEndpoint.Dispose` calls `Thread.Abort()` and can dereference a null `_thread`. | `Endpoints/ExtProtocol/ExtProtocolEndpoint.cs:385-390` |
| L-09 | High | **Fixed in 5.6.4.** `TranslationAggregatorWindow` now resolves the scroll view by `TranslatorViewModel` reference instead of indexing `_translationViews[i]`, removing the `IndexOutOfRangeException` path. | `UI/TranslationAggregatorWindow.cs` (`GetTranslationView`) |
| L-10 | High | `ComponentExtensions.TryGetTextFromGUIContentSafe` sets `_guiContentCheckFailed = false` in its catch, re-enabling a failing path instead of latching it. | `Extensions/ComponentExtensions.cs:314-323` |
| L-11 | High | `Settings.IsShutdown` is a one-way latch; `RebootPlugin` only resets endpoint error counters, so a spam shutdown cannot be recovered without restarting the game. | `Configuration/Settings.cs:61`; `AutoTranslationPlugin.cs:2993-3002`; no `IsShutdown = false` elsewhere |
| L-12 | Medium | `SimpleTextTranslationCache._shouldOverrideEntireFile` is set only when `hadTranslated` is true, but the only caller enters that path when `hadTranslated` is false. The full-rewrite branch is dead. | `SimpleTextTranslationCache.cs:270-296, 320-344` |
| L-13 | Medium | Zip entry streams from `ZipFile.GetInputStream` are not disposed; only the outer `ZipFile` is closed. | `TextTranslationCache.cs:757-770` |
| L-14 | Medium | `PluginTextCaches` is never evicted when a plugin translation folder disappears; stale caches and their maintenance actions remain. | `AutoTranslationPlugin.cs:600-623` |
| L-15 | Medium | `TranslationManager` mutates `_endpointsWithUnstartedJobs` while iterating it in reverse and has no duplicate-scheduling guard. | `TranslationManager.cs:200-238` |
| L-16 | Medium | `UntranslatedText` computes whitespace counts from the templated string and applies them to the original; the author left a `#warning`. Needs tests for templating that changes length. | `UntranslatedText.cs:230, 268-282` |
| L-17 | Medium | `TextTranslationCache` is not thread-safe for lookups while `SimpleTextTranslationCache` locks; reloads clear/refill under a lock that readers ignore. | `TextTranslationCache.cs:187-212, 1089`; `SimpleTextTranslationCache.cs:384` |
| L-18 | Low | `SafeFileWatcher` uses an unbalanced `Interlocked` counter; an exception between `Disable()` and `Enable()` can leave the watcher permanently disabled. | `SafeFileWatcher.cs:39-64` |
| L-19 | Low | `TextureTranslationInfo.NameToHash`, `AutoTranslationPlugin.CachedKeys`, `UtageHelper` fields, and similar static state are never reset on scene load/shutdown. | `TextureTranslationInfo.cs:18`; `AutoTranslationPlugin.cs:72, 2851-2853`; `Utilities/UtageHelper.cs:17-18` |
| L-20 | Low | Test coverage is minimal for the core: no tests for `TextTranslationCache`, `TextHelper`, the parser, directives, or prune. | `test/XUnity.AutoTranslator.Plugin.Core.Tests` |

### 4.1 Status snapshot

| Area | Status |
|---|---|
| F-01..F-08 (format/edge cases) | Open |
| H-01..H-10 (hot paths) | Open |
| L-01..L-08, L-10..L-20 (lifecycle/leaks) | Open |
| L-09 (`TranslationAggregatorWindow` index) | **Fixed in the 5.6.4 UI pass** — views are now looked up by `TranslatorViewModel` reference, not array index |
| Phase 0 fixtures/tests | Not present |
| Benchmarks (100k/1M) | Not run |
| Phase 1–5 core work | Not implemented |

The UI pass also touched `AutoTranslationPlugin` and `Settings`, so line numbers in the evidence index drift by roughly 50–60 lines in that file. Prefer symbol names when searching; the line references are a convenience, not a contract.

### 4.2 Notes on certainty

- L-01/L-02/L-03 are structural and directly visible in the source.
- L-06 is only active when `ReloadTranslationsOnFileChange=True` (default `False`), but it is severe when enabled.
- L-05 applies when font overrides/texture translation are used; the `#warning` confirms the author’s awareness.
- L-17 depends on whether any lookup runs off the main thread. All current call sites are in `AutoTranslationPlugin`; treat off-main-thread hooks/asset loaders as the trigger to fix this.
- F-01 and the no-BOM result were verified with compiled tests.

---

## 5. Modern C# and maintainability

### 5.1 Constraints

- Target frameworks: `net35;net471;net6.0`. `net35` remains the default managed target; `net471` is the opt-in modern managed target; `net6.0` is IL2CPP. See `docs/target-framework-support.md`.
- `LangVersion` is `latest`, so compiler-only features are available even on net35.
- The real constraint is the net35 BCL: no `Span<T>`, no `ValueTuple`, no records/`init` without polyfills, no `IAsyncDisposable`. Shared code must remain net35-compatible; the additive `net471` target does not remove this constraint.
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

### 5.5 Target architecture (minimum viable)

Do not rewrite the cache in one step. Extract the seams first, then move behavior behind them.

```
 files (.txt/.zip)
      |
      v
 TranslationFileReader  (one parser for all four readers)
      |
      v
 TranslationFileSet     (plain entries, regexes, directives, scopes, raw lines)
      |
      v
 TranslationIndex       (merged maps + source refs + derived token/variation entries)
      |
      v
 TranslationSnapshot    (immutable published view; atomic swap)
      |
      v
 lookups                (TryGetTranslation / reverse / regex / scoped)

 TranslationFileWatcher -> changed paths -> incremental reload
 TranslationWriter      -> append/rewrite/prune -> files
```

Minimum-viable components:

- **`TranslationFileReader`**: one `ReadLine()`-based parser shared by `TextTranslationCache`, `SimpleTextTranslationCache`, `SettingsTranslationsInitializer`, and `UIResizeCache`; returns entries + directives + regexes + parse diagnostics.
- **`TranslationFileSet`**: per-file ownership; enables incremental reload and provenance.
- **`TranslationIndex`/`TranslationSnapshot`**: merged view with source refs; lookups take a snapshot. Start with a lock-free snapshot reference; keep the old dictionaries behind it for compatibility.
- **`TranslationWriter`**: one append/rewrite/prune implementation with atomic replace and conflict checks.
- **`TranslationFileWatcher`**: one watcher service with debounce, subdirectories, error recovery, and own-write suppression.

Compatibility constraints:

- Keep `.txt`, `IReadOnlyTextTranslationCache`, and `SimpleTextTranslationCache` public behavior.
- Keep last-wins plain keys, first-wins regexes, directives/scopes, and `//`/`%3D` semantics.
- Do not make the sidecar authoritative.
- net35: no `Span`, records, tuples, or `IAsyncDisposable`; `Interlocked.Exchange` and `Stopwatch` are available.

First refactor boundary: replace the four readers with `TranslationFileReader` **without changing behavior**, then add the file-set/index layers. This gives golden-file coverage before performance work.

---

## 6. Implementation plan (v2)

Status: Phase 1, Phase 2 (non-benchmark parts), Phase 2.5 L1, Phase 3, Phase 3.5, and selected Phase 4 items are implemented. L2/L3, benchmarks, lookup-candidate/regex-index work, speculative asset destruction, the sidecar, and provider throughput remain open. L-09 is already fixed by the 5.6.4 UI pass.

### Phase 0 — Safety net and baseline (do first)

**Goal:** lock down current behaviour and measure before optimizing.

- [ ] Add `.txt`/`.zip` fixtures covering: `\=`, `\\`, `\n`, `\r`, `\uXXXX`, `%3D`, `//`, `r:`, `sr:`, `#set level`, `#set exe`, `#enable fallback`, CRLF/LF, BOM/no BOM, malformed lines, duplicate keys, and last-wins precedence.
- [ ] Add a round-trip test: for a corpus including URLs and `//`, assert `Decode(Encode(s)) == s`.
- [ ] Add a small cache test that loads a fixture and asserts forward/reverse/scoped/token lookups.
- [ ] Add a prune test that asserts regexes/comments/directives survive a rewrite and that atomic replace can be interrupted/recovered.
- [ ] Add a benchmark harness with generated 10k/100k/1M-line corpora; record cold load, reload, prune, lookup hit/miss, and peak memory.
- [ ] Keep parser/cache tests on managed net471/net35; add a net6.0 **compile-only** target for the IL2CPP build. Do not try to run IL2CPP interop tests outside Unity.

**Exit criteria:** tests fail on the current `//` bug and prune data loss; a published baseline exists for every performance claim.

### Phase 1 — Format correctness

- [ ] Add `case '/': builder.Append('/'); break;` to `ReadTranslationLineAndDecode` (`TextHelper.cs`).
- [ ] Add a skipped-line/error counter and log a summary after load.
- [ ] Make prune preserve unrecognised lines/comments/directives and `r:`/`sr:` lines; only remove plain entries genuinely covered by a regex.
- [ ] Write prune output to a temp file and use `File.Replace`/atomic rename instead of `File.Move` + `FileMode.Create`.
- [ ] Do not prune automatically on every reload; gate it behind an explicit maintenance action or a `(length, mtime)` change check.
- [ ] Add tests for F-01..F-05 and a dry-run report of pruned lines.

**Exit criteria:** no data loss in prune tests; `//` round-trips; malformed lines are visible in the log.

### Phase 2 — Loader and large-file performance

- [ ] Replace `ReadToEnd().Split` in the core loaders with `ReadLine()` streaming.
- [ ] Pre-size `_translations`/`_reverseTranslations` on first load; keep capacity across reloads.
- [ ] Use one snapshot of `_translations` for the read-only derivation passes; remove redundant `ToList()` copies.
- [ ] Add a cheap whitespace check before constructing `UntranslatedText` in the variation pass.
- [ ] Add `if (input.IndexOf('<') < 0) return null;` to `RichTextParser.Parse`.
- [ ] Add a first-character/length prefilter or compiled-regex cap for large regex packs.
- [ ] Benchmark 100k and 1M lines with time, allocated bytes, and peak working set; record the baseline before/after.

**Exit criteria:** no `ReadToEnd` in the core loaders; measured improvement on the benchmark; no behavioural change in golden-file tests.

### Phase 2.5 — Incremental live reload (the main live-edit gap)

- [ ] Introduce `TranslationFileSnapshot` (path, size, mtime, hash, order, kind) and `TranslationFileSet` (plain entries, regexes, directives, scopes, raw lines).
- [ ] **L1:** skip unchanged files; parse only changed files; rebuild the merged index from per-file sets. Measure before adding delta logic.
- [ ] **L2:** add `SourceRef` provenance and apply a delta (remove old refs, add new refs, recompute winners) for plain, regex, scoped, token, partial, and variation entries.
- [ ] **L3:** parse on a worker, build the new index, and publish an immutable `TranslationSnapshot` with `Interlocked.Exchange`.
- [ ] Watcher: `IncludeSubdirectories`, `.txt`/`.zip` filter, `Renamed`/`Deleted`/`Error`, debounce/coalesce, bounded `WaitForFile`, own-write suppression, and plugin-cache routing.
- [ ] Do not call `PruneMainTranslationFile` on watcher-driven reloads.
- [ ] Add tests that edit one file and assert: only that file is parsed, no prune runs, lookups see the new value, deleted keys disappear, and the old snapshot remains valid on parse failure.

**Exit criteria:** a single-file edit is O(changed entries), not O(total corpus); no full `Clear()`/reparse; no reload loop from the plugin's own writes.

### Phase 2.6 — Memory budget and sidecar decision

- [ ] Measure resident and peak memory for 10k/100k/1M lines with variations/tokens/partials on and off.
- [ ] Set a memory budget (especially for 32-bit Mono) and log a warning when a pack exceeds it.
- [ ] Reduce resident cost: pre-size, avoid snapshots, lazy variation/token/partial generation, prune empty scoped collections, avoid duplicate reverse maps where membership is enough.
- [ ] Decide the sidecar only from measured cold-start numbers. If used: per-file, validated by size+mtime+hash, fallback to text, never authoritative.
- [ ] Do not use a sidecar to solve live-edit or resident-memory problems.

**Exit criteria:** a documented decision with measured numbers; no unmeasured “sidecar will fix it” work.

### Phase 3 — Lifecycle and leaks

- [ ] Dispose every `PluginTextCaches` value in `AutoTranslationPlugin.OnDestroy`; clear the dictionary.
- [ ] Add `MaintenanceHelper.RemoveMaintenanceFunction` (or return an `IDisposable` registration) and use it from `TextTranslationCache.Dispose`.
- [ ] Unsubscribe `TextTranslationFileChanged` in both `TextTranslationCache.Dispose` and `AutoTranslationPlugin.OnDestroy`; clear dictionaries/collections.
- [ ] Unsubscribe the scene-loaded delegate in `TranslationScopeHelper` (store the delegate and remove it on shutdown).
- [ ] Make `SafeFileWatcher` disposable/cancellable: bounded wait, `Error` handler, unsubscribe `DirectoryUpdated`, guard callbacks after `_disposed`, coalesce events.
- [ ] Destroy superseded `Texture2D`, `Sprite`, and material copies; remove the stray `Instantiate(ui)` clone in `TextTranslationInfo.ChangeFont`.
- [ ] Call `Resources.UnloadUnusedAssets()` after reload and after texture scans.
- [ ] Dispose `XUnityWebClient`/`XUnityWebResponse` on timeout/completion; remove `Thread.Abort` and use a cooperative stop.
- [ ] Reset `Settings.IsShutdown` in `RebootPlugin` and re-enable the plugin.
- [ ] Reset/clear per-scene static caches on scene load/shutdown.

**Exit criteria:** a start/reload/shutdown cycle leaves no watcher, maintenance action, scene callback, or generated Unity object behind.

### Phase 3.5 — Unified writer, prune, and compaction

- [ ] Extract `TranslationWriter` for append/rewrite/prune; use it from both `TextTranslationCache` and `SimpleTextTranslationCache`.
- [ ] Atomic rewrite: temp file + `File.Replace` (or equivalent) + backup; preserve BOM/newline style; validate before replacing.
- [ ] External-edit detection: compare size/mtime/hash before write; reload/merge or abort with a warning.
- [ ] Partial-append/crash recovery: count/report malformed lines; recover from `.bak`; never silently drop.
- [ ] Suppress/ignore the plugin's own watcher events; coalesce writes; no reload loop.
- [ ] Fix or remove the dead `SimpleTextTranslationCache._shouldOverrideEntireFile` path.
- [ ] Dispose zip entry streams.

**Exit criteria:** prune is crash-safe and lossless; an external edit cannot be silently clobbered; both caches use one writer.

### Phase 4 — Hot paths and lookup

- [ ] Replace the spam checker's per-frame `.Sum()` with a running total.
- [ ] Memoize the last `UntranslatedText` on `TextTranslationInfo`.
- [ ] Budget `KickoffTranslations` per frame.
- [ ] Hoist `WaitForSecondsRealtime` and cap stabilization coroutines.
- [ ] Replace the lookup ladder with an ordered candidate loop (after golden tests).
- [ ] Regex index/prefilter and a bounded negative cache instead of wholesale resets.
- [ ] Fix `_guiContentCheckFailed` assignment (should be `true`, not `false`).
- [ ] Add a duplicate guard in `ScheduleUnstartedJobs`; make iteration snapshot-based.
- [ ] Add `UntranslatedText` edge-case tests for templating + whitespace.

**Exit criteria:** no per-frame LINQ allocation in the steady state; regex worst case bounded; lookup behavior unchanged under golden files.

### Phase 5 — Optional sidecar / format evolution

Only if Phase 2/2.6 measurements still show unacceptable cold-start time:

- [ ] Define a per-file sidecar: header/version, source size, mtime, hash, entry count, length-prefixed UTF-8 key/value blob, offset index.
- [ ] Load only when it validates; otherwise fall back to the text parser and regenerate.
- [ ] Never make the sidecar authoritative; keep `.txt` editable.
- [ ] Add a command/tool to rebuild or delete the sidecar.

Do not replace `.txt` as the source of truth in this phase.

### Phase 6 — Provider throughput (separate track)

Not part of the core text phases, but required for “the app feels fast”:

- [ ] Per-endpoint concurrency limits and a per-frame start budget.
- [ ] Batching/rate-limit-aware scheduling and retry/backoff with jitter.
- [ ] Cooperative cancellation/shutdown; no `Thread.Abort`.
- [ ] HTTP client reuse and response disposal.
- [ ] Metrics: queue depth, in-flight, p50/p95 latency, error rate, rate-limit waits.

**Decision gate:** if provider latency dominates after Phase 2/2.5, move to this track before adding a sidecar.

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

### Live-edit / writer / concurrency tests

- Edit one file in a multi-file corpus; assert only that file is parsed (instrument the reader), no prune runs, and the new value is visible immediately.
- Delete/rename a file; assert its entries disappear and keys shadowed by other files fall back to the next source.
- Change a file while it is locked/partially written; assert bounded retry, no partial load, and the old snapshot remains valid.
- Two rapid edits coalesce into one reload; a watcher event caused by the plugin's own append does not trigger a reload loop.
- Change a file under `plugins/<name>/`; assert the plugin cache receives the event (currently missed because the watcher does not include subdirectories).
- Prune dry run and atomic rewrite: kill the process between temp write and replace in a harness; assert recovery from the original/backup.
- External edit then append: assert the writer detects the `(size, mtime, hash)` change and does not silently clobber.
- Concurrent lookup + reload: run lookups on one thread while publishing snapshots; assert no torn reads or `NullReferenceException`.
- Dispose while a write is queued and while a watcher callback is pending; assert no callback after disposal.

### Integration/benchmark tests

- 10k/100k/1M-line load: time, allocations, peak memory; publish a budget from the baseline before optimizing.
- 100k-line reload with `ReloadTranslationsOnFileChange` off; assert no prune unless requested.
- Same corpus with the watcher on and one changed file: assert incremental reload time scales with changed entries, not total entries.
- Lookup benchmark: hit/miss ratio, scoped/unscoped, token/no-token, 0/100/1000 regexes; report p50/p95.
- A reload while a lookup is in progress (thread test) to expose L-17.
- Start/stop/start cycle: assert no duplicate maintenance actions, watcher callbacks, or scene callbacks.
- Memory: resident set before/after load/reload, with variations/tokens/partials on and off; check 32-bit headroom.

### Test target

- Keep parser/cache unit tests on managed net471/net35 so they run without Unity.
- Add a net6.0 compile-only target for the IL2CPP core; do not run interop tests outside Unity.
- Benchmarks can run in a small console harness; `Stopwatch` and `GC.GetTotalMemory` are enough. No BenchmarkDotNet requirement on net35.

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
- Do not use SQLite/a database as the source of truth.
- Do not fold provider/network throughput into the core text phases; it is a separate track.

### Risks

- `ReadLine()` changes handling of `\r`-only files; add a fixture before swapping.
- Pre-sizing uses an estimate; benchmark to avoid over-allocating on small files.
- Prune preservation changes the meaning of “prune”; document the new behaviour.
- Unity asset destruction must not destroy objects still referenced by the game; only destroy objects created by XUAT.
- Thread-safety fixes can introduce main-thread stalls; prefer atomic reference swaps over coarse locks.
- Some findings are conditional (file watcher only when enabled; texture/font leaks only when those features are on). Validate the feature matrix before declaring a fix complete.
- Incremental reload is a behavior-sensitive change: last-wins plain keys, first-wins regexes, directives, and scopes must be preserved exactly. Golden tests are mandatory.
- Snapshot publishing can double peak memory during a full rebuild. L1/L2 should be benchmarked for peak working set, not only time.
- A whole-file sidecar is invalidated by every live edit. If live editing is the goal, per-file parsing plus delta is more valuable than a sidecar.
- External text editors and the plugin's append path can race. A per-file lock plus reload-before-write is the practical mitigation; multi-process writes are not fully solvable.
- FileSystemWatcher behavior differs on Windows, Linux, and macOS (rename/overflow semantics). Test each target; recreate the watcher on `Error`.
- Background parsing must not call Unity APIs. Use `Stopwatch` and pure managed data structures; publish only after validation.

---

## 9. Remaining bottlenecks and decision gates

### 9.1 Ranked bottlenecks after the plan

| Rank | Bottleneck | Current evidence | Fix phase | What to measure |
|---|---|---|---|---|
| 1 | Reload = prune + full reparse | `AutoTranslationPlugin.cs:3241-3247`; `TextTranslationCache.cs:187-239`; no incremental path | Phase 2.5 | One-file edit time, files parsed, entries rebuilt, peak memory |
| 2 | Load-time derivation/allocation | Variation `:276-318`; partial `:321-333`; token `:335-387`; `ReadToEnd` `:588`; `TextHelper` line decode | Phase 2 | Cold load, allocations, phase breakdown |
| 3 | Resident memory | Forward/reverse maps, variations, tokens, partials, scoped collections | Phase 2.6 | Peak working set, bytes/entry, 32-bit headroom |
| 4 | Lookup/regex worst case | `TryGetTranslation` `:1089-1622`; regex loop `:1519-1587`; negative-cache reset | Phase 4 | p50/p95 lookup, regex scans, miss rate |
| 5 | Storage/prune/external edits | Append `:814-843`; prune `:140-169, 500-571`; dead override `SimpleTextTranslationCache.cs:282-344` | Phase 1/3.5 | Prune time, data-loss tests, conflict tests |
| 6 | Provider throughput | `TranslationManager.cs:200-227`; per-request clients; no frame budget | Phase 6 | Queue depth, in-flight, p50/p95, error rate |
| 7 | Lifecycle leaks | `PluginTextCaches`, watcher, scene callback, Unity clones, `IsShutdown` | Phase 3 | Start/reload/stop object counts, logs |

### 9.2 Decision gates

1. **After Phase 2 benchmark:** if cold load is still above the chosen budget, evaluate the sidecar (Phase 5). If resident memory is above budget, work on compact/lazy data before the sidecar.
2. **After Phase 2.5 L1:** if changed-file parse plus full index rebuild is still above the live-edit budget, implement L2 delta. If watcher events are missing, fix the watcher before optimizing the parser.
3. **After Phase 4:** if regex scans dominate lookup time, add the regex index/negative-cache work. If plain dictionary probes dominate, reduce candidate permutations instead.
4. **If perceived latency is dominated by endpoint time:** stop optimizing the loader and move to Phase 6. A faster parser will not make a slow network endpoint faster.
5. **Never** change the `.txt` source format or make a sidecar authoritative to win a benchmark.

### 9.3 Suggested starting budgets (validate, do not promise)

These are starting points for the benchmark, not measured facts:

- 100k-line cold load: under ~1 s on a modern desktop; 1M lines under ~5 s.
- Live single-file edit on 100k lines: under ~200 ms; 1M lines under ~1 s, with no frame hitch.
- Resident set: keep 1M lines under a few hundred MB on 64-bit; treat 32-bit as a hard budget and warn/limit.
- Lookup p95: sub-millisecond without regexes; regex-heavy packs measured separately.
- No periodic allocation in the steady-state Update path.

If the measurements disagree, change the budgets, not the test.

---

## Appendix A — Evidence index

Line numbers are from the v2 tree. The 5.6.4 UI pass shifted `AutoTranslationPlugin.cs` by roughly 50–60 lines; prefer symbols when searching. `TextTranslationCache.cs` and `TextHelper.cs` were not changed by the UI pass.

### Format / large files

- `TextTranslationCache.cs:588` — `ReadToEnd().Split`
- `SimpleTextTranslationCache.cs:162` — same
- `TextHelper.cs:16-127` — line decode
- `TextHelper.cs:129-179` — encode/escape
- `TextTranslationCache.cs:276-318` — variation pass
- `TextTranslationCache.cs:321-387` — partial/token passes
- `TextTranslationCache.cs:500-571` — prune loads/rewrite
- `TextTranslationCache.cs:140-169` — backup/recreate
- `TextTranslationCache.cs:551-562` — regex/comment/directive drop
- `TextTranslationCache.cs:573-679` — line-based loader
- `TextTranslationCache.cs:1089-1622` — lookup ladder
- `TextTranslationCache.cs:1519-1587` — regex scan and negative cache
- `TextTranslationCache.cs:1548-1552, 1581-1585` — negative-cache reset
- `TextTranslationCache.cs:187-239` — clear/refill every load
- `AutoTranslationPlugin.cs:3241-3247` — always prune + reload
- `AutoTranslationPlugin.cs:580-635` — full `LoadTranslations`

### Reload / live edit

- `AutoTranslationPlugin.cs:257` — `TextTranslationFileChanged` subscription, never removed
- `AutoTranslationPlugin.cs:276-279` — watcher only sets `_translationReloadRequest`
- `AutoTranslationPlugin.cs:2873-2877` — reload request handling
- `TextTranslationCache.cs:57-81` — watcher only in the global cache constructor
- `TextTranslationCache.cs:88-104` — plugin caches have no watcher
- `SafeFileWatcher.cs:31` — no `IncludeSubdirectories`
- `SafeFileWatcher.cs:49-52` — event raised without a disposed guard
- `SafeFileWatcher.cs:99-107` — unbounded `WaitForFile`
- `SafeFileWatcher.cs:127-146` — dispose does not unsubscribe
- `MaintenanceHelper` — no unregister (see below)

### Hot paths

- `AutoTranslationPlugin.cs:2864` — `SpamChecker.Update` per frame
- `SpamChecker.cs:224` — `.Sum()` per frame
- `AutoTranslationPlugin.cs:686` — full-scene `FindObjectsOfType`
- `AutoTranslationPlugin.cs:3225-3238` — per-hook `UntranslatedText`/cache key
- `TranslationManager.cs:200-227` — full job drain
- `AutoTranslationPlugin.cs:2533, 2582` — wait-object allocation per retry
- `Parsing/RichTextParser.cs:47-56` — regex split/TagRegex
- `TextTranslationCache.cs:280, 338` — full `ToList()` snapshots
- `TextTranslationCache.cs:1015-1024` — per-hit `UntranslatedText` allocation

### Storage / writer

- `TextTranslationCache.cs:814-843` — append under watcher disable
- `TextTranslationCache.cs:140-169` — non-atomic prune replace
- `SimpleTextTranslationCache.cs:270-299` — queue/write path
- `SimpleTextTranslationCache.cs:309-361` — thread-pool writer
- `SimpleTextTranslationCache.cs:282-344` — dead override branch
- `TextTranslationCache.cs:757-770` — zip entry streams

### Lifecycle / leaks

- `AutoTranslationPlugin.cs:67` — `PluginTextCaches`
- `AutoTranslationPlugin.cs:3661-3701` — `OnDestroy`
- `TextTranslationCache.cs:80` — maintenance registration
- `XUnity.Common/Utilities/MaintenanceHelper.cs:24-75` — no unregister
- `TextTranslationCache.cs:1671-1683` — `Dispose` only watcher
- `Utilities/TranslationScopeHelper.cs:71-93` — scene callback
- `TextTranslationInfo.cs:153-158` — `Instantiate(ui)` clone
- `TextureTranslationInfo.cs:63-90` — texture creation
- `SafeFileWatcher.cs:99-107,127-146` — unbounded wait/dispose
- `Endpoints/Http/HttpEndpoint.cs:84` — per-translation client
- `Endpoints/ExtProtocol/ExtProtocolEndpoint.cs:385-390` — `Thread.Abort`
- `UI/TranslationAggregatorWindow.cs` (`GetTranslationView`) — **fixed in 5.6.4**; no index mismatch
- `Extensions/ComponentExtensions.cs:314-323` — failure flag
- `Configuration/Settings.cs:61` — `IsShutdown`
- `AutoTranslationPlugin.cs:2993-3002` — reboot
- `AutoTranslationPlugin.cs:600-623` — stale plugin caches
- `TranslationManager.cs:200-238` — list mutation/reentrancy
- `UntranslatedText.cs:230,268-282` — whitespace/templating warning
- `Configuration/Settings.cs:292` — `ReloadTranslationsOnFileChange`

### Verified checks

- `//` round-trip: compiled `TextHelper` + test program; `http://a//b` encoded to `http:\/\/a\/\/b` and decoded unchanged (bug confirmed).
- Append BOM: compiled .NET Framework test; `StreamWriter` on a non-empty `FileMode.Append` stream wrote no BOM (theory ruled out).
- `HookingHelper` changes from the previous task were compiled against stubs for both `IL2CPP` and managed configurations.
- L-09 was fixed by the 5.6.4 UI pass; it is no longer an open leak/crash item.
- No 100k/1M-line benchmark has been run yet; every performance number in this document is an estimate or a proposed budget.

---

## Appendix B — Suggested pull requests

Keep the first PR small and reviewable:

1. `TextHelper.cs`: add `case '/'` and a round-trip test.
2. `TextTranslationCache.cs`: preserve regexes/comments/directives during prune; write via temp file + atomic replace; add a skipped-line counter.
3. Tests: fixture-based round-trip, prune, and malformed-line tests.

Second PR (after the safety net):

1. `TranslationFileReader`: one `ReadLine()` parser shared by all readers.
2. Pre-size dictionaries and remove redundant snapshots.
3. Add the 10k/100k benchmark baseline.

Third PR (live editing):

1. `TranslationFileSnapshot`/`TranslationFileSet` and changed-file L1 reload.
2. Watcher subdirectory/filter/error/debounce fixes.
3. One-file-edit and concurrent-lookup tests.

Do not combine format fixes, incremental reload, and the sidecar in one PR. The first PR addresses data corruption without a format change or public API break.
