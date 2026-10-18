# XUAT In-Game UI — Scaling and Visual Redesign Plan

Status: first implementation pass complete  
Scope: `src/XUnity.AutoTranslator.Plugin.Core/UI`, `AutoTranslationPlugin.OnGUI`, the IMGUI proxy in `src/UnityEngine`, and relevant settings  
Date: 2026-10-18  
Related: `docs/core-performance-and-lifecycle-plan.md`

---

## Implementation status (first pass)

Landed:

- `[UI] Scale=Auto` setting with numeric override (`0.8`–`2.5`), applied through `GUI.matrix` in `AutoTranslationPlugin.OnGUI`.
- Screen-to-GUI mouse conversion and window clamping for all three windows.
- Dark flat theme in `UI/UITheme.cs`: surfaces, 1 px borders, accent headers, hover/pressed/disabled button states, themed panels, tooltips, sliders, and close buttons.
- CJK-capable UI font selection in `FontHelper.GetUIFont` (game override font first, then common CJK/UI fonts).
- Window title/background consistency; ASCII `----` section dividers replaced with uppercase section headers; whole-window drag replaced by title-bar drag.
- Cascaded default window positions; aggregator/options windows clamped to the screen.
- `UI Scale` row in the aggregator options window (`-`, `+`, `Auto`).
- Fixed the dormant `posx` row-reset bug in the main window.
- Dropdown popup no longer closes while dragging its scrollbar; anchors remain in GUI coordinates.
- Main-window height is now content-driven (toggle/button/label counts) instead of a fixed 604 px.
- Aggregator panel heights are capped from the available screen height; each panel keeps its own scroll view, so the window no longer runs off the bottom of the screen.
- Window z-order: interacting with a window calls `GUI.BringWindowToFront`; `Esc` closes options first, then the aggregator, then the main window.
- Copy actions show a short success toast in the aggregator.
- Translation-view lookup is by translator reference instead of array index, removing the `IndexOutOfRangeException` path.
- Dropdown popups flip above the header when they would exceed the main-window content height.
- Click-through now consumes the IMGUI mouse event in addition to resetting legacy axes.

Still open:

- Generic new-Input-System action blocking. The current pass consumes IMGUI events and resets legacy axes; blocking arbitrary game actions requires game-specific action maps (or disabling devices, which can break IMGUI/menus), so it is intentionally not forced.
- Optional window-position persistence (size is already persisted).
- Optional uGUI/TMP overlay if the in-game UI becomes a primary product surface.

Validation note: the changed UI files compile under the net35/managed configuration via the Visual Studio Roslyn compiler against the repo's UnityEngine stubs and XUnity.Common. The IL2CPP proxy API surface was checked for `GUI.matrix`, the `GUI.Window` style overload, `GUIStyle` properties, and `GUILayout` style overloads. A full SDK/MSBuild build is not available in this environment (no .NET SDK installed).

---

## 0. Executive summary

The UI problems have two separate causes:

1. **No scaling model.** Everything is absolute IMGUI pixels, with no `GUI.matrix`, no DPI factor, no responsive layout, and fixed window sizes. A 320×596 window is 83% of a 720p screen height but only 28% of a 4K screen height, while the font stays ~12px. That is exactly the “huge on 720p, tiny on 4K” behavior.
2. **Default Unity skin + ad-hoc layout.** The UI copies `GUI.skin` styles, uses a flat 1×1 gray plate, has no UI font (so CJK text becomes boxes), uses ASCII `----` dividers, a 20×16 `X` close button, fixed row heights, no hover/disabled polish, and several hard-coded layout assumptions.

**Recommendation:** do an **IMGUI scale + theme + responsive-layout pass first**. It directly fixes the resolution problem with low regression risk. Do **not** start with a full uGUI/TMP rewrite: the core has no compile-time UGUI/TMP references, those types are reached only by runtime reflection, and they may be stripped in IL2CPP builds. A uGUI overlay is a product-level decision for later, not a UI bug fix.

---

## 1. Verified findings

### 1.1 Sizing and scaling

