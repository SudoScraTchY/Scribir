# Phase 2.5 — Platform Depth & Self-Hosted Sync Tasks

All tasks in this file implement Phase 2.5+ capabilities outlined in the Product Specification SDD (`01-product-specification.md` §7). They extend Scribir's native operating system integration and decentralized sync while strictly respecting ADR-0007 (Local-first, zero-cloud architecture).

---

### PH2.5-PLAT-001 — Cross-platform share-sheet & media launch integration

**Status:** Done  
**Completed:** 2026-10-28  
**Depends On:** PH1-UI-002  
**Related ADR(s):** ADR-0001, ADR-0011  

**Goal:** Allow users to share audio files into Scribir directly from external system apps (Voice Recorder, WhatsApp, Files, Voice Memos) via Android Share Sheet and Windows file association / protocol activation ("Transcribe with Scriber").

**Scope:** `Scriber.App` (`Platforms/Android/MainActivity.cs`, `Platforms/Android/AndroidManifest.xml`, `Platforms/Windows/Package.appxmanifest`, `App.xaml.cs`, `Services/IMediaLaunchHandler.cs`).

**Interface / Contract:**
- Android `AndroidManifest.xml`: Add `<intent-filter>` for `android.intent.action.SEND` and `android.intent.action.VIEW` for MIME types `audio/*`, `video/*`.
- Windows `Package.appxmanifest`: Add `windows.fileTypeAssociation` for `.wav`, `.mp3`, `.m4a`, `.aac`, `.flac`, `.ogg` and protocol `scriber:`.
- Application service contract:
  ```csharp
  namespace Scriber.App.Services;

  public interface IMediaLaunchHandler
  {
      Task<bool> CanHandleAsync(Uri uri);
      Task HandleIncomingMediaAsync(Uri uri, CancellationToken ct = default);
  }
  ```

**Acceptance Criteria:**
- On Android, selecting "Share" on any audio file displays "Scriber". Tapping it opens Scribir, resolves content URIs via `ContentResolver`, copies the audio stream to a safe app-cache path, and navigates directly to `ImportPage`.
- On Windows, right-clicking an audio file -> "Open with" -> "Scriber" launches the application and loads the file in `ImportPage`.
- Protocol activation via `scriber://transcribe?path=...` properly parses parameters and navigates to the transcribe workflow.
- Handles runtime storage/media permissions (`READ_MEDIA_AUDIO` on Android 13+) gracefully without crashing.

**Out of Scope:** Silent background transcription without user review; live phone call recording.

**Suggested Verification:** Manual share test from Android Voice Recorder and Files app on connected Redmi Note 11 (`5a8c869e`); Windows command line launch `Scriber.App.exe "test.wav"`.

**Completion note (2026-10-28):** Done. Created `IMediaLaunchHandler` and `MediaLaunchHandler` resolving `file://`, `content://` (via Android ContentResolver cache copy), and `scriber://transcribe?path=...` protocol URIs. Registered intent-filters on `MainActivity` for `android.intent.action.SEND` and `android.intent.action.VIEW` across audio MIME types and protocol `scriber:`. Added `READ_MEDIA_AUDIO`, `READ_MEDIA_VIDEO`, and `READ_EXTERNAL_STORAGE` to AndroidManifest.xml. Added `windows.fileTypeAssociation` and `windows.protocol` extensions to Windows `Package.appxmanifest`. Connected command-line argument processing and `OnAppLinkRequestReceived` in `App.xaml.cs` to auto-switch `TranscribeViewModel` into Import mode with staged files. Verified clean builds on both Windows and Android.

---

### PH2.5-SYNC-001 — Decentralized self-hosted transcript synchronization

**Status:** Done  
**Completed:** 2026-10-28  
**Depends On:** PH1-STORE-001, PH1-STORE-003  
**Related ADR(s):** ADR-0007, ADR-0009  

**Goal:** Deliver zero-cloud synchronization of transcripts, speaker voiceprints, and library metadata across user devices via user-owned storage (WebDAV, Nextcloud, Syncthing local folder, or NAS share).

**Scope:** `Scriber.Storage` (`Sync/ISyncService.cs`, `Sync/WebDavSyncProvider.cs`, `Sync/LocalFolderSyncProvider.cs`, `Sync/SyncManifest.cs`), `Scriber.App` (`Views/SettingsPage.xaml`).

**Interface / Contract:**
```csharp
namespace Scriber.Storage.Sync;

public enum SyncProviderType
{
    LocalDirectory,
    WebDav
}

public sealed record SyncOptions(
    SyncProviderType ProviderType,
    string TargetUriOrPath,
    string? Username,
    string? PasswordOrToken,
    bool EncryptRemotePayloads,
    string? EncryptionPassphrase);

public sealed record SyncProgress(
    string CurrentStep,
    int ItemsSynced,
    int TotalItems);

public sealed record SyncResult(
    bool Success,
    int UploadedCount,
    int DownloadedCount,
    int ConflictCount,
    string? ErrorMessage);

public interface ISyncService
{
    Task<SyncResult> SyncAsync(SyncOptions options, IProgress<SyncProgress>? progress = null, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(SyncOptions options, CancellationToken ct = default);
}
```

**Acceptance Criteria:**
- Implements `ISyncService` with `LocalFolderSyncProvider` (for Syncthing / Nextcloud local mount) and `WebDavSyncProvider` (standard WebDAV over HTTP/HTTPS).
- Syncs versioned encrypted JSON bundles containing transcripts and voiceprint vectors, tracking revisions with a lightweight `SyncManifest`.
- Conflict resolution: Last-write-wins by timestamp; concurrent edits generate `<Title> (Conflicted Copy <Date>)` without data loss.
- Zero-cloud guarantee: no telemetry, no centralized accounts, no project servers per ADR-0007.
- Fully offline resilient: queues local changes and syncs when target directory / endpoint is available.

**Out of Scope:** Proprietary cloud APIs (Google Drive, Dropbox, iCloud); real-time multi-user live collaborative editing.

**Suggested Verification:** Integration test syncing between two isolated SQLite databases using a local directory mock; manual test with a local WebDAV server (e.g. Nextcloud Docker container).

**Completion note (2026-10-28):** Done. Built `SyncService`, `LocalFolderSyncProvider`, `WebDavSyncProvider`, `SyncCrypto` (AES-256 with HMAC-SHA256 authenticated encryption), and `SyncManifest`. Handles bidirectional reconciliation of transcripts and voiceprints with timestamp ordering and conflicted copy branching (`<Title> (Conflicted Copy <Date>)`). Integrated into `SettingsPage.xaml` and `SettingsViewModel.cs` with provider picker, encryption passphrase, connection test, and immediate sync. Fully verified by unit test suite in `SyncTests.cs` (29 passing tests in `Scriber.Storage.Tests`).
