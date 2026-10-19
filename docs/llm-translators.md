# LLM Translators

The built-in LLM endpoints translate through chat-completion APIs:

| Endpoint | Service ID | API |
| --- | --- | --- |
| OpenAI | `OpenAiTranslate` | OpenAI or any OpenAI-compatible `/v1/chat/completions` server |
| Ollama | `OllamaTranslate` | Ollama `/api/chat` |
| LM Studio | `LmStudioTranslate` | LM Studio OpenAI-compatible `/v1/chat/completions` |
| Index-Translate | `IndexTranslate` | Index-Translate's OpenAI-compatible `/v1/chat/completions` |

All four endpoints read the same `LLMConfig.yaml` file. Everything for the LLM endpoints lives in that one file: URL, model, API key, prompts, glossary entries, batching limits, and model parameters. The endpoint chosen in `Config.ini` decides the request/response shape. The in-game **Advanced** window edits `LLMConfig.yaml` in place.

## Configuration

Set the active endpoint in the AutoTranslator configuration file:

```ini
[Service]
Endpoint=LmStudioTranslate
FallbackEndpoint=
```

`LLMConfig.yaml` is looked up in this order:

1. The AutoTranslator config folder:
   - BepInEx: `<Game>/BepInEx/config`
   - ReiPatcher: `<Game>/AutoTranslator`
2. The folder containing `XUnity.AutoTranslator.LlmTranslators.dll` (normally `Translators`).

A copy of the sample is shipped next to the translator DLL. Copy it to the config folder when you want one shared configuration for multiple games.

```yaml
apiKey: "None"
apiKeyRequired: false
url: "http://localhost:1234/v1/chat/completions"
model: "model-identifier"

# Batching: short lines are grouped to amortize the system prompt.
# Long texts are sent on their own and split into smaller blocks when needed.
# Set a character limit to 0 or lower to disable that limit.
maxTranslationsPerRequest: 12
maxCharactersPerRequest: 4000
maxCharactersPerTranslation: 2000

# max_tokens is sent to OpenAI/LM Studio and mapped to Ollama's num_predict.
modelParams:
  temperature: 0.2
  max_tokens: 4096
  top_p: 0.9
  top_k: 40
  frequency_penalty: 0
  presence_penalty: 0

systemPrompt: |
  Translate the source text into fluent English while preserving the original tone and meaning.
  Do not add explanations, notes, markdown, or quotation marks.
  Preserve character names, placeholders, variables, rich-text tags, escaped characters, and line breaks.
  Use the glossary translation exactly when a matching source term appears.

glossaryPrompt: |
  # Glossary for Consistent Translations
  Use the translation for exact matches.
  ## Terms
```

### Endpoint URLs

| Endpoint | `url` | `apiKeyRequired` |
| --- | --- | --- |
| OpenAI | `https://api.openai.com/v1/chat/completions` | `true` |
| Ollama | `http://localhost:11434/api/chat` | `false` |
| LM Studio | `http://localhost:1234/v1/chat/completions` | `false` |
| Index-Translate | `indexTranslateUrl` (default: `https://index-translate.bilibili.com/v1/chat/completions`) | `false` |

`OpenAiTranslate` also works with keyless local OpenAI-compatible servers (vLLM, llama.cpp server, LocalAI, and similar). Keep `apiKeyRequired: false` in that case; no `Authorization` header is sent. `IndexTranslate` does not use the top-level `url`/`model`; it defaults to the free public API and reads `indexTranslateUrl`/`indexTranslateModel` when set. Leave the top-level values in place for the other endpoints.

### IndexTranslate mode

`IndexTranslate` selects Index-Translate's native `translate.py` translation mode instead of the generic system prompt. It defaults to the free public 35B API, so no URL or model edit is required:

```yaml
apiKeyRequired: false
modelParams:
  temperature: 0
  max_tokens: 4096

# Optional self-hosted overrides:
# indexTranslateUrl: "http://127.0.0.1:8000/v1/chat/completions"
# indexTranslateModel: "IndexTeam/Index-Translate-9B"
```

For a local server, set `indexTranslateUrl` to its OpenAI-compatible URL (for example `http://127.0.0.1:8000/v1/chat/completions`) and `indexTranslateModel` to its model name (for example `IndexTeam/Index-Translate-9B`). The free public API serves the 35B preview model; use a self-hosted 2B or 9B model for other sizes or offline play. If the game cannot reach the public API directly, run the upstream `call_api.py --serve` bridge and use `http://127.0.0.1:8080/v1/chat/completions` as `indexTranslateUrl`.

The endpoint builds the canonical Chinese instruction prompt, maps AutoTranslator's destination language to the model's language name, turns matching `glossary` entries into `【硬性要求】专名/术语对照` constraints, disables thinking with `chat_template_kwargs.enable_thinking: false`, and sends `User-Agent: Index-Translate-Client/1.0`. `systemPrompt` and `glossaryPrompt` are not used in this mode. When no `modelParams` are configured, it sends `temperature: 0` and `max_tokens: 4096`.