| Finding | Evidence |
|---|---|
| Main window is fixed **320×596**, starting at (20,20) | `UI/XuaWindow.cs:14-17` |
| Options window is fixed **320×400**, also at (20,20) | `UI/TranslationAggregatorOptionsWindow.cs:12-14` |
| Aggregator height is computed per frame and unclamped | `UI/TranslationAggregatorWindow.cs:41`, `:45-47` |
| The aggregator formula grows with enabled translators (`(enabled + 2) * Height + 30 + LabelHeight + spacing`) and can exceed the screen | `UI/TranslationAggregatorWindow.cs:41` |
| All three windows default to the same position, so they stack exactly on top of each other | `XuaWindow.cs:17`, `TranslationAggregatorOptionsWindow.cs:14`, `TranslationAggregatorWindow.cs:28` |
| No `Screen.width`/`Screen.height` layout use; `Screen.height` is only used to convert mouse Y | `XuaWindow.cs:43,53`, `TranslationAggregatorWindow.cs:51,61`, `Options:47,57` |
| No `GUI.matrix`, `Screen.dpi`, `GUIUtility.pixelsPerPoint`, or `ScaleAroundPivot` anywhere in Core | repo-wide grep |
| All metrics are absolute pixels | `UI/GUIUtil.cs:13-18` (`WindowTitleClearance=10`, `ComponentSpacing=10`, `LabelWidth=60`, `LabelHeight=21`, `RowHeight=21`) |
| No window position persistence; only aggregator width/height are saved | `Configuration/Settings.cs:345-346, 423-429` |

Net effect: **1 IMGUI unit = 1 device pixel**. The UI does not react to resolution, window size, DPI, or in-game render scale.

### 1.2 Layout fragility

- `XuaWindow`'s `posx` is initialised once (`XuaWindow.cs:66`) and incremented inside the command-button loop (`:124`), never reset per row. With more than 3 buttons, row 2 starts outside the 320px window. It is dormant only because exactly 3 buttons exist today.
- `XuaWindow` height 596 is hand-tuned to the current content. Adding a toggle or status label overflows with no scroll or auto-grow.
- Labels are single fixed-height rows with no word wrap (`XuaWindow.cs:154-155`); long keys collide with the right-aligned values (`col2x=80`, `col2=230`).
- The aggregator has no background plate, unlike the main and options windows (`TranslationAggregatorWindow.cs:43-47` has no `GUI.Box`; `XuaWindow.cs:37` and `Options:41` do).
- The dropdown anchor is captured once in the constructor and never recomputed (`XuaWindow.cs:159-163`); its popup is drawn inside the window function (`DropdownGUI.cs:59,77-81`) and is clipped by the window.
- The dropdown closes on any mouse event if the header was not the click target (`DropdownGUI.cs:62-65`), including dragging its own scrollbar.
- Tooltips are funneled into a fixed 105px label at the bottom (`XuaWindow.cs:165`), not shown near the cursor.
- The close button is 20×16 (`XuaWindow.cs:73`, `AGW:86`, `Options:70`), below a comfortable hit target.
- `GUI.DragWindow()` is called over the whole window (`XuaWindow.cs:167`), not just the title bar, so clicking/dragging controls can move the window.
- Z-order is `OnGUI` call order only (`AutoTranslationPlugin.cs:2899-2939`); `GUI.FocusWindow` changes IMGUI focus but does not bring a window to the front.
- No `Esc`/close shortcut and no keyboard focus management.

### 1.3 Styling

- Every custom style is a copy of the stock skin: `GUIUtil.cs:24-67` (`CopyStyle(GUI.skin.label/button/box, …)`). There is no custom `GUISkin`, no palette, no font, no iconography.
- The only art is a **1×1 flat gray texture** (`new Color(0.6f,0.6f,0.6f,1)`), `GUIUtil.cs:139-146`.
- `GUI.skin.font` is never set. The game text uses a runtime font via `FontCache.GetOrCreate(size)` (`Fonts/FontCache.cs:40-48`), but the UI does not. Japanese/Chinese/Russian in the aggregator therefore renders as blanks/boxes.
- Titles use ASCII decoration: `"---- XUnity.AutoTranslator UI ----"`, `"---- Command Panel ----"`, `"---- Translator Aggregator ----"`.
- No hover/press/disabled polish beyond skin defaults; “selected” rows reuse `onActive` for every state (`GUIUtil.cs:52-61`).
- The UI is English-only and hard-coded (`AutoTranslationPlugin.cs:347-399`).

### 1.4 Interaction and input

