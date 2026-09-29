# PH0-SPIKE-002 — end-to-end desktop proof (diarize -> ASR -> naive align)

> Spike only — no product code. **Findings: `../../Agent-docs/findings/PH0-SPIKE-002.md`.**

## What this is

A C# console (`SpeakerTranscript`) proving the full pipeline shape on desktop:

1. **Diarization** — raw ONNX Runtime (the runtime path PH0-SPIKE-001 recommended), with a
   natively ported mel frontend + feature-stacking embedder + AOSC speaker-cache + segment
   extraction (`DiarizerPort`), validated against the PH0-SPIKE-001 Python reference.
2. **ASR** — sherpa-onnx with Whisper-small int8 (multilingual) — `small-encoder.int8.onnx`,
   `small-decoder.int8.onnx`, `small-tokens.txt`.
3. **Naive alignment** — per-diarization-turn transcription (stock Whisper offline decode is
   limited to <30 s windows; turns >28 s are split), or token-timestamp alignment when an
   attention-enabled decoder is used. Prints `[Speaker N] [mm:ss.mmm - mm:ss.mmm] text`.

## Layout

- `csharp/DiarizerPort/` — validated diarizer port; `--check` runs all reference gates
  (`runs/check/check_report.txt`); `--audio <wav>` prints/saves segments.
- `csharp/SpeakerTranscript/` — the end-to-end console; transcripts under `runs/<audio>/`.
- `artifacts/` — **git-ignored**: test audio (including k2-fsa two-speaker clips + the synthetic
  fixture from PH0-SPIKE-001), `whisper/sherpa-onnx-whisper-small/` (stock model),
  `whisper-export/` (attention-enabled export experiment).

## Reproduce

```powershell
$env:DOTNET_ROOT='C:\Program Files\dotnet'      # machine quirk: default DOTNET_ROOT lacks .NET 9
cd M:\Agentic\Projects\Scribir\spikes\PH0-SPIKE-002\csharp\DiarizerPort
'C:\Program Files\dotnet\dotnet.exe' build -c Release
.\bin\Release\net9.0\win-x64\DiarizerPort.exe --check          # expect: ALL CHECKS PASSED

cd ..\SpeakerTranscript
'C:\Program Files\dotnet\dotnet.exe' build -c Release
.\bin\Release\net9.0\win-x64\SpeakerTranscript.exe --audio ..\..\artifacts\audio\1-two-speakers-en.wav
# optional: --whisper-dir <dir> to switch Whisper models
```

Notes:
- `SpeakerTranscript` pins ONNX Runtime 1.30.0 (from Microsoft.ML.OnnxRuntime) over the version
  sherpa bundles (1.28.2) via an MSBuild target; `--smoke` verifies both native stacks in one process.
- Diarizer reference gates compare against `spikes/PH0-SPIKE-001/artifacts/` fixtures; float noise
  vs torch's float32 is ~1.5e-4 (see findings §).
