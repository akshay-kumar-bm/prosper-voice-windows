# Prosper for PowerToys Command Palette

[Prosper Voice for Windows](../README.md) as a **PowerToys Command Palette
extension**: dictation into any app, spoken edits of selected text, and an Assistant (OpenAI, with optional MCP tools)
that you start from Command Palette, its Dock, or the same global shortcuts as before.

Command Palette (Win+Alt+Space) is PowerToys' launcher. Extensions are separate MSIX-packaged apps that Command
Palette starts as out-of-process COM servers and talks to over WinRT. This one keeps Prosper's own engine (speech to
text, cleanup, rewrite, Assistant, MCP, encrypted history, the recording pill) and replaces its tray icon, settings
window and history window with Command Palette commands and pages.

## What's in the palette

| Command / place | What it does |
|---|---|
| **Prosper: Dictate** | Records; run it again (or click ✓ on the pill) to finish. The text is cleaned up and pasted where your cursor was. With text selected, what you say is an instruction for it ("make this formal") and the result replaces the selection. |
| **Prosper: Assistant** | Speak a request (an email, a reply, an answer…). It sees your selection, the app's name, *About you*, and your screen if you add it. The answer is typed into the focused field, or shown in a card with Insert / Copy. |
| **Ask Prosper** | Type a request instead. *Ask* shows the answer in the palette (Copy / Insert into ⟨app⟩ / follow up); *Write into ⟨app⟩* types it where your cursor was. |
| **Ask Prosper: …** (fallback) | Anything you type in Command Palette's search box can be sent to the Assistant. |
| **Prosper: Chat or transcribe audio** | A typed chat. Give an audio file path (≤ 25 MB) to get its transcript or ask about it. |
| **Prosper history** | Every conversation with a preview pane (what you said, the answer, each MCP tool call). Open one to read it all and follow up. Copy / Insert / Delete from the context menu (Ctrl+K). |
| **Prosper settings** | OpenAI key (DPAPI-encrypted), endpoint (OpenAI or Azure/compatible), models, *About you*, cleanup, history, the bar, global shortcuts, MCP server (sign in or token, always-allowed tools). |
| **Dock band** | Dictate · Assistant · Add screen · Cancel, with a live timer while recording. Pin it from Command Palette's Dock settings. |
| **Context menu** on Dictate / Assistant | Finish · Cancel · Add screen · Settings. |

**Shortcuts.** Ctrl+Alt+Space (dictate), Ctrl+Alt+A (Assistant) and Ctrl+Alt+S (add screen) still work: the extension
registers them itself while Command Palette is running (turn this off in settings). You can also give any Prosper
command its own hotkey or alias in Command Palette's settings → Extensions → Prosper.

**The pill.** While you talk, Prosper's frosted pill appears at the bottom of the screen with ✕ · timer · live
waveform · ✓ (and *Add screen* for the Assistant), then shows what it's doing ("Transcribing", "Thinking with your
screen") and confirms ("Typed · copied"). At rest it is hidden unless you turn on *Keep the Prosper bar on screen*.

## Install on your laptop