- Hotkeys use legacy `Input` and are wrapped in a one-shot `_inputSupported` try/catch (`AutoTranslationPlugin.cs:2743-2758`); if `UnityInput` throws once, every hotkey dies silently.
- Click-through blocking calls `UnityInput.Current.ResetInputAxes()` (`XuaWindow.cs:57`, `AGW:65`, `Options:61`), which is a no-op on the new Input System (`XUnity.Common/Utilities/UnityInput.cs:180`) and in the Null fallback.
- `_isMouseDownOnWindow` is refreshed only on mouse-button/wheel **down** frames (`XuaWindow.cs:41`), so it goes stale between events.
- Copy buttons have no feedback (`AGW:94-119`); a separate global auto-copy path exists (`AutoTranslationPlugin.cs:2837-2840`).

### 1.5 IMGUI/IL2CPP constraints

- Core references only `UnityEngine.IMGUIModule`; UGUI/TMP are runtime-reflection types (`XUnity.AutoTranslator.Plugin.Core.csproj:46-60`; `XUnity.Common/Constants/UnityTypes.cs:91-106`).
- The net35 target compiles against in-repo stubs under `src/UnityEngine`; there is no `UnityEngine.UI` at all.
- `GUI.matrix` in the net35 proxy is **get-only and throws** (`src/UnityEngine/GUI.cs:197`). Any scale fix that assigns `GUI.matrix` must add a setter to the proxy or use an alternative.
- IL2CPP can strip IMGUI; the code already degrades three GUILayout features (`AGW:231-239`, `Options:101-109`, `DropdownGUI:89-93`). If the target game strips IMGUI, the UI may not work at all — scaling is moot until a uGUI fallback exists.

---

## 2. Design goals

1. **Readable at any resolution:** 720p, 1080p, 1440p, 4K, ultrawide, Steam Deck, windowed/borderless.
2. **Predictable geometry:** no hard-coded 596px height; content auto-sizes or scrolls; windows clamp to the screen.
3. **One visual language:** dark utility panel, consistent spacing/typography/colors, no default Unity bevels, no ASCII dividers.
4. **CJK and mixed-script support:** use a real font, not Unity's default Arial.
5. **Low-risk implementation:** keep IMGUI for now; do not require UGUI/TMP or a new runtime dependency.
6. **Recoverable UI:** reset position/scale, remember settings, and degrade gracefully if IMGUI is partially stripped.

---

## 3. Recommended approach: IMGUI scale + theme + responsive layout

### 3.1 Phase UI-1 — global scale (the direct fix)

Add a UI scale setting:

```ini
[UI]
Scale=Auto        ; Auto | 0.75 | 1.0 | 1.25 | 1.5 | 2.0 | custom
```

Auto formula:

```text
scale = clamp(Screen.height / 1080, 0.80, 2.25)
```

Optionally use `Screen.dpi / 96` when it is in a sane range (some games report 0 or 96); otherwise fall back to the height formula. A user override is important for ultrawide, in-game render scale, and Steam Deck.

Implementation:

- Apply `GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1))` once in `AutoTranslationPlugin.OnGUI` (`AutoTranslationPlugin.cs:2890`), restore in `finally`.
- Add a setter to the net35 proxy at `src/UnityEngine/GUI.cs:197`; otherwise the shared code will not compile for `net35`.
- Adjust the manual mouse-to-GUI conversion in all three windows (`XuaWindow.cs:43,53`, `AGW:51,61`, `Options:47,57`) by the scale, or centralize it in `GUIUtil.ScreenToGuiPoint`.
- Scale the window clamp computations and any `Screen.height` math used for layout.
- Verify `GUI.Window` dragging and `GUI.FocusWindow` behavior under `GUI.matrix` in both Mono and IL2CPP.

Alternatively, wrap the whole OnGUI body in `GUIUtility.ScaleAroundPivot`, but check the proxy/stub support and mouse-coordinate behavior first. `GUI.matrix` is the simpler, more predictable option.

### 3.2 Phase UI-1 — typography and theme

Create a small `UITheme`/`UIMetrics` layer instead of copying the default skin:

**Palette (suggested):**

