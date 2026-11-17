## Prosper Voice: install (no admin rights)

1. Download `ProsperVoice.exe` and `install.cmd` into the same folder.
2. Double-click `install.cmd`. It installs for your Windows account only, in `%LOCALAPPDATA%\Programs\ProsperVoice`, adds Start menu and desktop shortcuts, and starts the app.
3. Add your OpenAI API key in Settings. It is encrypted for your Windows account.

Running `ProsperVoice.exe` from any folder works too. Requires Windows 10 or 11 (.NET Framework 4.8 is built in).

**Windows SmartScreen** may warn that the app is from an unknown publisher, because the file isn't code-signed. Click **More info**, then **Run anyway**. To check that the file is the one built here, compare it with `SHA256SUMS.txt`:

```powershell
Get-FileHash .\ProsperVoice.exe -Algorithm SHA256
```

Built on GitHub Actions from this tag with `build.cmd`; the self-test passed before publishing.

## Prosper for PowerToys Command Palette

The same dictation, spoken edits and Assistant as an extension for [PowerToys Command Palette](https://learn.microsoft.com/windows/powertoys/command-palette/overview) (Win+Alt+Space): Dictate and Assistant commands, typed "Ask Prosper" requests, history and settings pages, and a Dock band. Needs PowerToys with Command Palette turned on.

1. Download `Install-ProsperPalette.ps1`, plus `ProsperPalette-x64.msix` and `ProsperPalette-x64.cer` (or the `ARM64` pair for ARM laptops), into one folder.
2. In that folder run `powershell -ExecutionPolicy Bypass -File .\Install-ProsperPalette.ps1`. The package is signed with a certificate made for this build; the script trusts it (one administrator prompt) and installs the extension for your account.
3. In Command Palette, run **Reload**, then **Prosper settings** to add your OpenAI key.

Check the files against `ProsperPalette-x64-SHA256SUMS.txt` / `ProsperPalette-ARM64-SHA256SUMS.txt`. Details: [`prosper-cmdpal/README.md`](https://github.com/shipengineai/prosper-voice-windows/blob/main/prosper-cmdpal/README.md).
