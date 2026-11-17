# LJ Link MIDI Clock for Daslight 4

An open-source Windows bridge that converts **Ableton Link from LJ Link into MIDI Clock for Daslight 4**, with an integrated virtual MIDI connection.

Daslight 4 can synchronize its BPM to MIDI Clock. This bridge connects that input to an Ableton Link session, so your lighting tempo can follow the tempo published by LJ Link.

```text
DJ setup → LJ Link → Ableton Link → this bridge → virtual MIDI → Daslight 4
```

The single executable includes the Link helper and .NET runtime. On a supported Windows installation, you do not need to run loopMIDI or install virtualMIDI separately.

**Status: early preview.** The integrated MIDI connection uses a preview Microsoft API. Clock reception and the virtual MIDI connection have been tested locally; full lighting-show synchronization and every Daslight 4 configuration have not been validated. Test your setup before using it in a live show.

## Requirements

- Windows 11 **25H2 or later**, x64.
- Windows MIDI Services and its MIDI 2.0 loopback transport available and enabled.
- LJ Link with its Ableton Link output enabled, or another application publishing an Ableton Link session.
- Daslight 4 on the same PC as this bridge.
- Network connectivity between the bridge and any Link participants on other devices.

The bridge currently uses **Windows MIDI Services API 0.99.92-preview.11**. Windows versions and preview API releases must be compatible. It does not install or replace system-wide MIDI components. An existing external MIDI output is also supported if the integrated port cannot be created.

## Set up Daslight 4

1. Start **LJ Link MIDI Clock before opening Daslight 4**. It creates the virtual MIDI connection automatically.
2. Enable **Ableton Link output in LJ Link**. The bridge follows the Link session; it does not require Ableton Live.
3. Select **LJ Link Clock (integriert)** in the bridge and click **Clock starten**. The current application UI uses German labels: these mean “integrated” and “Start clock”.
4. Open Daslight 4 and go to **Edit → Preferences → MIDI**. In a German installation, this is **Bearbeiten → Einstellungen → MIDI**.
5. Under **MIDI Clock / MIDI Uhr → Device / Gerät**, select **LJ Link Clock**. Daslight may append a numeric port index to this name.
6. In Daslight's BPM controls, select **MIDI** as the tempo source.
7. Set the scenes you want to follow the music to **BPM synchronization**. Receiving a clock does not automatically change the timing mode of every scene.

Keep the bridge open while using it. If Daslight was already open when the ports were created, reopen Daslight so it refreshes its device list.

### Which MIDI port should I select?

| Name | Purpose |
| --- | --- |
| **LJ Link Clock** | The MIDI input to select as the clock source in **Daslight 4**. |
| **LJ Clock Sender** | Internal sending side of the Windows loopback pair. Do not select this as Daslight's clock input. |

Windows exposes both ends of the loopback. These two different names are expected. Repeated pairs with identical names are not expected: the bridge removes stale pairs with its own exact names and descriptions at startup, uses stable endpoint identifiers, and prevents multiple running instances. Other MIDI devices are left alone.

## Clock behavior

- Sends MIDI Timing Clock (`F8`) at **24 pulses per quarter note**.
- Uses Link tempo and beat position, with phase-based scheduling.
- Reads the Link session without setting its tempo, beat position, or playback state.
- Pauses clock output when no other Link participants are detected or received timeline data becomes stale. Link peer-loss detection itself can take additional time.
- Runs continuously while a peer is present; it **does not follow the DJ player's Play/Pause state**.
- Optional **MIDI Start/Stop** sends `FA` when the bridge starts output and `FC` when it stops or loses synchronization. This follows the bridge's state, not the DJ transport.
- Skips overdue pulses after a timing stall rather than emitting a burst of old messages. Windows scheduling can still introduce jitter.

MIDI Clock provides tempo synchronization. It does not transfer track titles, waveforms, cue points, DMX, or a guaranteed lighting-scene bar/phrase position.

## Build from source

Install the **.NET 8 SDK** on Windows, then run in PowerShell:

```powershell
git clone https://github.com/nicorixx/lj-link-midi-clock.git
cd lj-link-midi-clock
./scripts/Build.ps1
```

Output: `artifacts/win-x64/LJLinkMidiClock.exe`.

The dependency script downloads pinned releases of **Carabiner 1.2.0** and **Windows MIDI Services Preview 11** from their official GitHub releases, verifies SHA-256 checksums, and extracts the required x64 files into the ignored `.deps` directory. Dependencies and build output are not committed to this repository. The MIDI SDK archive includes multiple platforms and debugging data, so the download is substantially larger than the files used by the app.

The published EXE is self-contained and extracts its bundled components to a temporary folder when launched. It does not require a separate .NET runtime installation. The current UI is German; this repository's documentation is English.

## Validation and troubleshooting

The following checks were completed on the original development PC:

- Parser and clock calculations at 60, 90, 120, 128, and 180 BPM.
- Peer-loss and stale-timeline calculation checks.
- Virtual port visibility through the classic Windows WinMM MIDI API.
- Receipt of **24 out of 24** test clock messages through the virtual connection.
- Repeated clean shutdown and cleanup of stale test ports.
- A live Link session and the bridge's running tempo display.

For calculation checks, run `LJLinkMidiClock.exe --self-test`. `--port-test` creates the virtual connection, sends test clock messages, then removes it; run this only with the bridge closed and outside a live show. `--link-test` joins Link for a short passive test without sending MIDI. Diagnostic reports are written beside the extracted application files when using the single-file build. GitHub Actions builds the app and runs the calculation checks; it does not validate Daslight or a live MIDI setup.

If no Link peers appear, check LJ Link's Link output, local network connectivity, VPNs, network isolation, and the private-network firewall permission for the bundled Carabiner helper. If the integrated MIDI port fails, check Windows version and MIDI Services availability. Reopen Daslight after a port change. If BPM is received but a scene does not follow it, check that scene's BPM synchronization mode.

Stop and restart the clock after a Link error. Normal application shutdown removes its own MIDI pair. After a forced termination, the next bridge instance cleans up its stale pair. Restarting the Windows MIDI service interrupts other MIDI applications, so do not do that during an active show.

## License and acknowledgments

The original bridge code is licensed under **GNU GPL version 2 or later**; see [LICENSE](LICENSE). Dependencies retain their own licenses. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for source links, versions, and redistribution considerations, especially before publishing binary releases with bundled dependencies.

Built with [Carabiner](https://github.com/Deep-Symmetry/carabiner), [Ableton Link](https://github.com/Ableton/link), [Windows MIDI Services](https://github.com/microsoft/MIDI), [.NET](https://github.com/dotnet/runtime), and [C#/WinRT](https://github.com/microsoft/CsWinRT). Thanks to their maintainers, and to [LJ Link](https://github.com/skyplayser/LJ-Link).

Independent project, not affiliated with or endorsed by Daslight, Ableton, Microsoft, or the LJ Link project. Product names and trademarks belong to their respective owners.

References: [Daslight 4](https://www.daslight.com/en/daslight4), [Daslight 4 manual](https://eu-litterature.n-g.co/Release/daslight_4_manual_en.pdf), [Windows MIDI Services Preview 11](https://github.com/microsoft/MIDI/releases/tag/inbox-preview-11).
