# ThermalVision — Requirements

Status: **Draft v0.1** (2026-09-22) — derived from stakeholder interview after the streaming POC.
Priorities follow MoSCoW (**M**ust / **S**hould / **C**ould / **W**on't for now).

---

## 1. Purpose and scope

ThermalVision is an open, cross-platform replacement for the vendor software of the
VANTRUE POWER TS1 thermal camera (USB VID:PID `3474:45e1`, UVC 1.1, 256×192 native).

It consists of:

- a **desktop viewer** for live view and playback of recordings (Linux, Windows),
- a **headless recorder** for long-term unattended recordings on a Raspberry Pi,
  controlled through a small web interface,
- an **offline ML pipeline** (Python) that turns recordings into a dataset and fine-tunes
  a semantic segmentation model, whose result is used again in the viewer.

### Stakeholders

| Stakeholder | Interest |
|---|---|
| Owner / primary user | Long-term recordings (e.g. radiators, effect of an air conditioner), replacing the slow, Windows-only vendor app |

### Primary use cases

- **UC-1 Long-term recording:** Place a Raspberry Pi with the camera in front of an object, start a recording via the browser, check the live preview, let it run for hours or days.
- **UC-2 Playback:** Review a recording on the desktop, scrub through time, adjust palette and temperature range after the fact.
- **UC-3 Live view:** Camera connected directly to the desktop, e.g. for aiming or quick checks.
- **UC-4 Dataset & training:** Export recordings to a dataset, label, fine-tune a segmentation model in Python, run it offline on recordings in the viewer.

---

## 2. System overview

| Component | Tech | Runs on |
|---|---|---|
| `ThermalViewer.Core` | C# / .NET 8, no platform deps | all |
| `ThermalViewer.Camera` | C#, libusb-1.0 interop (isochronous) | all |
| `ThermalViewer.App` (viewer) | Avalonia UI | Linux x64, Windows x64 |
| `ThermalViewer.Recorder` (new) | .NET console/service + ASP.NET Core | Linux ARM64 (Raspberry Pi) |
| `ml/` (new) | Python (PyTorch or similar), ONNX export | Desktop |

The **recording file format** (§4) is the contract between recorder, viewer and ML pipeline.

---

## 3. Camera access (CAM)

| ID | Prio | Requirement | Acceptance criterion |
|---|---|---|---|
| CAM-01 | M | Read video directly via USB isochronous transfers (no OS video stack). | Stream runs with the kernel UVC driver detached; driver is re-attached on close. |
| CAM-02 | M | Stream the 256×386 double frame at 25 fps. | ≥ 24.5 fps average over 10 min on the reference desktop, drop rate < 1 %. |
| CAM-03 | M | Decode temperatures from the raw 16-bit half (1/64 K). | Verified against references: skin ≈ 33–35 °C, ice water ≈ 0 °C, boiling water ≈ 100 °C (±2 °C incl. emissivity effects). |
| CAM-04 | M | Trigger shutter/FFC manually. | Command works; the following frame(s) are discarded cleanly. |
| CAM-05 | S | Read device info (name, version, serial). | Shown in viewer and recorder status. |
| CAM-06 | S | Recover from camera disconnect/reconnect without restarting the application. | Unplug/replug during streaming resumes streaming within 10 s. |
| CAM-07 | C | Switch gain (low/high range) via vendor commands. | Verified on PID 45e1; temperature range reported correctly. |
| CAM-08 | C | Support sister devices with the same chipset (e.g. P3, PID 45a2). | Device table instead of hard-coded PID. |

---

## 4. Recording and file format (REC, FMT)

| ID | Prio | Requirement | Acceptance criterion |
|---|---|---|---|
| FMT-01 | M | Store **raw 16-bit temperature values**, never only rendered images. | Palette/range can be changed arbitrarily on playback. |
| FMT-02 | M | Every frame has its own timestamp (UTC, µs). | Variable frame rates and gaps are reproduced correctly on playback. |
| FMT-03 | M | Self-describing header: format version, resolution, pixel encoding, camera info, recording settings. | A file can be opened without external information. |
| FMT-04 | M | Format is documented and readable from Python without C#. | Reference reader in `ml/` loads a recording into a NumPy array. |
| FMT-05 | S | Optionally store the 8-bit image half and metadata rows. | Configurable; off by default for long-term recordings. |
| FMT-06 | S | Robust against power loss: a truncated file is readable up to the last complete frame. | Test: kill process mid-recording, file opens and plays. |
| FMT-07 | C | Mark events in the stream (shutter/FFC, settings changes, user markers). | Visible on the playback timeline. |
| FMT-08 | C | Lossless compression. | Measured size reduction, CPU load within NFR-04 on the Pi. |
| REC-01 | M | Configurable recording rate from time-lapse (e.g. 1 frame / 60 s) up to full 25 fps. | Both extremes verified. |
| REC-02 | M | Recording can be started from the desktop viewer (Linux, Windows). | Same file format as the Pi recorder. |
| REC-03 | S | Split long recordings into segments (by size or duration). | 24 h recording produces several readable segments. |
| REC-04 | S | Stop automatically when free disk space falls below a threshold. | No corrupt file or full disk. |
| REC-05 | C | Adaptive rate: record faster when the scene changes. | Threshold-based, configurable. |

Sizing reference (temperature half only, 98 304 B/frame, uncompressed):

| Rate | Per day |
|---|---|
| 1 frame / 60 s | ~140 MB |
| 1 frame / 10 s | ~850 MB |
| 1 fps | ~8.5 GB |
| 25 fps | ~212 GB (not a target for long recordings) |

---

## 5. Desktop viewer — live view (VIEW)

| ID | Prio | Requirement | Acceptance criterion |
|---|---|---|---|
| VIEW-01 | M | Live image with false-colour palette. | Smooth at 25 fps on the reference desktop. |
| VIEW-02 | M | Selectable palettes (at least grey, iron, rainbow). | Switch without stream interruption. |
| VIEW-03 | M | Automatic and manual temperature range. | Manual min/max in °C. |
| VIEW-04 | S | Spot temperature under cursor, min/max with position. | Values match decoded raw data. |
| VIEW-05 | S | Snapshot (PNG + raw frame). | Raw snapshot opens in playback. |
| VIEW-06 | C | Area (rectangle) and line profile measurements. | — |
| VIEW-07 | C | Upscaled display (e.g. 512×384). | Interpolation selectable. |

## 6. Desktop viewer — playback (PLAY)

| ID | Prio | Requirement | Acceptance criterion |
|---|---|---|---|
| PLAY-01 | M | Open and play recordings, scrub via timeline, show real timestamps. | 24 h time-lapse recording opens in < 5 s. |
| PLAY-02 | M | Change palette and temperature range during playback. | Applies instantly to the current frame. |
| PLAY-03 | S | Adjustable playback speed (incl. fast-forward of time-lapse). | — |
| PLAY-04 | S | Playback supports VIEW-04 (spot/min/max). | — |
| PLAY-05 | C | Temperature-over-time chart for a point or area. | Export as CSV. |
| PLAY-06 | C | Export as video (MP4) and image sequence. | — |

---

## 7. Raspberry Pi recorder and web interface (PI, WEB)

| ID | Prio | Requirement | Acceptance criterion |
|---|---|---|---|
| PI-01 | M | Runs headless on Raspberry Pi OS 64-bit. Reference hardware: **Raspberry Pi 3B+**. | 24 h recording at 1 fps without crash or memory growth. |
| PI-02 | M | Runs as a systemd service; optional auto-start of a recording on boot. | Survives reboot; configuration in a file. |
| PI-03 | S | Full 25 fps recording on the reference Pi. | Drop rate measured and documented (feasibility, not guaranteed). |
| PI-04 | S | Runs on Raspberry Pi Zero 2 W. | Feasibility test (dwc2 isochronous, 512 MB RAM); result documented. |
| PI-05 | S | Records to SD card or USB storage (configurable path). | — |
| WEB-01 | M | Web UI: start/stop recording, set rate. | Works from a phone browser in the local network. |
| WEB-02 | M | Live preview image in the web UI (reduced rate). | ≥ 2 fps preview while recording, recording not affected. |
| WEB-03 | M | Status: recording time, frame count, drop count, free space, camera state. | — |
| WEB-04 | S | List and download recordings. | — |
| WEB-05 | C | Basic authentication. | — |

---

## 8. ML pipeline (ML)

| ID | Prio | Requirement | Acceptance criterion |
|---|---|---|---|
| ML-01 | M | Export frames from recordings into a dataset (raw temperatures + normalised images). | Reproducible from recording + config. |
| ML-02 | M | Fine-tune a semantic segmentation model in Python on labelled thermal frames. | Training script, metrics (e.g. mIoU) on a held-out split. |
| ML-03 | M | Export the trained model to ONNX. | Output identical (within tolerance) to PyTorch. |
| ML-04 | S | Run segmentation offline on recordings in the C# viewer (ONNX Runtime). | Overlay on playback. |
| ML-05 | M | Labelling workflow per §8.1. | A labelled dataset of ≥ 200 frames exists. |
| ML-06 | C | Use raw temperatures as model input (not only 8-bit images). | Compared against 8-bit baseline. |
| ML-07 | W | Live segmentation on the Pi. | Out of scope. |

Class list: **open** — to be defined once real recordings exist.

### 8.1 Manual labelling (LAB)

Decision (draft): use an existing, self-hosted labelling tool (CVAT or Label Studio) and build only the data bridge.

| ID | Prio | Requirement | Acceptance criterion |
|---|---|---|---|
| LAB-01 | M | Self-hosted labelling tool supporting semantic segmentation (polygon and brush masks). | Runs via Docker in the homelab; tool choice documented (CVAT vs Label Studio). |
| LAB-02 | M | Export selected frames as images for labelling with a **fixed, documented temperature range and palette** per export. | Same scene yields identical images across exports; range stored in export manifest. |
| LAB-03 | M | Every exported image is traceable to its source: recording file, frame index, timestamp. | Manifest (e.g. JSON/CSV) in the export folder. |
| LAB-04 | M | Import label masks back and join them with the raw temperature frames. | Python loader yields (raw temperatures, mask) pairs. |
| LAB-05 | M | Versioned class list (names, IDs, colours) shared by tool, dataset and model. | Single source file in the repo. |
| LAB-06 | S | Frame selection helpers: every n-th frame, skip near-duplicates (important for time-lapse). | Export of a 24 h recording yields a manageable, diverse set. |
| LAB-07 | S | Model-assisted pre-labelling (e.g. Segment Anything in the tool, later own model) to reduce manual effort. | Pre-labels are editable, not accepted blindly. |
| LAB-08 | S | Train/validation/test split by **recording**, not by frame (avoid leakage between near-identical frames). | Split script with fixed seed. |
| LAB-09 | C | Mask review/correction inside the C# viewer on raw data with adjustable range. | — |
| LAB-10 | W | Full labelling tool in the C# viewer. | Out of scope. |

---

## 9. Non-functional and quality (NFR, QA)

| ID | Prio | Requirement |
|---|---|---|
| NFR-01 | M | Platforms: Linux x64 (reference: CachyOS), Windows x64, Linux ARM64 (recorder). |
| NFR-02 | M | Target framework .NET 8 (LTS). |
| NFR-03 | M | No root/admin rights at runtime on Linux (udev rule shipped). |
| NFR-04 | S | Recorder CPU load < 50 % on reference Pi at 1 fps incl. web preview. |
| NFR-05 | S | Windows: documented WinUSB driver installation. |
| QA-01 | M | Core logic (frame assembly, decoding, file format) covered by unit tests using recorded fixtures. |
| QA-02 | M | CI on GitHub Actions: build + test for Linux and Windows. |
| QA-03 | M | Pinned package versions. |
| QA-04 | S | Replay of recordings as a camera substitute (development and tests without hardware). |
| QA-05 | S | README with screenshots, architecture overview, setup per platform. |
| QA-06 | C | Release binaries (Linux x64/ARM64, Windows x64). |

---

## 10. Milestones

| Milestone | Content | Exit criterion |
|---|---|---|
| **M0 — POC** | Direct USB streaming, first live image | CAM-01–03, VIEW-01; raw frame fixture in repo |
| **M1 — Recording** | File format + desktop recording | FMT-01–04, REC-01–02, QA-01–03 |
| **M2 — Playback** | Viewer plays recordings | PLAY-01–02, QA-04 |
| **M3 — Windows** | Viewer on Windows | NFR-01 (Windows), NFR-05, CI green on both |
| **M4 — Pi recorder** | Headless recorder on 3B+ | PI-01–02 |
| **M5 — Web UI** | Control and preview from the browser | WEB-01–03 |
| **M6 — Labelling** | Tool set up, export/import bridge, first labelled set | LAB-01–05, ML-01, ML-05 |
| **M7 — ML** | Fine-tuning, ONNX, offline inference | ML-02–04, LAB-08 |

**"C# on the CV"** after M3: a cross-platform desktop app with native interop, tests and CI.

---

## 11. Risks and open points

| # | Risk / open point | Mitigation |
|---|---|---|
| R-1 | Isochronous transfers on Pi USB (dwc2, shared hub on 3B+) unstable at 25 fps. | Early spike on 3B+; time-lapse recording needs far less bandwidth per second but still full-rate streaming from the camera — measure. |
| R-2 | Windows requires replacing the UVC driver with WinUSB → camera unusable for other apps. | Document; evaluate libusbK / driver switching; decide before M3. |
| R-3 | Raspberry Pi has no RTC → wrong timestamps without network. | Require NTP for recordings, or store monotonic time + warning in header. |
| R-4 | SD card wear and corruption during multi-day recordings. | FMT-06, REC-03, recommend USB storage. |
| R-5 | Temperature accuracy (emissivity, gain mode, camera drift). | Document as "indicative"; emissivity correction as later Could. |
| O-1 | Segmentation classes; CVAT vs Label Studio. | Decide after first recordings (before M6). |
| R-6 | Labelling effort too high → dataset too small for fine-tuning. | LAB-06/07, start with few classes, small pretrained model. |
| O-2 | Measurement features in the live view — scope still open. | Revisit after M2. |

## 12. Out of scope (for now)

Battery operation, live segmentation on the Pi, cloud upload, mobile app, other camera vendors.