Requirements: Windows 10 2004+ / Windows 11, [PowerToys](https://aka.ms/installpowertoys) with Command Palette
turned on, and an OpenAI API key.

### From a release (no Visual Studio)

1. From the repository's latest [Release](../../releases/latest), download **`ProsperPalette-x64.zip`**
   (`ProsperPalette-ARM64.zip` for ARM laptops) and unzip it: it holds the package, its certificate and the install
   script. Between releases, the same files are in the `ProsperPalette-x64` / `ProsperPalette-ARM64` artifact of any
   run of the *Prosper Command Palette extension* workflow under **Actions**.
2. Run, in that folder:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Install-ProsperPalette.ps1
   ```
   Each build is signed with its own self-signed certificate. The script adds that certificate to the machine's
   *Trusted People* store (one UAC prompt: this is the only step that needs administrator rights), then installs the
   extension for your account, replacing an older version.
3. Open Command Palette, type **Reload** → *Reload Command Palette extensions*, then **Prosper settings**: paste your
   OpenAI key, add a line *About you*, **Save**, and **Test connection**.
4. If Windows blocks the microphone: *Settings → Privacy & security → Microphone →* allow desktop apps.

Company laptops that block self-signed packages need IT to sign the MSIX with the company certificate (change
`Publisher` in `ProsperPalette/Package.appxmanifest` to match it) or to allow it.

Uninstall: `Get-AppxPackage ProsperPalette | Remove-AppxPackage` (history and settings stay in
`%APPDATA%\ProsperVoice`; delete that folder too to remove everything).

### From source (Visual Studio)

Open `ProsperPalette.sln` in Visual Studio 2022 (17.14+) with the *.NET desktop development* and *Windows application
development* workloads, pick **x64** (or ARM64), then **Build → Deploy Solution**, and *Reload* in Command Palette.
F5 with the *ProsperPalette (Package)* profile attaches the debugger.

Command line on Windows (what CI runs):

```powershell
msbuild ProsperPalette\ProsperPalette.csproj /restore /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 `
  /p:SelfContained=true /p:GenerateAppxPackageOnBuild=true /p:AppxBundle=Never /p:UapAppxPackageBuildMode=SideloadOnly
```

## Releasing

Push a version tag (`git tag v1.2 && git push origin v1.2`). `release.yml` builds `ProsperVoice.exe` and
`cmdpal.yml` builds the extension; both attach their files to that tag's release (package version 1.2.0.0, so it
installs as an update).

## How it's built

```
ProsperPalette/
  Program.cs, ProsperExtension.cs   COM server + IExtension (CLSID 138DF00F-…, same as Package.appxmanifest)
  ProsperCommandsProvider.cs        top-level commands, fallback, Dock band, live recording state
  Palette/                          Command Palette pages and commands (history, ask, chat, settings form)
  Engine/                           Prosper's engine: a copy of ../src ported to .NET 10 with minimal changes
    Engine.cs                       was App.cs: the STA UI thread, focus return, global shortcuts
    Chat.cs                         typed requests and follow-ups (was in the history window)
    Ai.cs Audio.cs Mcp.cs Desktop.cs History.cs Bubble.cs Style.cs Logo.cs   as upstream
    Core.cs                         as upstream, with JSON on System.Text.Json instead of JavaScriptSerializer
tests/EngineTests                   platform-neutral checks of the port (runs anywhere: dotnet run --project tests/EngineTests)
install/Install-ProsperPalette.ps1  trusts the build's certificate and installs the MSIX
```

Things that differ from running Prosper on its own, and why:

- **Threads.** Command Palette calls the extension on MTA COM threads; Prosper's WinForms pill, answer card, Allow
  dialog and the clipboard need an STA thread with a message loop. `Engine` owns one and every palette call is queued
  onto it.
- **Finding your app.** When a command runs from the palette or the Dock, Command Palette is in front. The engine
  remembers the last foreground window that isn't Command Palette (an out-of-context `EVENT_SYSTEM_FOREGROUND`
  WinEvent: no hook DLL, no keystrokes read) and returns to it before reading the selection or pasting.
- **Packaging.** .NET 10, MSIX, self-contained. Trimming/AOT are off (the template turns them on) because WinForms
  and UI Automation aren't trim-safe; the package is larger as a result.
- **Data.** Same files as Prosper (`%APPDATA%\ProsperVoice`: settings, DPAPI-encrypted key and history, the
  content-free log), so an existing Prosper setup carries over. Packaged apps get their own copy of new files there
  (MSIX AppData virtualization), and existing files are shared.
- **Not carried over:** the tray icon and *Start with Windows* (Command Palette starts the extension), the history
  window's mic button (dictate, then follow up by typing), and the file picker (type the audio file's path).

The security notes in Prosper's README still hold: Prosper only talks to your AI endpoint and, if you connect one, your
MCP server; secrets are DPAPI-encrypted for your account; screenshots are taken only when you ask; the pill is kept
out of screenshots and screen shares; terminals never get a Ctrl+C.
