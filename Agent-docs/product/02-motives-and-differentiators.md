# Motives, Competitive Landscape, and Differentiators

## 1. Why this, why now

- **Privacy is the actual product.** Commercial meeting-transcription
  tools (Otter, Fireflies, Fathom, Read.ai) all ship audio to a server.
  For legal, medical, journalistic, HR, or simply personal conversations,
  that's a non-starter for many people — and for entire regions where
  sending audio abroad carries real legal or safety implications. Offline
  by architecture beats offline as a policy promise.
- **The hardware bar dropped enough to make this real.** A 100M-parameter
  diarizer and a compact quantized ASR model together fit comfortably on
  a modern phone's CPU, no GPU required (see ADR-0004, ADR-0006). This
  was not a realistic claim two years ago.
- **No incentive to enshittify.** No subscription and no cloud cost to
  recoup means no product pressure to gate the transcript behind a
  paywall or mine conversations. Being open-source removes the "trust
  us" problem entirely.
- **Persian and other underserved languages are a wedge, not an
  afterthought.** Commercial tools prioritize English/Mandarin/Spanish. A
  community-contributable model manifest (ADR-0009) lets Scriber support
  any language a community member is willing to vet a model for,
  including ones no commercial vendor will ever prioritize.

## 2. Competitive landscape (as researched during planning)

Existing open-source offline transcribers already cover a fair amount of
ground. Worth knowing precisely what's already table-stakes so effort
goes toward genuine gaps, not re-implementing what exists:

| Tool | Already has |
|---|---|
| **Vibe** (MIT, macOS/Windows/Linux, mobile "coming soon") | Offline Whisper transcription, speaker diarization, summarization via Claude API or Ollama, batch processing, system-audio + mic capture, YouTube URL ingestion, custom model URLs, TXT/SRT/VTT/HTML/PDF/JSON export, multi-model support (Whisper, Parakeet, Nemotron) |
| **Buzz** (cross-platform desktop) | Offline Whisper, live real-time transcription with a presentation overlay window, speaker diarization, translation |
| **WhisperX** (BSD-2-Clause) | Word-level timestamps via forced alignment, diarization, ~70× realtime batched inference — the strongest pure transcription engine, but files-only, no app shell |
| **Meetily** | Local Whisper or NVIDIA Parakeet transcription, cross-platform, bot-free |
| **Humla** (macOS-only) | Fully local pipeline: Whisper on Metal GPU, offline diarization on the Neural Engine, summaries via Ollama |

**Conclusion:** diarization, offline Whisper transcription, basic
summarization, and multi-format export are now baseline expectations, not
differentiators. Scriber's actual product bets need to sit above this
line.

## 3. What's genuinely missing from this space (the product bets)

1. **Cross-session speaker identity, not just per-file diarization.**
   Every tool above re-learns "Speaker 1/2/3" from scratch on every file.
   None do a one-time voice enrollment (a calibration sentence per
   participant, or harvesting a clean segment from their first turn) that
   lets a *recurring* meeting, class, or client relationship recognize
   the same people automatically across sessions. This is the single
   largest, mostly-unbuilt feature gap in this space. See ADR-0010.

2. **Confidence-aware review, not a flat transcript.** None of the
   surveyed tools surface *where the model was unsure*. Word/segment
   confidence (most ASR backends already emit this) rendered as a subtle
   visual heat-map means a human reviewer's eye goes straight to the
   portion of the transcript that actually needs checking — directly
   useful for legal/medical use, and cheap to build.

3. **A real, measured device-aware onboarding**, not a static model list.
   A short on-device benchmark at first run — actually timing 2-3
   candidate model configs on the user's real hardware — replaces asking
   users to self-report "is my phone modern or low-end." No surveyed tool
   does this; they all present a static list and let the user guess.

4. **Diff-aware re-transcription.** If a user upgrades a model or
   hand-edits a transcript, none of these tools show what changed on a
   re-run, or protect prior manual corrections from being silently
   overwritten. A git-diff-style comparison view is unbuilt in this
   space and directly serves anyone iterating on model quality.

5. **Structured, agent-and-PKM-friendly Markdown export**, not just
   prose. Speaker-attributed, timestamped Markdown with YAML frontmatter
   (participants, date, duration, language) — built specifically to drop
   into Obsidian/Logseq or be queried by an agent via MCP (ADR-0008),
   not just "here is the text."

6. **Unified in-device audio capture, done honestly.** Vibe already pulls
   audio *from* a YouTube URL. None of the surveyed tools transcribe
   arbitrary system audio live, regardless of source (a browser call, a
   podcast app, a video actually playing) as one unified "listen to
   whatever this device is outputting" mode. This is real platform work
   (see `tasks/phase1.5-recording-streaming.md`) but would be a genuine,
   unmatched capability if done well.

7. **Local PII/redaction as a first-class feature**, not an afterthought.
   Given the privacy-first positioning and legal/medical use cases, a
   local pass that flags or redacts names, emails, phone numbers, or
   user-defined sensitive terms before export/share extends the "your
   data never has to leave, and stays in your control even once
   exported" story. No surveyed tool markets this.

8. **A shared, versioned, community-contributable model manifest
   format**, not a hardcoded list the maintainer alone curates forever.
   This is the actual mechanism by which the "Persian and beyond" language
   goal scales without becoming a permanent personal maintenance burden.
   See ADR-0009.

## 4. What this deliberately does NOT chase

Per the product non-goals: raw accuracy parity with cloud SOTA, real-time
cloud meeting-bot integrations, and any LLM-summarization feature framed
as the core value rather than an optional add-on. The bet is "very good,
free, private, and it recognizes people across sessions and tells you
what it's unsure about" — not "beats Otter.ai on a leaderboard."
