# PH0-SPIKE-001 — scratch area (sherpa-onnx vs. a custom Nemotron 3 Diarization ONNX export)

> Spike only — no product code. **Findings: `../../Agent-docs/findings/PH0-SPIKE-001.md`.**

## Layout

- `python/` — export + validation harness (uv-managed venv).
  - `01_export_step_onnx.py` — export the streaming step graph (v1) + numerical checks.
  - `02_run_streaming_ort.py` — full streaming run (torch vs ONNX, per-step diffs, segments).
  - `03_run_offline_ort.py` — offline-configuration run through the same step graph.
  - `04_make_fixture.py` — synthetic 2-speaker fixture with exact ground truth.
  - `05_export_csharp_fixtures.py` — per-step tensors for the C# replay.
  - `07_export_step_onnx_masked.py` — mask-aware step graph (v2) + offline validation.
  - `08_dump_port_assets.py` — mel filters / weights / reference vectors for a future C# port.
  - `logs/` — raw evidence (committed).
- `csharp/SherpaAttempt/` — sherpa-onnx C# diarization API probe.
  - `SherpaAttempt.exe control <wav> <sherpa-models> <onnx>` — inventory + native control run.
  - `SherpaAttempt.exe exp <wav> <sherpa-models> <onnx>` — the incompatible-model experiment.
- `csharp/OrtRunner/` — replays the per-step fixtures through `Microsoft.ML.OnnxRuntime` and compares
  logits + recomputes segments in C#.
- `csharp/logs/` — raw evidence (committed).
- `artifacts/` — **git-ignored**: downloaded model files, exported ONNX graphs, fixtures, sherpa models,
  port assets. Regenerate with the scripts above (see findings §6 for sizes/URLs).

## Environment quirks (this machine)

- Set `PYTHONUTF8=1` for Python runs (console codec is cp1252; torch's progress prints crash otherwise).
- Use `C:\Program Files\dotnet\dotnet.exe` and `DOTNET_ROOT=C:\Program Files\dotnet` — the default
  `DOTNET_ROOT` here points at a bundled runtime without .NET 9.
- Keep caches/venvs on `M:` (`UV_CACHE_DIR`, `UV_PYTHON_INSTALL_DIR`, `PIP_CACHE_DIR`, `HF_HOME` under
  `M:\Agentic\Projects\Scribir\.cache`); `C:` has limited free space.
