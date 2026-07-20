# Translating Keyji into a new language

🌐 **English** · [Русский](CONTRIBUTING-translations.ru.md)

Keyji is localized through flat `key → string` JSON dictionaries. One language per file;
the base languages (ru / en / ja) live in the exact format a contributor fills in — there is
no separate "internal" mechanism.

> **Important:** a language reaches Keyji **only through a rebuild** — either a reviewed PR to
> this repository, or fork-and-build-it-yourself. There is **no** loose file you drop next to
> `Keyji.exe`: the build is a single self-contained `.exe` with every language embedded inside
> the binary. This is a deliberate choice (curated quality), not a limitation.

## Where the translations live

```
src/Keyji/Localization/Strings/
├── ru.json   ← Russian
├── en.json   ← English (template and fallback)
└── ja.json   ← Japanese
```

## How to add a language

1. **Copy `en.json`** — it is the canonical template (the full key set). English is the
   fallback language: any key missing from your file is shown in English, so the UI never
   breaks on an incomplete translation.
2. **Name the file with a region** — BCP-47 with region: `pt-BR.json`, `zh-CN.json`,
   `de-DE.json`. (Keyji picks the language by the two-letter system-locale code, but we keep
   the file name fully qualified to avoid ambiguity.)
3. **Translate the values, never the keys.** The key (left of the `:`) is an identifier shared
   across all languages; only the string on the right gets translated.
4. **Keep the placeholders `{0}`, `{1}`.** Some strings are assembled with substitution (a
   language name, a status word, an OS error). Change the word order freely — just preserve the
   placeholders themselves. Example: `"dialog.toggleFailed": "Couldn't {0} {1}."` — `{0}` is the
   verb, `{1}` the name.
5. **Keep leading glyphs** where they carry meaning in the string (arrows, warning signs). The
   brand name "Keyji" is not translated.
6. **Register the file in the build.** `src/Keyji/Keyji.csproj` already has
   `<EmbeddedResource Include="Localization\Strings\*.json" />` — a new file in that folder is
   picked up automatically, no separate csproj edit needed.
7. **Add the language code to the allow-list.** In `src/Keyji/Localization/Loc.cs`, append the
   two-letter code (e.g. `"de"`) to the `Supported` array — otherwise Keyji stays in English on
   that locale.

## Quality bar

- Tone — friendly and calm, like the Russian/English originals (not bureaucratic).
- **Japanese** — a single polite register, **です／ます (敬体)**.
- Translations are checked against a native speaker before merging (the maintainer coordinates
  this). A machine-translation draft is acceptable as a starting point — a native speaker
  validates it.

## Checking your work

Build (`dotnet build`) and run Keyji. The language selector is in the panel settings
(gear → "Language"): the dropdown switches language **live**, no restart, which makes it easy
to check both the rendering and the English fallback for unfilled keys.

Note: this dropdown only lists the shipped languages (ru / en / ja) — **your new language is not
there yet** (the maintainer adds an entry when it's merged). To test your translation before
that, switch the **Windows UI language** to it — Keyji picks the language from the system locale
(step 7 above puts your code into that selection). A faster path: write the code directly into
Keyji's `settings.json` via the `"Language": "<code>"` field and launch. Either way, verify the
string rendering and the fallback.

## Language names in lists — not from here

The names of the languages themselves (日本語 / Русский / English and their localized variants in
the list and tooltips) come from Windows, not from these files — there is no need to translate
them. Only the interface strings (buttons, labels, hints, dialogs) are localized.
