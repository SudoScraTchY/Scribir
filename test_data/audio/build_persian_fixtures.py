"""Builds 3 real multi-speaker Persian conversational audio test fixtures with exact ground truth.
Speaker 1 (Male): local recording ('من به سکوت تو بیشتر از احترامت احتیاج دارم')
Speaker 2 (Female): Afsham23 ('آماده', 'آفتاب', 'آب و هوا')
"""
import os
import soundfile as sf
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
persian_1_path = os.path.join(HERE, "persian_sample_1.wav")

def log(*args):
    print(*args, flush=True)

# Pre-downloaded local clips
afsham_clips = {
    "weather": (os.path.join(HERE, "afsham_weather.wav"), "آب و هوا"),
    "sunshine": (os.path.join(HERE, "afsham_sunshine.wav"), "آفتاب"),
    "ready": (os.path.join(HERE, "afsham_ready.wav"), "آماده"),
}

dl_cache = {}
for key, (path, text) in afsham_clips.items():
    data, sr = sf.read(path, dtype="float32", always_2d=True)
    mono = data.mean(axis=1)
    if sr != 16000:
        t = np.arange(len(mono), dtype=np.float64) / sr
        tn = np.arange(0, len(mono) / sr, 1.0 / 16000)
        mono = np.interp(tn, t, mono).astype(np.float32)
    dl_cache[key] = (mono, text)

# Load Speaker 1 (Male)
s1_data, s1_sr = sf.read(persian_1_path, dtype="float32", always_2d=True)
s1_mono = s1_data.mean(axis=1)
if s1_sr != 16000:
    t = np.arange(len(s1_mono), dtype=np.float64) / s1_sr
    tn = np.arange(0, len(s1_mono) / s1_sr, 1.0 / 16000)
    s1_mono = np.interp(tn, t, s1_mono).astype(np.float32)
s1_text = "من به سکوت تو بیشتر از احترامت احتیاج دارم"

log(f"Speaker 1 duration: {len(s1_mono)/16000:.2f}s")
for k, (m, txt) in dl_cache.items():
    log(f"Speaker 2 ({k}) duration: {len(m)/16000:.2f}s")

GAP = 0.6  # 600ms pause between turns

def build_fixture(filename, turns):
    pieces = []
    truth = []
    t = 0.0
    for idx, (spk_name, audio, text) in enumerate(turns):
        dur = len(audio) / 16000.0
        pieces.append(audio)
        truth.append(f"{t:.2f}-{t+dur:.2f} [{spk_name}] {text}")
        t += dur
        if idx != len(turns) - 1:
            pieces.append(np.zeros(int(GAP * 16000), dtype=np.float32))
            t += GAP

    out = np.concatenate(pieces)
    out_wav = os.path.join(HERE, f"{filename}.wav")
    out_gt = os.path.join(HERE, f"{filename}.groundtruth.txt")
    sf.write(out_wav, out, 16000, subtype="PCM_16")
    with open(out_gt, "w", encoding="utf-8") as fh:
        fh.write("\n".join(truth) + "\n")
    log(f"Wrote {out_wav} ({len(out)/16000:.2f}s) and {out_gt}")

# Fixture 1: 2 speakers alternating (Male / Female x 2)
turns_1 = [
    ("Speaker 1", s1_mono, s1_text),
    ("Speaker 2", dl_cache["weather"][0], dl_cache["weather"][1]),
    ("Speaker 1", s1_mono, s1_text),
    ("Speaker 2", dl_cache["sunshine"][0], dl_cache["sunshine"][1]),
]
build_fixture("persian_dialogue_fixture_1", turns_1)

# Fixture 2: 2 speakers inquiry and answer
turns_2 = [
    ("Speaker 2", dl_cache["ready"][0], dl_cache["ready"][1]),
    ("Speaker 1", s1_mono, s1_text),
    ("Speaker 2", dl_cache["weather"][0], dl_cache["weather"][1]),
]
build_fixture("persian_dialogue_fixture_2", turns_2)

# Fixture 3: 2 speakers 5-turn discussion
turns_3 = [
    ("Speaker 2", dl_cache["ready"][0], dl_cache["ready"][1]),
    ("Speaker 1", s1_mono, s1_text),
    ("Speaker 2", dl_cache["sunshine"][0], dl_cache["sunshine"][1]),
    ("Speaker 1", s1_mono, s1_text),
    ("Speaker 2", dl_cache["weather"][0], dl_cache["weather"][1]),
]
build_fixture("persian_dialogue_fixture_3", turns_3)

log("ALL 3 PERSIAN FIXTURES BUILT SUCCESSFULLY!")
