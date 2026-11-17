# Third-party components

The repository contains the original bridge source and license notices. It does not vendor third-party executable binaries. The build downloads the following components directly from their maintainers.

## Carabiner 1.2.0 and Ableton Link

- Carabiner copyright Deep Symmetry, LLC; GPL-2.0-or-later.
- [Official release and Windows binary](https://github.com/Deep-Symmetry/carabiner/releases/tag/v1.2.0).
- [Carabiner source](https://github.com/Deep-Symmetry/carabiner/tree/v1.2.0).
- [Carabiner license](https://github.com/Deep-Symmetry/carabiner/blob/v1.2.0/LICENSE.md). The GPL text is also in this repository's `LICENSE`.
- Includes Ableton Link 3.1.2, copyright Ableton AG; GPL-2.0-or-later for this open-source use, and Mongoose under the applicable GPL licensing in the Carabiner distribution.
- Includes gflags, copyright Google Inc.; BSD-3-Clause. See `licenses/GFLAGS-BSD-3-Clause.txt`.

Carabiner runs as a separate helper process; the bridge communicates over a local TCP connection. The published single-file application embeds its unmodified executable.

**If distributing a binary with Carabiner embedded, provide the corresponding Carabiner source, including its pinned submodules and build instructions, in accordance with the GPL.** Merely attaching the bridge's own source does not include those components. Obtain the complete source checkout with:

```sh
git clone --branch v1.2.0 --recurse-submodules https://github.com/Deep-Symmetry/carabiner.git
```

The initial repository publication is source-only, with no prebuilt binary release. The build workflow checks compilation and does not upload binary artifacts.

## Windows MIDI Services API Preview 11

- Version `0.99.92-preview.11`; copyright Microsoft Corporation, MIT.
- [Official package and release notes](https://github.com/microsoft/MIDI/releases/tag/inbox-preview-11).
- [Source](https://github.com/microsoft/MIDI/tree/inbox-preview-11).
- MIT text: `licenses/MICROSOFT-MIT.txt`.

The original loopback code follows the API pattern demonstrated in Microsoft's MIT-licensed C# loopback sample. The build extracts the WinRT metadata, x64 DLL, and resource PRI from the official package. No system service or transport installer is bundled.

The preview release also specifies developer go-live commitments: support Windows 11 25H2 or later, disclose the preview API, keep the integration updated, provide an update mechanism, and transition away from app-local preview binaries when the system API becomes available. Maintainers publishing binaries should read those release notes in full. App updates can be delivered by replacing the EXE; the project has no automatic updater or guaranteed ongoing maintenance.

## .NET, Windows Forms, Windows SDK projections, and C#/WinRT

Microsoft components retain their MIT and applicable third-party licenses. Restore uses the NuGet packages declared in the project, including C#/WinRT `2.2.0` and Windows SDK projection version `10.0.26100.57`. Self-contained builds include the .NET runtime and Windows Desktop runtime.

- https://github.com/dotnet/runtime
- https://github.com/dotnet/winforms
- https://github.com/microsoft/CsWinRT

Preserve the runtime's license and third-party notice files when packaging and redistributing builds.

Neither loopMIDI nor the proprietary virtualMIDI SDK/driver is included or redistributed.