See the [Index-Translate repository](https://github.com/bilibili/Index-Translate) for model weights, serving presets, the API client, and the local bridge proxy.

### Fields

| Field | Description |
| --- | --- |
| `apiKey` | Bearer token used when `apiKeyRequired` is `true`. |
| `apiKeyRequired` | Sends `Authorization: Bearer <apiKey>` and requires a non-empty key. |
| `url` | Chat-completion endpoint URL for OpenAI, Ollama, and LM Studio. |
| `model` | Model name for OpenAI, Ollama, and LM Studio. |
| `indexTranslateUrl` | Optional IndexTranslate URL override. Defaults to `https://index-translate.bilibili.com/v1/chat/completions`. |
| `indexTranslateModel` | Optional IndexTranslate model override. Defaults to `Index-Translate-35B-A3B`. |
| `maxTranslationsPerRequest` | Maximum number of queued strings sent in one request. Defaults to `12`. |
| `maxCharactersPerRequest` | Character budget for one batch. A text that would exceed it waits for the next batch. Defaults to `4000`; `0` disables the limit. |
| `maxCharactersPerTranslation` | Texts longer than this are sent alone and split into smaller blocks. Defaults to `2000`; `0` disables the limit. |
| `modelParams` | Extra model parameters such as `temperature`, `top_p`, and `max_tokens`. For Ollama these are nested under `options`, and `max_tokens` is mapped to `num_predict`. `IndexTranslate` defaults a missing temperature to `0` and a missing `max_tokens` to `4096`. |
| `systemPrompt` | Main translation instruction. Not used by `IndexTranslate`. |
| `glossaryPrompt` | Header used before matching glossary terms. Not used by `IndexTranslate`. |

## Batching and Long Texts

Short strings are grouped into one request so the system prompt and glossary are paid for once per batch instead of once per string. The batch stops at `maxTranslationsPerRequest`, at `maxCharactersPerRequest`, or when the next string is longer than `maxCharactersPerTranslation`.

Long strings (for example character or city dossiers with several paragraphs) are deliberately sent on their own. This prevents one long multi-paragraph response from breaking the marker count for a batch of short lines and exhausting their retry budget. The endpoint then splits the long text at paragraph, line, sentence, or word boundaries, sends the pieces as separate blocks, and reassembles the translated blocks using the original separators. If the model ignores the block markers, the response is accepted as one translation for a single long input or as one block per original input.

LLM endpoints are exempt from the core `[Behaviour] MaxCharactersPerTranslation` drop limit (200 by default), so a long dossier is queued and split instead of being discarded before it reaches the endpoint. The per-endpoint `LLMConfig.yaml` `maxCharactersPerTranslation` (default 2000) controls the split size.

`IndexTranslate` uses the same request plan. Its canonical prompt wraps the marked blocks in the model's `【源文】` section and adds a hard constraint requiring the model to keep every marker, block count, order, and line breaks.

`max_tokens` (or `num_predict` for Ollama) should be large enough for the longest batch you expect. The sample uses `4096`.

## Reloading and UI

The main plugin window shows a `Temperature` row for the selected endpoint. Its status section also shows `Processing` (active translation jobs) and `Queued` (unstarted jobs). If a scene change invalidates a GUI window, the window is recreated with its previous visibility. LLM endpoints display the configured `temperature`, `0.2` (`0` for `IndexTranslate`) when no `modelParams` are set, or `default` when other model parameters are set without a temperature. `IndexTranslate` reports its effective defaults (`0` and `4096`) even when only other parameters are set. Non-LLM endpoints show `n/a`.

`LLMConfig.yaml` is checked for changes at most twice per second. Edits are applied to subsequent requests without restarting the game, including the model, prompt, API key, glossary, and batch limits.

`Reload` / `Alt+R` refreshes active registered text only, so large scenes reload quickly. Use `Hook` / `Alt+U` when you need a full object-tree scan. Scene loads run an active-object scan after one second.

## Glossary

Glossary entries live in `LLMConfig.yaml` under `glossary`:

```yaml
glossary:
  - raw: 舅舅
    result: Uncle
    transliteration: Jiu Jiu
    context: Endearing way to address an uncle
```

Only `raw` and `result` are used to build the prompt. `transliteration` and `context` are documentation fields for translators. `IndexTranslate` sends matching entries as hard `【硬性要求】` terminology constraints instead of using the `glossaryPrompt` header.

## Batching

Batching is controlled by `EnableBatching` under `[Behaviour]` in `Config.ini` (default `True`). The endpoint groups short strings into one request, separated by `<<<1>>>`-style markers, and applies the two character limits described above. If the model returns the wrong number of translations, AutoTranslator temporarily disables batching and retries the affected lines individually.

## Advanced UI

The main window's **Advanced** button opens an editor for the selected LLM endpoint. It exposes temperature, max tokens, top P, max translations per request, the two character limits, model, and URL. **Set** validates the value and writes it into `LLMConfig.yaml`; the change is reloaded immediately. Use `default` (or an empty model-parameter field) to remove that model parameter.

## Concurrency Defaults

| Endpoint | `MaxConcurrency` |
| --- | --- |
| OpenAI | 15 |
| Ollama | 5 |
| LM Studio | 5 |
| Index-Translate | 5 |

Local servers are kept lower by default so a single machine is not saturated. `IndexTranslate` also keeps concurrency modest because the free public API is shared.

## Build and Test

The translator project is part of the main solution:

```powershell
msbuild XUnity.AutoTranslator.sln /m /p:Configuration=Release
```

The non-live tests run on `net8.0`:

```powershell
dotnet test test/XUnity.AutoTranslator.LlmTranslators.Tests/XUnity.AutoTranslator.LlmTranslators.Tests.csproj -c Release
```
