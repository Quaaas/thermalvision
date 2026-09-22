# ThermalViewer

Cross-platform (Linux/Windows) viewer for the Vantrue POWER TS1 USB thermal camera, as a
replacement for the manufacturer's app. Part of a portfolio project for a transition into
AI/ML/Data Science; semantic segmentation on the thermal data is planned as a later addition.

## Architecture

```
ThermalViewer.sln
├── src/
│   ├── ThermalViewer.Core     — domain models & pure processing (frame splitting,
│   │                             temperature decoding, UVC probe/commit structure, UVC
│   │                             payload → frame reassembly). No platform/USB/UI dependencies.
│   ├── ThermalViewer.Camera   — direct USB access via an own thin libusb-1.0 interop
│   │                             (control + isochronous transfers), the proprietary Vantrue
│   │                             command protocol, streaming.
│   └── ThermalViewer.App      — Avalonia UI (MVVM), cross-platform desktop frontend.
└── tests/
    └── ThermalViewer.Core.Tests — xUnit tests for ThermalViewer.Core.
```

**Why direct libusb instead of the OS video stack:** the app reads the raw USB video
stream itself, so it sees exactly the bytes the camera sends (including the raw
temperature block), independent of V4L2/Media Foundation.

**Why an own libusb interop instead of LibUsbDotNet:** LibUsbDotNet 3.x doesn't support
isochronous transfers (its endpoint readers throw `NotSupportedException`), which is what
this camera streams over. The interop layer (`ThermalViewer.Camera/Interop`) wraps only
the ~15 libusb functions needed and runs its own libusb event loop thread.

**Streaming pipeline:** UVC probe/commit (format 1, frame 2 = 256x386, 25 fps) →
alternate setting 1 on interface 1 → ring of 8 isochronous transfers × 32 packets × 1024
bytes on endpoint `0x81` → `UvcFrameAssembler` strips the 12-byte UVC payload headers and
closes frames on EOF/FID toggle → `FrameSplitter` → `FrameReceived`.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) (Linux and Windows)
- Linux: `libusb-1.0` (usually preinstalled; otherwise e.g. `sudo pacman -S libusb` /
  `sudo apt install libusb-1.0-0`)
- Windows: `libusb-1.0.dll` next to the executable (from the libusb release archive) and a
  **WinUSB driver** for the device, e.g. via [Zadig](https://zadig.akeo.ie/) — otherwise
  Windows loads the standard USB Video Class driver, which libusb can't talk to directly.
  Isochronous transfers via WinUSB need a recent libusb (≥ 1.0.27); not tested yet.

## Getting started

```bash
git clone <this-repo>
cd ThermalViewer
dotnet restore
dotnet build
dotnet test
dotnet run --project src/ThermalViewer.App
```

> **Note:** the App project uses floating versions (`11.*` / `8.*`) for Avalonia and
> CommunityToolkit.Mvvm — pin them to exact versions once restored.

While streaming, the app detaches the kernel's `uvcvideo` driver from the camera (other
camera apps can't use it meanwhile); it is re-attached when the app disconnects.

### Linux: USB permissions

Grant the camera access via a udev rule (otherwise the app needs root for USB access):

```bash
sudo cp deploy/linux/99-thermalviewer.rules /etc/udev/rules.d/
sudo udevadm control --reload-rules && sudo udevadm trigger
```

Then re-plug the camera.

### Windows: driver

Plug in the camera, then use [Zadig](https://zadig.akeo.ie/) to install the **WinUSB**
driver for the device (VID `3474`, PID `45e1`) — not the standard UVC driver.

## Camera hardware (reference)

- **Vantrue POWER TS1**, USB-C, 512x384 super resolution / 256x192 native, -20 °C…550 °C, 15x zoom
- USB ID **3474:45e1** ("Thermal Master Co.,Ltd Camera" — OEM chip manufacturer)
- UVC 1.10 compliant, no official VC_EXTENSION_UNITs in the descriptor
- Two frame formats (FORMAT_UNCOMPRESSED, YUY2, 16 bit/pixel, 25 fps):
  - `256x194` — plain image (256x192 + 2 metadata rows)
  - `256x386` — dual frame: image on top, raw 16-bit temperature block below it
- Proprietary 18-byte command protocol over vendor control transfers (see
  `ThermalViewer.Camera.VantrueProtocol`), reference for the sibling camera P3:
  [jvdillon/p3-ir-camera — P3_PROTOCOL.md](https://github.com/jvdillon/p3-ir-camera/blob/main/P3_PROTOCOL.md).
  Shutter verified on PID 45e1; register reads to be confirmed.

## Open next steps

1. First run of the isochronous streaming against the camera — check the status line:
   frames/s ≈ 25, `dropped` stays low; a constant `last … B` drop size points to a wrong
   frame size/format index.
2. Verify the dual-frame layout and the 1/64 K temperature formula with real frames
   (dump one raw frame as a test fixture; sanity check: skin ≈ 33 °C).
3. Live image view in `MainWindow.axaml` (Image + WriteableBitmap, false-color LUT,
   min/max/spot temperature).
4. Windows: WinUSB + libusb isochronous support.
5. CI (GitHub Actions: build + test), pin package versions.
6. Later: semantic segmentation on the thermal data.