| Token | Value |
|---|---|
| `Background` | `#0E1116` |
| `Surface` | `#161B22` |
| `SurfaceRaised` | `#1F2630` |
| `Border` | `#2D3748` |
| `Text` | `#E6EDF3` |
| `MutedText` | `#8B98A5` |
| `Accent` | `#4CC2FF` |
| `AccentStrong` | `#2A9DF4` |
| `Success` | `#3FB950` |
| `Warning` | `#D29922` |
| `Danger` | `#F85149` |

**Metrics at 1×:**

| Token | Value |
|---|---|
| Base font | 13px |
| Section header | 12px, uppercase |
| Window title | 15px |
| Row height | 24px |
| Button height | 28px |
| Title bar height | 30px |
| Padding | 8px |
| Section gap | 12px |
| Label width | 90px |
| Border | 1px |

**Styling rules:**

- Flat surfaces with 1px borders; no default Unity bevels/gradients.
- Use generated 1×1 textures for backgrounds/borders; if rounded corners are wanted, generate a small 9-slice rounded texture at runtime or ship one PNG.
- Set `style.font = FontCache.GetOrCreate(fontSize)` and an explicit `style.fontSize`; this immediately fixes CJK boxes. Reuse the existing fallback/retry path.
- Buttons: 28–30px tall, 8px horizontal padding, clear hover/active/disabled states, accent focus ring.
- Section headers: real labels with an accent underline/left stripe, not `"---- Section ----"`.
- Status: label left, value right; use muted labels and brighter values; color only error/disabled states.
- Tooltips: a small styled box near the cursor, with wrapping and clamping to the screen.
- Window chrome: title bar with title + close; drag only the title bar (`GUI.DragWindow(new Rect(0, 0, width, titleHeight))`), not the whole window.
- Use a consistent background plate on all three windows (the aggregator currently lacks one).

### 3.3 Phase UI-2 — responsive layout

- Replace hard-coded window heights:
  - `XuaWindow`: compute preferred height from the toggle/button/label counts, or switch to `GUILayout.Window` if the proxy supports it. At minimum, `height = header + toggles + buttons + labels + tooltip + padding`, clamped to `Screen.height / scale - 40`.
  - `TranslationAggregatorWindow`: clamp `_viewModel.Height` and total window height to the screen; use a scrollable content area when the panels exceed it.
  - `TranslationAggregatorOptionsWindow`: content-driven or scrollable; 400px is arbitrary.
- Clamp `_windowRect` to `[0, Screen.width/scale - width]` / `[0, Screen.height/scale - height]` every frame.
- Cascade default positions: main (20,20), aggregator (360,20), options (20,640) or a diagonal offset; never stack all three.
- Persist window position and scale in settings; add “Reset UI” and “Reset Position” actions.
- Fix `posx` reset per row (`XuaWindow.cs:66,124`).
- Recompute dropdown anchors each frame; draw the dropdown popup outside the window clip or in a separate `GUI.Window`/overlay pass.
- Make the close button at least 28×24; add `Esc` to close the focused window.
- Add min/max sizes for the aggregator and validate loaded width/height (clamp invalid/negative values).
- Add a `.ui-scale` / `.ui-position` config section; document it in the README.

### 3.4 Phase UI-2/3 — interaction polish

- Replace the whole-window drag with a title-bar drag handle.
- Add hover/selected states for toggles and rows; use `GUI.color`/textures rather than the default skin's `onActive` for everything.
- Add a “Copied” toast or temporary button label after copy actions (`AGW:94-119`).
- Fix click-through:
  - Use the new Input System path when legacy input is disabled; `ResetInputAxes` is a no-op there (`UnityInput.cs:180`).
  - Refresh `_isMouseDownOnWindow` on all relevant mouse events, not only down frames.
  - Consider a transparent full-screen IMGUI blocker behind the windows instead of poking game input.
- Add keyboard focus/tab order if feasible; at minimum, `Esc` closes, and hotkeys are documented in the UI.
- Bring a clicked window to the front (manual ordering or a small window manager; `GUI.FocusWindow` alone is not enough).
- Fix the dropdown close condition (`DropdownGUI.cs:62-65`) so dragging its scrollbar does not dismiss it.
- Add a visible “IMGUI unavailable” message/fallback if the UI cannot initialize under a stripped IL2CPP build.

### 3.5 Wireframes

**Main window**

