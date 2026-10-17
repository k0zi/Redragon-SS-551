# Redragon SS-551 for Linux

Unofficial Linux driver and configuration app for the **Redragon SS-551**
stream deck (Mirabox "Stream Dock" N3-family hardware, USB `0200:2000`):
6 LCD keys, 3 buttons and 3 knobs.

The vendor only ships a Windows app ("Redragon Stream Station"). This
project replaces it on Linux: no kernel module, no Wine, just hidapi in
user space.

> **Disclaimer:** not affiliated with or endorsed by Redragon or Mirabox.
> The USB protocol was reverse-engineered (see [`docs/protocol.md`](docs/protocol.md));
> it is tested on firmware `V3.SS-551.02.008` only. Use at your own risk.
>
> Most of the code was written with the help of [Claude Code](https://claude.com/claude-code).

## Features

- Images on the 6 LCD keys, multiple pages, deck brightness
- Actions for every key, button, knob push and knob turn (per direction)
- Macros: a list of steps (hotkeys, media keys, shell commands, apps, URLs,
  page switching, brightness, delays)
- Multi-stage long press
- State indicator keys (toggle, speaker / mic mute, custom command)
- Pick an installed application: its icon and name go on the key
- Tray icon, can start hidden
- Light / dark themes (Redragon red by default, plus Linux distro inspired
  ones) via [KD.Avalonia.Rice](https://www.nuget.org/packages/KD.Avalonia.Rice)

## Repository layout

| Path | Contents |
|---|---|
| `driver/` | `libss551`, a small C library on top of hidapi, plus `ss551-probe`, a command-line test tool |
| `app/` | C# / Avalonia desktop app (`SS551.App` UI, `SS551.Core` logic, `SS551.Native` P/Invoke bindings) |
| `udev/` | rule granting the logged-in user access to the device |
| `packaging/` | `.deb` / `.rpm` build (nfpm) and `install.sh` |
| `docs/protocol.md` | the reverse-engineered USB protocol |

## Requirements

- Linux with hidraw and uinput (tested on Ubuntu)
- `cmake gcc pkg-config` and the hidapi headers
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Optional: `wpctl` (PipeWire) for the mute indicator keys, `gio` and
  `xdg-open` for the App / Open actions

```sh
sudo apt install cmake gcc pkg-config libhidapi-dev            # Debian / Ubuntu
sudo dnf install cmake gcc pkgconf hidapi-devel                # Fedora
sudo zypper install cmake gcc pkg-config libhidapi-devel       # openSUSE
```

## Install (deb / rpm package)

```sh
git clone https://github.com/k0zi/Redragon-SS-551.git
cd Redragon-SS-551
packaging/install.sh               # builds the package and installs it with apt / dnf / zypper
packaging/install.sh --uninstall   # removes it again
```

The package puts the app in `/usr/lib/ss551`, the `ss551` and `ss551-probe`
commands in `/usr/bin`, a menu entry, and the udev rule in
`/usr/lib/udev/rules.d/` (no manual udev setup needed; re-plug the deck once
after the first install). It is self-contained: no .NET runtime is needed on
the target machine. Tested on Ubuntu 24.04, Debian 13, Fedora, openSUSE
Tumbleweed and Leap 15.6 (x86_64 only).

To build the packages without installing them:

```sh
packaging/build-packages.sh             # .deb and .rpm into artifacts/packages/
packaging/build-packages.sh deb         # only one format
```

The packages require at least the glibc of the machine they were built on
(the build script reads this from the compiled driver), so build on the
oldest distribution you want to support.

**Versioning:** [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning),
`<year>.<month>.<git height>` (e.g. `26.10.3`). `version.json` holds the
year and month; when the month changes, `build-packages.sh` updates it
(commit that change so the patch number restarts from there; `--no-bump`
skips it). Builds outside `main` and `v*` tags get a `~g<commit>` suffix,
which sorts before the release of the same version. Show the current
version with `dotnet tool restore && dotnet nbgv get-version`.

To start the app hidden in the tray on login:

```sh
mkdir -p ~/.config/autostart
cat > ~/.config/autostart/ss551.desktop <<EOF
[Desktop Entry]
Type=Application
Name=Redragon SS-551
Exec=/usr/bin/ss551 --hidden
X-GNOME-Autostart-enabled=true
EOF
```

## Run from source

```sh
git clone https://github.com/k0zi/Redragon-SS-551.git
cd Redragon-SS-551

sudo cp udev/70-redragon-ss551.rules /etc/udev/rules.d/
sudo udevadm control --reload && sudo udevadm trigger
# re-plug the deck
```

Hotkeys and media keys are sent through a virtual uinput keyboard, so the
user needs write access to `/dev/uinput` (granted by default on Ubuntu
desktop sessions; check with `test -w /dev/uinput && echo ok`).

### Build and run

```sh
cmake -S driver -B driver/build -DCMAKE_BUILD_TYPE=Release
cmake --build driver/build

dotnet run --project app/src/SS551.App               # opens the window
dotnet run --project app/src/SS551.App -- --hidden   # start in the tray
```

The app copies `driver/build/libss551.so*` next to its binaries, so build
the driver first. The profile is stored in `~/.config/ss551/profile.json`.

## Probe tool

Installed as `ss551-probe` by the package; from a source build it is
`driver/build/ss551-probe`.

```sh
driver/build/ss551-probe info
driver/build/ss551-probe listen               # print key/knob events
driver/build/ss551-probe brightness 50
driver/build/ss551-probe image 1 icon.jpg     # 64x64 JPEG, pre-rotated 90° CW
driver/build/ss551-probe raw 5=STP            # send an arbitrary CRT packet
```

## Actions, macros and long press

Every key, button and knob push has a **press** macro and any number of
**long-press stages**; knob turns have a macro per direction. A macro is a
list of steps run in order.

- Without long-press stages the press macro runs immediately on press.
- With stages, each stage has a hold time counted from the previous stage
  (2 s + 2 s fires at 4 s). Every stage reached while holding runs; the
  press macro then runs on release only if no stage was reached.

| Step type | Parameter |
|---|---|
| Hotkey | key combination, e.g. `Ctrl+Alt+T`, `Super+E`, `F5` |
| Media | play/pause, next, previous, stop, mute, volume up/down |
| Command | shell command (`/bin/sh -c`) |
| App | installed application, chosen from a list (`gio launch`) |
| Open | URL, file or folder (`xdg-open`) |
| Page | next / previous / go to page N |
| Brightness | change deck brightness by N % |
| Delay | wait N ms before the next step |

**Choose application…** in the key editor takes an installed app's icon
(PNG or SVG, resolved through the current icon theme) and name, and makes
the key launch it if the key had no action yet. **App…** next to the icon
field only takes the icon.

### State indicator keys

A key can show a second look while "on": `Toggle` (flips on each press),
`SpeakerMuted` / `MicMuted` (via `wpctl`), or `Command` (polled shell
command, exit code 0 = on). Optionally it runs a different macro while on.

## Troubleshooting

- **Device not found / permission denied:** check that the udev rule is
  installed and the deck was re-plugged; `ls -l /dev/hidraw*` should show
  an ACL for your user (`getfacl /dev/hidrawN`). `lsusb | grep 0200:2000`
  confirms the deck is detected.
- **Hotkeys do nothing:** `/dev/uinput` is not writable for your user.
- **Strange behaviour while testing:** close the app before running
  `ss551-probe`, so the two don't talk to the deck at the same time.

## Contributing

Issues and pull requests are welcome, especially reports from other
firmware versions or other N3-family decks. When reporting a bug, please
include the output of `ss551-probe info` and your distribution.

## License

[MIT](LICENSE)
