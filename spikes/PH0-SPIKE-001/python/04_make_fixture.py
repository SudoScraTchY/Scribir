"""PH0-SPIKE-001: build a synthetic 2-speaker test fixture with known ground truth.

Downloads two single-speaker clips from hf-internal-testing/dummy-audio-samples and
interleaves them with short silence gaps. Ground truth is exact by construction.
"""
import os, io, json
import numpy as np
import soundfile as sf
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
AUD = os.path.join(ROOT, "artifacts", "audio")
SRC = os.path.join(AUD, "src")
os.makedirs(SRC, exist_ok=True)

def log(*a):
    print(*a, flush=True)

API = "https://huggingface.co/api/datasets/hf-internal-testing/dummy-audio-samples/tree/main"
def list_files():
    with urllib.request.urlopen(API, timeout=60) as r:
        return json.load(r)

files = [f["path"] for f in list_files() if f["path"].lower().endswith((".wav", ".flac", ".mp3"))]
log("available:", files)

# pick two different-speaker files, prefer en-*
en = [f for f in files if os.path.basename(f).startswith("en-")]
cand = en or files
pick = []
seen = set()
for f in cand:
    name = os.path.basename(f).lower()
    who = name.split("_")[-1].split(".")[0] if "_" in name else name
    if "woman" in name: who = "woman"
    if "man" in name: who = "man"
    if who not in seen:
        seen.add(who)
        pick.append(f)
    if len(pick) == 2:
        break
if len(pick) < 2:
    pick = cand[:2]
log("picked:", pick)

paths = []
for f in pick:
    local = os.path.join(SRC, os.path.basename(f))
    if not os.path.exists(local):
        url = f"https://huggingface.co/datasets/hf-internal-testing/dummy-audio-samples/resolve/main/{f}"
        log("downloading", url)
        urllib.request.urlretrieve(url, local)
    paths.append(local)

def read16k(path):
    data, sr = sf.read(path, dtype="float32", always_2d=True)
    data = data.mean(axis=1).astype(np.float32)
    if sr != 16000:
        t = np.arange(len(data), dtype=np.float64) / sr
        tn = np.arange(0, len(data) / sr, 1.0 / 16000)
        data = np.interp(tn, t, data).astype(np.float32)
    return data

srcs = [read16k(p) for p in paths]
for p, s in zip(paths, srcs):
    log(f"source {os.path.basename(p)}: {len(s)/16000:.2f}s")

def take_loop(src, need):
    if len(src) >= need:
        return src[:need]
    reps = int(np.ceil(need / len(src)))
    return np.tile(src, reps)[:need]

TURN = 6.0
GAP = 0.5
TURNS = 8   # alternate A/B x4
pieces = []
truth = []
t = 0.0
for k in range(TURNS):
    who = "A" if k % 2 == 0 else "B"
    src = srcs[0] if who == "A" else srcs[1]
    dur = TURN
    seg = take_loop(src, int(dur * 16000))
    pieces.append(seg)
    truth.append(f"{t:.2f}-{t+dur:.2f} {who}")
    t += dur
    if k != TURNS - 1:
        pieces.append(np.zeros(int(GAP * 16000), dtype=np.float32))
        t += GAP

out = np.concatenate(pieces)
out_path = os.path.join(AUD, "two_speaker_fixture.wav")
sf.write(out_path, out, 16000, subtype="PCM_16")
gt_path = os.path.join(AUD, "two_speaker_fixture.groundtruth.txt")
with open(gt_path, "w", encoding="utf-8") as fh:
    fh.write("\n".join(truth) + "\n")
log("wrote:", out_path, f"({len(out)/16000:.2f}s)")
log("wrote:", gt_path)
for line in truth:
    log("  ", line)
log("FIXTURE DONE")
