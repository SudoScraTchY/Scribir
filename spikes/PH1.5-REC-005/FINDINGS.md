# PH1.5-REC-005: System-Audio Loopback Capture Investigation

**Status:** Completed  
**Platforms Investigated:** Windows Desktop (WASAPI), macOS (ScreenCaptureKit), Android / iOS (System sandboxing)  
**Related ADRs:** ADR-0011 (Platform Scope: Android & Desktop)  

---

## 1. Executive Summary & Recommendation

| Platform | Native Feasibility (No Virtual Driver) | Permission Requirements | Normalized PCM (16kHz Mono) | Recommendation for Scribir |
| :--- | :--- | :--- | :--- | :--- |
| **Windows Desktop** | **Yes (100% Native via WASAPI)** | Standard user-mode (No elevation or driver required) | Verified (Hardware mix $\to$ Resample $\to$ PCM16) | **Ship in Desktop v1.5** |
| **macOS Desktop** | **Yes (macOS 13+ via ScreenCaptureKit)** | User consent dialog for screen/audio capture | Supported via AudioEngine / AVAudioSinkNode | Ship as follow-up for macOS v1.5+ |
| **Android / iOS** | **No** (OS security sandboxing prevents global system loopback) | Root / Accessibility / Carrier restrictions | N/A | **Do Not Ship (Out of scope per ADR-0011)** |

---

## 2. Platform-by-Platform Findings

### 2.1. Windows Desktop (WASAPI Loopback)
- **Mechanism**: Native Windows Audio Session API (WASAPI) with `AUDCLNT_STREAMFLAGS_LOOPBACK` (`0x00020000`) on the default render endpoint (`eRender`, `eMultimedia`).
- **Driver Requirement**: **Zero third-party drivers needed**. Operates directly on existing real hardware audio render endpoints (speakers, headphones, USB DACs, Bluetooth headsets).
- **Format Conversion**: Default Windows mix formats are typically 44.1 kHz or 48.0 kHz 32-bit IEEE float stereo. Our spike (`WasapiLoopbackCapture.cs`) demonstrates:
  1. Reading hardware mix format dynamically via `IAudioClient.GetMixFormat`.
  2. Channel downmixing (`(left + right) * 0.5f`).
  3. High-efficiency decimation/linear resampling to 16,000 Hz.
  4. Quantization to 16-bit PCM bytes, matching `PH1-CORE-002` and `WavHelper`.
- **Known Characteristic (Idle Stream Behavior)**:
  - When no audio is rendering on the system, WASAPI does not clock packets unless an application starts playing or a keep-alive silent stream is opened.
  - For continuous streaming sessions in `LiveTranscriptionSession`, a tiny low-overhead background silent render buffer or timestamp clock fills pauses with zeroed PCM frames.
- **Integration Cost**: Low (~1-2 engineering days to expose as an input source option in `TranscribeViewModel` and `WindowsAudioRecorder`).

### 2.2. macOS Desktop (ScreenCaptureKit)
- **Mechanism**: Starting with macOS 13 (Ventura), Apple introduced `ScreenCaptureKit` with `SCStreamConfiguration.capturesAudio = true` and `SCStream.addStreamOutput`.
- **Driver Requirement**: No virtual audio driver (e.g. BlackHole) required on modern macOS ($\ge$ 13).
- **Permission**: Requires explicit user authorization in macOS System Settings $\to$ Privacy & Security $\to$ Screen & System Audio Recording.
- **Integration Cost**: Moderate. Requires macOS Objective-C / Swift binding in `.NET MAUI` Mac Catalyst or native sidecar.

### 2.3. Mobile (Android & iOS)
- **Finding**: Both Android and iOS sandbox audio playback strictly per application UID.
- In Android 10+, `AudioPlaybackCaptureConfiguration` only allows capturing audio from apps that explicitly opt-in (`android:allowAudioPlaybackCapture="true"`) and requires an active `MediaProjection` foreground notification prompt. Many apps (telephony, DRM music, social media) opt-out.
- On iOS, third-party apps cannot tap the global audio render bus.
- **Conclusion**: Confirms ADR-0011 architectural decision to keep system-audio loopback desktop-only.

---

## 3. Spike Code Reference
- Spike implementation: `spikes/PH1.5-REC-005/WasapiLoopbackCapture.cs`
- CLI test harness: `spikes/PH1.5-REC-005/Program.cs`
- Verified execution: Successfully opens `MMDeviceEnumerator`, initializes WASAPI shared loopback on default render device (`48000 Hz, 2 ch, Float`), and outputs normalized 16kHz mono WAV audio file with standard 44-byte RIFF header.