```text
┌──────────────────────────────────────────────┐
│ XUAT · Unity Auto Translator           —  ×  │
├──────────────────────────────────────────────┤
│ TRANSLATOR                                   │
│  Primary    [ DeepL Translate        ▾ ]     │
│  Fallback   [ None                   ▾ ]     │
│                                              │
│ BEHAVIOUR                                    │
│  [✓] Translate text      [✓] Aggregator      │
│  [ ] Silent mode         [ ] Copy to clip.   │
│                                              │
│ ACTIONS                                      │
│  [ Reload ]  [ Hook ]  [ Reboot ]            │
│                                              │
│ STATUS                                       │
│  Queued             3                        │
│  Translated         1,204                    │
│  Errors             0                        │
└──────────────────────────────────────────────┘
```

**Translation aggregator**

```text
┌──────────────────────────────────────────────┐
│ Translation Aggregator                 —  ×  │
├──────────────────────────────────────────────┤
│ ORIGINAL TEXT                      [ Copy ]  │
│ ┌──────────────────────────────────────────┐ │
│ │ ...                                      │ │
│ └──────────────────────────────────────────┘ │
│ DEEPL                              [ Copy ]  │
│ ┌──────────────────────────────────────────┐ │
│ │ ...                                      │ │
│ └──────────────────────────────────────────┘ │
│ [◀ Prev]  [Next ▶]          [Options]        │
└──────────────────────────────────────────────┘
```

The key visual change is not decoration: it is **cards, consistent spacing, a real font, a title bar, and a clear primary/secondary hierarchy**.

---

## 4. Options compared

| Option | Effort | Visual ceiling | Scaling quality | Risk |
|---|---|---|---|---|
| IMGUI scale + theme | Low | Medium | Good | Low |
| Responsive IMGUI layout | Medium | Medium | Good | Medium |
| uGUI/TMP overlay | High | High | Excellent | High |
| External HTML UI | Medium–high | High | Excellent | Orthogonal; not an in-game overlay |

### Why not uGUI first?

- No compile-time UGUI/TMP references; the core compiles against IMGUI-only stubs for net35.
- UGUI/TMP are reached by runtime reflection/Il2Cpp; Canvas, CanvasScaler, GraphicRaycaster, and Button are not in `UnityTypes`.
- IL2CPP stripping and TMP version skew are already a problem in this project.
- A uGUI rewrite needs an IMGUI fallback anyway, doubling the UI surface.

Use uGUI only if the in-game UI becomes a primary product surface and you are prepared to own a runtime UI framework. Use an external HTML UI for debug/aggregator workflows where a browser is acceptable.

---

## 5. Implementation plan

### Phase UI-0 — baseline

- [ ] Capture screenshots at 1280×720, 1920×1080, 2560×1440, 3840×2160, and 21:9.
- [ ] Record current window rects, font sizes, and any clipping/overlap.
- [ ] Decide the default scale policy and the config key name.

### Phase UI-1 — scale + theme

- [ ] Add `GUI.matrix` support to the net35 proxy (`src/UnityEngine/GUI.cs`).
- [ ] Add `UIScale` setting (`Auto`/numeric) and persistence.
- [ ] Apply the scale in `AutoTranslationPlugin.OnGUI` and adjust mouse coordinate conversions.
- [ ] Add `UIMetrics`/`UITheme` with the palette, font sizes, spacing, and style factories.
- [ ] Set the UI font from `FontCache.GetOrCreate`; verify CJK rendering.
- [ ] Replace the 1×1 gray plate with the themed surfaces/borders; unify all three windows.
- [ ] Replace ASCII section headers and the tiny close button; add hover/disabled states.

**Exit criteria:** UI is legible and proportionally correct at all target resolutions; CJK renders; no window extends beyond the screen at 100% scale.

### Phase UI-2 — responsive layout

- [ ] Auto-size or scroll the main window; remove the 596px constant.
- [ ] Clamp aggregator/options heights to the screen; add content scrolling.
- [ ] Clamp and persist window positions; cascade defaults; add Reset UI.
- [ ] Fix `posx` row reset and label clipping.
- [ ] Recompute dropdown anchors; prevent popup clipping.
- [ ] Add min/max sizes and invalid-value clamping.

**Exit criteria:** all windows stay on-screen at 720p/4K/ultrawide; content overflow is scrollable; no fixed-height overflow.

### Phase UI-3 — interaction polish

