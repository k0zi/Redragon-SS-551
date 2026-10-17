# Redragon SS-551 protocol notes

Source: static analysis of the vendor Windows app "Redragon Stream Station"
3.10.194 (`SDLibrary1.dll`, class `SDDevice`, exported symbols). Items marked
**unverified** still need confirmation on real hardware.

## Hardware

- USB `0200:2000`, strings "HOTSPOTEKUSB" / "HOTSPOTEKUSB HID DEMO" (Mirabox / HotSpot).
- The vendor app is a rebranded Mirabox "Stream Dock". Its model table puts
  `SS-551` in the same group (type 4) as `MBox-N3E` / `VSDN3`: N3-family hardware.
- Interface 0: vendor HID, usage page `0xFFA0`, no report IDs.
  IN EP 0x82 (512 B reports), OUT EP 0x03 (1024 B reports).
- Interface 1: standard boot keyboard (8 B reports).

## Host -> device

Each command is one output report: report ID `0x00` + 1024-byte payload,
zero-padded. Every command payload starts with `CRT\0\0` (`43 52 54 00 00`),
followed by the command name at offset 5.

| Command | Payload | Vendor function |
|---|---|---|
| Brightness | `CRT\0\0LIG\0\0` + `[10]` = 0..100 | `addAdjustBrightnessPack` |
| Clear key  | `CRT\0\0CLE\0\0\0` + `[11]` = key, `0xFF` = all | `addClearCommand` / `addClearAllCommand` |
| Wake screen | `CRT\0\0DIS`, `[10]` = 1 | `addWakeUpScreenPack` |
| Screen off | `CRT\0\0HAN`, `[10]` = 1 | `addScreenOffPack` |
| Flush / show | `CRT\0\0STP` | `addFinishCommand` |
| Heartbeat | `CRT\0\0CONNECT` | `addHeartbeatPack` |
| Image header | `CRT\0\0BAT\0\0` + `[8..11]` = JPEG size (BE) + `[12]` = key | `sendPicSizeCommand` |

Image upload: `BAT` header, then the raw JPEG in 1024-byte output reports,
then `STP`.

### Key images (verified on hardware)

- Key indices for `BAT` / `CLE`: 1..6, top row 1-2-3, bottom row 4-5-6
  (same numbering as the key events).
- Format: **64x64 JPEG, rotated 90° clockwise** before upload (the panel
  shows the image rotated 90° counter-clockwise). The outermost ~1 px is
  hidden by the key bezel.
- All six keys sit on **one shared panel**. An image larger than 64x64 is
  not clipped and spills into neighbouring keys; anything outside the key
  cells (e.g. the factory wallpaper) stays until `CLE 0xFF` + `STP`.

## Device -> host

512-byte input reports:

| Prefix | Meaning |
|---|---|
| `ACK\0\0OK\0\0` + `[9]` = key, `[10]` = state | key / knob event (`sigHandleKeyEvents`); key 0 = plain ack / wake-up |
| `ACK\0\0TF…` + `[9..13]` | firmware/status info |
| `ACK\0\0CB\0\0OK` | callback ack |
| `CRT\0\0MOD` + `[10]` = mode | mode change (`sigGetModeCmd`) |
| `HANDLER` | device requests handshake (only used by StreamDock[296]) |
| `error` | device-side error |

## Session start (verified)

The device sends **no input reports** until it receives the `QUCMD` packet
the vendor app sends right after opening:
`CRT\0\0QUCMD` + `[10..15]` = `11 11 00 11 00 11`
(`[10]` is `0x1F` and `[13]` is `0xFF` under conditions not yet analysed).

Firmware version via `hid_get_input_report` (report ID 0): `V3.SS-551.02.008`.

## Event codes (verified on hardware, firmware V3.SS-551.02.008)

| Code `[9]` | `[10]` | Source |
|---|---|---|
| `0x01`..`0x06` | 1 = down, 0 = up | LCD keys |
| `0x25`, `0x30`, `0x31` | 1 = down, 0 = up | the 3 physical buttons below the LCD keys, left to right |
| `0x50` / `0x51` | always 0 | big knob: left (CCW) / right (CW), one step per report |
| `0x90` / `0x91` | always 0 | left small knob: left / right |
| `0x60` / `0x61` | always 0 | right small knob: left / right |
| `0x35`, `0x33`, `0x34` | 1 = down, 0 = up | knob pushes: big, left small, right small |

