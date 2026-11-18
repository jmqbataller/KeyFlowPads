# KeyFlow Pads

![KeyFlow Pads](assets/branding/KeyFlowPads-Logo.png)

**Seamless Worship. Effortless Control.**

A compact Windows companion for church media volunteers who run worship audio
and presentation slides together.

[Download the Windows installer](https://github.com/jmqbataller/KeyFlowPads/releases/latest)
 · [Report an issue](https://github.com/jmqbataller/KeyFlowPads/issues)
 · [Contribute](CONTRIBUTING.md)

## Start here

Download **KeyFlowPads-1.3.0-Setup.exe** from Releases and copy that single file
to your church's Windows PC. Run it and open KeyFlow Pads from Start.
The installer includes the runtime and twelve worship pads.

## Made for live worship

- Twelve musical keys with continuous looping.
- Smooth overlapping key changes with a two-second default crossfade.
- Floating player with standard and compact modes.
- Persistent music library with local file import and song crossfades.
- Seven configurable vocal profiles with selected and sounding key indicators.
- Optional YouTube window with search and browser fallback.
- Offline local playback with no account requirement.

## Open source

The application code is available under the [MIT license](LICENSE).
The maintainer confirmed permission to redistribute the included Churchfront
recordings. Their original rights and usage terms apply to the recordings and
decoded excerpts. See [component notices](LICENSES.md).

## Verification and limitations

The original engine completed a 30-minute, 30-second Windows playback test with
three loop crossings and zero reported decoder underruns or endpoint discontinuities.
Later checks exercised imported music transitions and the updated controls.
YouTube playback depends on internet access, WebView2, and YouTube's availability.
The ad filter can leave some video ads visible.

Test your output connection and volume before a service.

KeyFlow Pads is an offline Windows player for twelve Churchfront WARM worship pads. It floats over presentation software and uses a single Windows audio stream for looping and key changes.

## Install and use

1. On Windows 10 or Windows 11 x64, run the versioned installer, such as `KeyFlowPads-1.3.0-Setup.exe`. To upgrade, exit KeyFlow Pads from the tray first. Choose the destination and, if useful, the desktop shortcut. Setup installs for your Windows user.
2. Open **KeyFlow Pads** from Start. Click any key to start that pad. Click a different key to mix into it. The default crossfade is two seconds.
3. Use **Pause** to hold your position, **Resume** to return to it, or **Stop** to fade out and reset. Minimize to the tray while you work and double-click the tray icon to restore the player.
4. Use **Settings** to choose your audio output, crossfade duration, transport fade, and window options. Under **Vocal range**, set a profile’s transposition from −6 to +6 semitones. All profiles start at zero. The panel shows the selected musical key and the pad the app will play.
5. Use Windows **Installed apps** to uninstall. Preferences are stored in `%LOCALAPPDATA%\KeyFlow Pads\settings.json` so they are available if you reinstall.

The portable ZIP contains the same Windows x64 build. Extract the entire ZIP and run `KeyFlowPads.exe` from the extracted folder. The .NET Desktop Runtime is included. The installer is unsigned, so Windows SmartScreen may show a reputation prompt.

## Music Library

Choose **Import Music** below the main playback controls to open the file picker. **Library** opens the saved list. Search it and double-click a track or select **Play track**. The main player's Pause, Resume, Stop, and volume controls apply to local music too.

Imports are stored per Windows user in `%LOCALAPPDATA%\KeyFlow Pads\Music`. KeyFlow creates its own stereo WAV copy for immediate playback after a restart. This uses about 10 MB per minute. You can move the source file afterward. The library is local to the PC where you import the music.

MP3 and WAV imports are supported. M4A, AAC, FLAC, and WMA decoding depends on the Windows media codecs. Songs play once. Switching while a song is playing crossfades into the next song or pad, using the configured duration when enough audio remains. The player skips near-silent sections at the edges of imported files.

## YouTube

Choose **YouTube** below the main playback controls and enter a search. Select a result in the visible YouTube window. This feature requires internet access and the Microsoft Edge WebView2 Runtime. **Open in browser** is available when the site or runtime cannot load inside KeyFlow.

**Block ads** is on by default and remembers your choice. It blocks known ad requests, hides supported sponsored placements, and uses YouTube’s Skip button when it becomes available. Some video ads may still appear. The setting applies within KeyFlow’s YouTube window.

The YouTube window uses WebView2's DNS configuration. When using **Open in browser**, you can configure `https://dns.adguard-dns.com/dns-query` as the Secure DNS provider in a compatible browser. YouTube ads served from video domains can still appear. [AdGuard DNS setup](https://adguard-dns.io/en/public-dns.html) · [AdGuard on YouTube DNS filtering](https://adguard.com/en/support/adguard-mini-android/about/about_the_settings/video_browser/youtube_hard_to_block.html)

YouTube audio plays through YouTube's player. It pauses local playback when YouTube starts producing audio, and the in-app video stops when the YouTube window loses focus. The local crossfade applies to imported songs and pads. [YouTube Developer Policies](https://developers.google.com/youtube/terms/developer-policies)

## Audio behavior

- The original 44.1 kHz stereo MP3s are included byte for byte. The app reads the recordings’ iTunSMPB metadata to exclude codec delay and padding. Prepared loop markers trim the musical fade-in and fade-out. A two-second overlap blends sustained passages from the same key.
- Each pad has an immediately available PCM entry. Background threads decode ahead into bounded buffers. An event-driven WASAPI stream renders audio independently of the window and stays open through pad switches, pause, and stop.
- Key changes use an equal-power transition. Repeated changes blend from the current levels into the newest key. Up to one voice per key can participate while a transition is in flight.
- Vocal profiles select the matching original recording by pitch class. The bundled recordings keep their original octave.
- A disconnected endpoint can be changed in Settings. Changing the device pauses playback and lets the operator choose when to resume. Standard desktop windows are supported by the floating player. Exclusive fullscreen behavior depends on the presentation application and Windows.

## Build

Use the .NET 10 SDK and PowerShell 7. Inno Setup 7 builds the installer. Run this command from the source root.

```powershell
./scripts/Build-Release.ps1 -InnoCompiler 'C:/path/to/ISCC.exe'
```

The self-contained executable and support files are in `artifacts/publish`. The setup executable and portable ZIP are in `release`. Use `-SkipInstaller` when building only the portable version.

Run these commands to regenerate the loop data and reproduce the checks.

```powershell
dotnet run -c Release --project tests/KeyFlowPads.Checks -- prepare assets/audio
dotnet run -c Release --project tests/KeyFlowPads.Checks -- render assets/audio
dotnet run -c Release --project tests/KeyFlowPads.Checks -- music assets/audio
dotnet run -c Release --project tests/KeyFlowPads.Checks -- live assets/audio
dotnet run -c Release --project tests/KeyFlowPads.Checks -- soak assets/audio 1830
```

The last two commands play at reduced volume on the Windows default output. The live test isolates the test process’s audio and requires Windows 11 or a Windows build that supports WASAPI process loopback. The soak checks the shared endpoint mix, so run it on an otherwise quiet endpoint. Both checks analyze capture in memory.
