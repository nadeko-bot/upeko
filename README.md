# Upeko - NadekoBot Installer

Upeko is a desktop app that installs, updates and runs [NadekoBot](https://nadeko.bot) for you.
It works on Windows, macOS and Linux and is built with .NET 8 and Avalonia UI.

## Download

Get the latest build for your system from the [releases page](https://github.com/nadeko-bot/upeko/releases/latest).

| System | File |
| --- | --- |
| Windows | `upeko-win-x64.exe` (or `upeko-win-arm64.exe`) |
| macOS | `upeko-osx-arm64.app.zip` for Apple Silicon, `upeko-osx-x64.app.zip` for Intel |
| Linux | `upeko-linux-x64` (or `upeko-linux-arm64`); run `chmod +x` on it first |

## Features

- **Several bots**: install and manage any number of NadekoBot instances from one window.
- **Bot token**: paste the token on the bot page. Upeko does not start a bot that has no token.
- **Updates**: Upeko checks for a new NadekoBot version at startup and every 6 hours, and sends a notification. Update with one click, also while the bot runs: the bot stays online during the download and starts again on the new version. Your `data` folder (creds, database, configs) is kept.
- **What's new**: a strip above the bot list shows the first changes of a new NadekoBot version. Its **What's new** button opens the full changelog since your version.
- **Log**: the bot page shows the last 500 lines of the bot's output, with a Copy button. When a bot stops by itself, Upeko shows its last error.
- **Music tools**: install ffmpeg, yt-dlp and a JavaScript runtime (deno) from the top bar.
- **Runs in the background**: closing the window keeps Upeko in the system tray, and bots that were running start again the next time Upeko opens. If you start Upeko again, the window of the running Upeko opens.
- **Light and dark theme**, and 7 languages: English, Français, Deutsch, Español, Polski, Русский, 中文.

## Don't want to host it yourself?

[NadeKloud](https://cloud.nadeko.bot/?ref=upeko&utm_source=upeko&utm_medium=readme) is managed NadekoBot hosting from Nadeko's developer: your own bot online 24/7, with updates and nightly backups handled for you. It has a free trial, and you can import the `data` folder of a bot you already run.

## First launch

1. Start Upeko.
2. Click **Add Instance**. Upeko creates a bot in `Documents/upeko/<bot name>`. Open the bot and click **Browse** to pick another folder.
3. Click **Download & Install**.
4. Paste your bot's token in **Bot token** and click **Save**. The [creds guide](https://docs.nadeko.bot/creds-guide) tells how to get a token.
5. Click **Start**.
6. Click **creds.yml** under *Shortcuts* to set the other fields, for example your owner ID.

### Music

The dots at the top right show whether **ffmpeg**, **yt-dlp** and a **js** runtime (deno, bun or node) are installed: green means installed, orange means missing. Click a missing one to install it.

- On Windows they go to `C:\ffmpeg`, `C:\yt-dlp` and `C:\deno`, and Upeko adds them to your user `PATH`.
- On Linux they go to `~/.local/bin`.
- On macOS, Upeko installs yt-dlp and deno. Install ffmpeg yourself with `brew install ffmpeg`.

## Where Upeko keeps its files

| What | Where |
| --- | --- |
| Settings and bot list | `Documents/upeko/upeko.json`, with backups `upeko.json.bak.1` to `.bak.5` |
| Logs | `Documents/upeko/logs/` |
| Changelog of the latest NadekoBot release | `Documents/upeko/cache/` |
| Lock file that stops a second Upeko | `Documents/upeko/upeko.lock` |
| Previous version of a bot after an update | `.old-<bot name>` next to the bot's folder |

Removing an instance in Upeko does not delete the bot's folder.

## Development

You need the [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (a newer SDK works too).

```bash
git clone https://github.com/nadeko-bot/upeko.git
cd upeko
dotnet run
```

Release builds are trimmed single-file executables. Before a release, check that a trimmed build still works:

```bash
dotnet publish -c Release -r linux-x64 --self-contained -o publish
```

Pushing a tag (for example `1.2.0`) makes the GitHub workflow build all platforms and publish a release.

### Project structure

- `Views/`: Avalonia views (`.axaml`)
- `ViewModels/`: view models; most of the app's logic lives here
- `ViewModels/MusicPrereqs/`: ffmpeg, yt-dlp and deno checks and installers
- `Services/`: NadekoBot downloads and updates (`UpdateChecker`), settings (`JsonBotRepository`), translations
- `Assets/Lang/`: UI strings, one file per language. Add a new string to every file.
- `packaging/`: macOS app bundle and Linux `.desktop` files

## Help

Ask in the `#help` channel of the [NadekoBot Discord](https://discord.nadeko.bot).

Nadeko is free and open source. If you'd like to help, you can [support it on Patreon](https://patreon.com/join/nadekobot).

## License

GPLv3. See [LICENSE.md](LICENSE.md).