- [ ] Title-bar-only drag; `Esc` to close; window z-order.
- [ ] New Input System click-through blocker; stale mouse-state fix.
- [ ] Dropdown scrollbar/dismiss fix.
- [ ] Copy toast/feedback.
- [ ] Tooltip near cursor.
- [ ] “IMGUI unavailable” fallback message.

**Exit criteria:** no stuck input, no accidental window drags, dropdown remains usable, copy actions give feedback.

### Phase UI-4 — optional uGUI/TMP

Only after UI-1..3 are stable and if the UI is a product surface:

- [ ] Prototype Canvas/CanvasScaler/GraphicRaycaster via reflection for one window.
- [ ] Keep IMGUI as the fallback.
- [ ] Port the aggregator first (highest user value), then settings.
- [ ] Validate on a stripped IL2CPP build and a Mono build.

---

## 6. Acceptance criteria

- At 1280×720, 1920×1080, 2560×1440, 3840×2160, and 21:9:
  - the main window is readable and fully on-screen;
  - the aggregator does not exceed the screen;
  - text does not clip at the window edges;
  - the close button and row controls are comfortably clickable.
- UI scale persists across restarts; `Auto` matches the chosen formula.
- CJK and mixed-script text renders correctly in the UI.
- No default Unity bevel/gradient look; all three windows share the same theme.
- No hard-coded 596px/400px window heights.
- No `posx` row overflow, dropdown clipping, or whole-window drag.
- Click-through works on both legacy input and the new Input System.
- If IMGUI is unavailable, the user gets a clear message rather than a silent failure.

---

## 7. Risks and non-goals

### Risks

- `GUI.matrix` must be added to the net35 proxy, and manual screen-to-GUI mouse conversion must be updated consistently.
- Font creation may fail or be stripped on IL2CPP; reuse the existing `FontCache` fallback/retry path.
- `GUILayout.Window` may not be available in the proxy; keep a manual computed-height fallback.
- Scaling changes hit-testing, dropdown positions, and window drag behavior; test all three windows at each scale.
- A themed skin must not break the existing `GUIUtil` style consumers (labels, buttons, boxes, toggles).

### Non-goals for this pass

- No uGUI/TMP rewrite.
- No external HTML UI.
- No localization framework.
- No new runtime dependencies for net35.
- No major behavior changes to translation logic.

---

## Appendix — evidence index

### Size / scaling

- `UI/XuaWindow.cs:14-17` — 320×596 fixed
- `UI/TranslationAggregatorOptionsWindow.cs:12-14` — 320×400 fixed
- `UI/TranslationAggregatorWindow.cs:41,45-47` — computed unclamped height
- `UI/GUIUtil.cs:13-18` — absolute pixel metrics
- `AutoTranslationPlugin.cs:2890-2939` — no global scale transform
- `src/UnityEngine/GUI.cs:197` — `GUI.matrix` get-only/throws
- `Configuration/Settings.cs:345-346,423-429` — aggregator bounds persistence only

### Styling

- `UI/GUIUtil.cs:24-67` — every style copies the stock skin
- `UI/GUIUtil.cs:139-146` — 1×1 gray background
- `UI/XuaWindow.cs:37` — main window plate
- `UI/TranslationAggregatorWindow.cs:43-47` — no aggregator plate
- `Fonts/FontCache.cs:40-48` — runtime font available but unused by the UI
- `UI/XuaWindow.cs:73`, `TranslationAggregatorWindow.cs:86`, `Options:70` — 20×16 close button
- `UI/XuaWindow.cs:105,133,149` — ASCII section dividers
- `UI/XuaWindow.cs:165` — fixed 105px tooltip label

### Layout / interaction

- `UI/XuaWindow.cs:66,124` — `posx` not reset per row
- `UI/XuaWindow.cs:43,53`; `AGW:51,61`; `Options:47,57` — manual mouse conversion
- `UI/DropdownGUI.cs:62-65` — dropdown dismissed on any mouse event
- `UI/DropdownGUI.cs:59,77-81` — popup inside window clip
- `UI/XuaWindow.cs:167` — whole-window drag
- `AutoTranslationPlugin.cs:2743-2758` — one-shot input support
- `XUnity.Common/Utilities/UnityInput.cs:180` — `ResetInputAxes` no-op on new input
- `UI/TranslationAggregatorWindow.cs:113` — translation view index mismatch risk (also in the core leak plan)
