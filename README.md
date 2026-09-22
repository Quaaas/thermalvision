# ThermalViewer

Cross-platform (Linux/Windows) viewer for the Vantrue POWER TS1 USB thermal camera, as a
replacement for the manufacturer's app. Part of a portfolio project for a transition into
AI/ML/Data Science; semantic segmentation on the thermal data is planned as a later addition.

## Architecture

```
ThermalViewer.sln
├── src/
│   ├── ThermalViewer.Core     — domain models & pure processing (frame splitting,
│   │                             temperature decoding). No platform/USB/UI dependencies.
│   ├── ThermalViewer.Camera   — USB/UVC access (LibUsbDotNet), the proprietary Vantrue
│   │                             command protocol, video capture wiring.
│   └── ThermalViewer.App      — Avalonia UI (MVVM), cross-platform desktop frontend.
└── tests/
    └── ThermalViewer.Core.Tests — xUnit tests for ThermalViewer.Core.
```

**Why LibUsbDotNet:** uses libusb as its backend, so it runs unmodified on Linux and
Windows (and is actively maintained, unlike some older alternatives).

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) (Linux and Windows)
- Linux: `libusb-1.0` (usually preinstalled; otherwise e.g. `sudo apt install libusb-1.0-0`)
- Windows: the device needs a **WinUSB driver** installed, e.g. via
  [Zadig](https://zadig.akeo.ie/) — otherwise Windows loads the standard USB Video Class
  driver for the camera, which libusb can't talk to directly.

## Getting started

```bash
git clone <this-repo>
cd ThermalViewer
dotnet restore
dotnet build
dotnet test
dotnet run --project src/ThermalViewer.App
```

> **Note:** this scaffold was created in a sandbox without NuGet access — `dotnet restore`
> was **not** run there. On your first local restore, exact package versions may shift
> (the App project deliberately uses `Version="11.*"` / `"8.*"` as floating versions for
> Avalonia and CommunityToolkit.Mvvm — pin them to an exact version after the first restore
> if you like). If the first build fails, check the relevant `.csproj` and the actually
> installed package versions against the current Avalonia/LibUsbDotNet/OpenCvSharp docs.

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
- Proprietary 18-byte command protocol over USB control transfers (see
  `ThermalViewer.Camera.VantrueProtocol`), reference for the sibling camera P3:
  [jvdillon/p3-ir-camera#2](https://github.com/jvdillon/p3-ir-camera/issues/2) —
  **not yet verified for PID 45e1.**

## Open next steps

1. Verify the control transfer parameters (`bmRequestType`/`bRequest`/`wValue`/`wIndex`) in
   `UsbCameraDevice.SendControlCommand` against a real USB capture (Wireshark/usbmon).
2. Implement isochronous video streaming (`UsbCameraDevice.StartStreamingAsync`) —
   endpoint `0x81`, alternate setting 1, 1024 bytes/packet.
3. Verify the raw-data byte layout in the `256x386` dual frame (`FrameSplitter`).
4. Calibrate the raw-value-to-°C conversion formula (`TemperatureDecoder`, currently just a
   placeholder).
5. Try sending the `shutter` command to the actual camera to check whether the P3 protocol
   carries over.
6. Wire up the live image view in `MainWindow.axaml` (an Image control + WriteableBitmap,
   fed from `IThermalCameraSource.FrameReceived`).
7. Later: add semantic segmentation on the thermal data.
