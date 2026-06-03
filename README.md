# MeiBrowser
Browse any hoyo games files &amp; download them at any version without having the game.

## [💻 Download latest build](https://nightly.link/Escartem/MeiBrowser/workflows/build/master/MeiBrowser.zip)

> **Fork by aeolira** — Added multi-select checkboxes, Motrix/aria2 push, cache cleanup, crash handling, and more.

## ✨ New Features (this fork)

- **Multi-select checkboxes** — Select multiple files/folders at once via checkboxes in the file tree, with Select All / Deselect All buttons and parent-child auto-propagation
- **Push to Motrix** — Send selected files directly to a local Motrix instance via aria2 JSON-RPC (port 16800), or generate an aria2-compatible input file for manual import
- **Clear Cache** — One-click cleanup of leftover `.aria2_temp` directories from interrupted downloads across Desktop, Documents, and Downloads
- **Aria2 integration** — Optional aria2c-based parallel chunked downloader with progress tracking and automatic fallback to HttpClient
- **Crash resilience** — Global exception handler with crash log (`meibrowser_crash.log`) and user-friendly error dialogs instead of silent crashes
- **HTTP timeouts** — All API requests have 15-second timeouts to prevent hanging on unreachable endpoints
- **Stale cache cleanup on startup** — Automatically removes leftover `.aria2_temp` directories on each launch

# Showcase

## Browse sophon files

![img](https://bin.escartem.moe/2025/11/17/6pIqZRCeEH.png)

## Browse scattered files
Scattered files being the old method before sophon, allowing access to early versions of the games

![img](https://bin.escartem.moe/2025/11/17/wipbd2J6aY.png)

## Access full game zip

![img](https://bin.escartem.moe/2025/11/17/g2hlhXdU3k.png)

## Access update zip

![img](https://bin.escartem.moe/2025/11/17/G7ledeWSax.png)

## Use custom sophon url
For when you want to use unreleased beta games

![img](https://bin.escartem.moe/2025/11/17/vAcOtNKI4U.png)

## See new/changed files between versions
It'll only display files that have changed in the update, so you don't need to download everything

![img](https://bin.escartem.moe/2025/12/01/ewPi5QU0ct.png)

## Use custom builds from closed betas (getBuildWithStokenLogin)

![img](https://bin.escartem.moe/2026/01/04/QFvZs7Egqh.png)

---

## Feel free to contribute, or star if you like this project :3
