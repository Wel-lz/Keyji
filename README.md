<div align="center">

<img src="img/keyji-logo.gif" alt="Keyji" width="200" />

**Learning a language shouldn't clutter your keyboard.**
Keyji keeps a rarely-needed layout one click away — and out of your way the rest of the time.

<a href="#"><img src="https://img.shields.io/badge/platform-Windows-0078D6?style=flat&logo=windows&logoColor=white" alt="Windows"></a>
<a href="https://github.com/Wel-lz/Keyji/stargazers"><img src="https://img.shields.io/github/stars/Wel-lz/Keyji?style=flat&color=yellow" alt="Stars"></a>
<a href="LICENSE"><img src="https://img.shields.io/github/license/Wel-lz/Keyji?style=flat&color=brightgreen" alt="License: MIT"></a>

🚧 **Status: v0.1 in development** — not released yet.

<img src="https://flagcdn.com/20x15/gb.png" alt="" width="20"> **English** · [<img src="https://flagcdn.com/20x15/ru.png" alt="" width="20"> Русский](README_RU.md)

</div>

---

## The problem

Windows gives you two bad options for a keyboard layout you only need *occasionally* — a learner's Japanese, an emigrant's Cyrillic, an Arabic script you touch once a week:

- **Keep it installed** — and it sits forever in your `Win+Space` cycle, so you tab past it dozens of times a day to reach the two layouts you actually use.
- **Remove it** — and every time you *do* need it you dig back into Settings → Language → Add, wait for the pack, and remove it again after.

Neither fits a layout you need rarely but genuinely. That's the gap Keyji fills.

## How it works

Keyji lives in the system tray. A click or a right-click toggle **turns a rare layout on or off** — added when you need it, gone when you don't, never cluttering your everyday cycle.

- **One click from the tray** — enable or disable a layout instantly. No Settings, no cycling past it.
- **Tray status at a glance** — the icon shows what's currently active.
- **Friendly onboarding** — pick your languages on first run from a curated preset list or the full Windows catalog. Built for non-technical users.
- **Native and instant** — talks to Windows language profiles directly (WinAPI / TSF), no `powershell.exe` spawned per action. Toggles in well under 100 ms.
- **One file, nothing to install** — download one self-contained `.exe`, double-click, done. No installer, no runtime to fetch.

## Who it's for

- **Language learners** who need a script for study but not for daily typing.
- **Multilingual users** who switch scripts occasionally, not constantly.
- **Anyone** tired of a rarely-used layout crowding their `Win+Space` cycle.

## What Keyji is *not*

It's **not** a replacement for `Win+Space`. Native switching cycles between your active layouts; Keyji decides *which layouts are in that cycle at all* — bringing a rare one in on demand and taking it back out. Different job.

## Screenshot

> 🖼️ *Placeholder — tray menu & onboarding GIF land here at v0.1.* A branded look-and-feel preview already exists as a design test.

## How it compares

| | Keyji | `Win+Space` | Windows Settings | PowerToys |
|---|:---:|:---:|:---:|:---:|
| Toggle a rare layout in/out on demand | ✅ | ❌ | 🐢 manual | ❌ |
| One-click toggle | ✅ | ✅ | ❌ | — |
| Keeps rare layouts out of the daily cycle | ✅ | ❌ | ❌ | ❌ |
| Nothing to install (single `.exe`) | ✅ | — | — | ❌ heavy |
| Purpose-built, no bloat | ✅ | — | — | ❌ |

## Status & roadmap

Keyji is built in **C# / .NET (WPF)**.

- **v0.1 (in progress)** — generalized toggle, onboarding window, tray toggles (click / right-click), autostart, native read/write, single `.exe`.
- **After MVP** — global hotkey, installer + `winget`, auto-update, growing preset catalog, per-app rules *on request*.
- **Never** — cross-platform, cloud sync, replacing `Win+Space`, turning into a settings behemoth.

## License

[MIT](LICENSE) — minimal friction for users and contributors.