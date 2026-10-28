namespace Scriber.Storage.Sync;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Scriber.Core.Audio;
using Scriber.Core.Domain;

/// <summary>
/// Orchestrates zero-cloud, end-to-end encrypted transcript and voiceprint sync (PH2.5-SYNC-001, ADR-0007).
/// Supports local directories (Syncthing/NAS) and WebDAV endpoints (Nextcloud) with last-write-wins and conflicted copies.
/// </summary>
public sealed class SyncService : ISyncService
{
    private readonly ITranscriptRepository _transcriptRepo;
    private readonly IVoiceprintRepository? _voiceprintRepo;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public SyncService(ITranscriptRepository transcriptRepo, IVoiceprintRepository? voiceprintRepo = null)
    {
        _transcriptRepo = transcriptRepo ?? throw new ArgumentNullException(nameof(transcriptRepo));
        _voiceprintRepo = voiceprintRepo;
    }

    public async Task<bool> TestConnectionAsync(SyncOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var provider = CreateProvider(options);
        return await provider.TestConnectionAsync(ct);
    }

    public async Task<SyncResult> SyncAsync(SyncOptions options, IProgress<SyncProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.EncryptRemotePayloads && string.IsNullOrWhiteSpace(options.EncryptionPassphrase))
        {
            return new SyncResult(false, 0, 0, 0, "Encryption is enabled but no passphrase was provided.");
        }

        using var provider = CreateProvider(options);

        progress?.Report(new SyncProgress("Connecting to sync endpoint...", 0, 0));
        bool connected = await provider.TestConnectionAsync(ct);
        if (!connected)
        {
            return new SyncResult(false, 0, 0, 0, $"Unable to connect to sync endpoint: {options.TargetUriOrPath}");
        }

        int uploaded = 0;
        int downloaded = 0;
        int conflicts = 0;

        try
        {
            progress?.Report(new SyncProgress("Fetching remote manifest...", 0, 0));
            var remoteManifest = await provider.GetRemoteManifestAsync(ct) ?? new SyncManifest();

            // 1. Fetch all local entities
            var localSummaries = await _transcriptRepo.ListAsync(ct);
            var localVoiceprints = _voiceprintRepo != null ? await _voiceprintRepo.GetAllAsync(ct) : [];

            int totalOps = localSummaries.Count + localVoiceprints.Count + remoteManifest.Items.Count;
            int currentStep = 0;

            // 2. Upload phase: Transcripts
            progress?.Report(new SyncProgress("Syncing local transcripts to remote...", currentStep, totalOps));
            foreach (var summary in localSummaries)
            {
                ct.ThrowIfCancellationRequested();
                currentStep++;
                string itemId = summary.Id.ToString();
                string relativePath = $"transcripts/{itemId}.bundle";

                bool needsUpload = false;
                if (!remoteManifest.Items.TryGetValue(itemId, out var remoteItem))
                {
                    needsUpload = true;
                }
                else if (summary.CreatedAt > remoteItem.UpdatedAtUtc)
                {
                    needsUpload = true;
                }

                if (needsUpload)
                {
                    var fullTranscript = await _transcriptRepo.GetByIdAsync(summary.Id);
                    if (fullTranscript != null)
                    {
                        byte[] rawBytes = JsonSerializer.SerializeToUtf8Bytes(fullTranscript, JsonOptions);
                        string checksum = ComputeSha256(rawBytes);

                        byte[] payloadToUpload = options.EncryptRemotePayloads
                            ? SyncCrypto.Encrypt(rawBytes, options.EncryptionPassphrase!)
                            : rawBytes;

                        await provider.UploadItemAsync(relativePath, payloadToUpload, ct);

                        remoteManifest.Items[itemId] = new SyncItemRecord(
                            ItemId: itemId,
                            ItemType: "transcript",
                            RelativePath: relativePath,
                            UpdatedAtUtc: summary.CreatedAt,
                            Checksum: checksum);

                        uploaded++;
                    }
                }

                progress?.Report(new SyncProgress("Processing transcripts...", currentStep, totalOps));
            }

            // 3. Upload phase: Voiceprints
            if (_voiceprintRepo != null)
            {
                foreach (var vp in localVoiceprints)
                {
                    ct.ThrowIfCancellationRequested();
                    currentStep++;
                    string itemId = vp.Id.ToString();
                    string relativePath = $"voiceprints/{itemId}.bundle";

                    bool needsUpload = false;
                    if (!remoteManifest.Items.TryGetValue(itemId, out var remoteItem))
                    {
                        needsUpload = true;
                    }
                    else if (vp.CreatedAt > remoteItem.UpdatedAtUtc)
                    {
                        needsUpload = true;
                    }

                    if (needsUpload)
                    {
                        byte[] rawBytes = JsonSerializer.SerializeToUtf8Bytes(vp, JsonOptions);
                        string checksum = ComputeSha256(rawBytes);

                        byte[] payloadToUpload = options.EncryptRemotePayloads
                            ? SyncCrypto.Encrypt(rawBytes, options.EncryptionPassphrase!)
                            : rawBytes;

                        await provider.UploadItemAsync(relativePath, payloadToUpload, ct);

                        remoteManifest.Items[itemId] = new SyncItemRecord(
                            ItemId: itemId,
                            ItemType: "voiceprint",
                            RelativePath: relativePath,
                            UpdatedAtUtc: vp.CreatedAt,
                            Checksum: checksum);

                        uploaded++;
                    }
                }
            }

            // 4. Download phase: Reconcile remote items into local database
            progress?.Report(new SyncProgress("Downloading remote updates...", currentStep, totalOps));
            foreach (var kvp in remoteManifest.Items)
            {
                ct.ThrowIfCancellationRequested();
                currentStep++;
                var record = kvp.Value;
                if (record.IsDeleted) continue;

                if (string.Equals(record.ItemType, "transcript", StringComparison.OrdinalIgnoreCase))
                {
                    if (Guid.TryParse(record.ItemId, out var transcriptId))
                    {
                        var localMatch = localSummaries.FirstOrDefault(s => s.Id == transcriptId);

                        if (localMatch == null)
                        {
                            // New transcript from another device
                            byte[]? data = await provider.DownloadItemAsync(record.RelativePath, ct);
                            if (data != null && data.Length > 0)
                            {
                                byte[] plainBytes = options.EncryptRemotePayloads
                                    ? SyncCrypto.Decrypt(data, options.EncryptionPassphrase!)
                                    : data;

                                var transcript = JsonSerializer.Deserialize<Transcript>(plainBytes, JsonOptions);
                                if (transcript != null)
                                {
                                    await _transcriptRepo.SaveAsync(transcript);
                                    downloaded++;
                                }
                            }
                        }
                        else if (record.UpdatedAtUtc > localMatch.CreatedAt)
                        {
                            // Remote is newer than local
                            byte[]? data = await provider.DownloadItemAsync(record.RelativePath, ct);
                            if (data != null && data.Length > 0)
                            {
                                byte[] plainBytes = options.EncryptRemotePayloads
                                    ? SyncCrypto.Decrypt(data, options.EncryptionPassphrase!)
                                    : data;

                                var remoteTranscript = JsonSerializer.Deserialize<Transcript>(plainBytes, JsonOptions);
                                if (remoteTranscript != null)
                                {
                                    var localFull = await _transcriptRepo.GetByIdAsync(transcriptId);
                                    byte[] localBytes = localFull != null ? JsonSerializer.SerializeToUtf8Bytes(localFull, JsonOptions) : [];
                                    string localChecksum = ComputeSha256(localBytes);

                                    if (localFull != null && localChecksum != record.Checksum)
                                    {
                                        // Concurrent conflict branch: preserve local and save conflicted copy
                                        var conflictCopy = remoteTranscript with
                                        {
                                            Id = Guid.NewGuid(),
                                            Title = $"{remoteTranscript.Title} (Conflicted Copy {DateTime.UtcNow:yyyy-MM-dd})"
                                        };
                                        await _transcriptRepo.SaveAsync(conflictCopy);
                                        conflicts++;
                                    }
                                    else
                                    {
                                        await _transcriptRepo.SaveAsync(remoteTranscript);
                                        downloaded++;
                                    }
                                }
                            }
                        }
                    }
                }
                else if (string.Equals(record.ItemType, "voiceprint", StringComparison.OrdinalIgnoreCase) && _voiceprintRepo != null)
                {
                    if (Guid.TryParse(record.ItemId, out var vpId))
                    {
                        var localVp = localVoiceprints.FirstOrDefault(v => v.Id == vpId);
                        if (localVp == null)
                        {
                            byte[]? data = await provider.DownloadItemAsync(record.RelativePath, ct);
                            if (data != null && data.Length > 0)
                            {
                                byte[] plainBytes = options.EncryptRemotePayloads
                                    ? SyncCrypto.Decrypt(data, options.EncryptionPassphrase!)
                                    : data;

                                var voiceprint = JsonSerializer.Deserialize<Voiceprint>(plainBytes, JsonOptions);
                                if (voiceprint != null)
                                {
                                    await _voiceprintRepo.SaveAsync(voiceprint);
                                    downloaded++;
                                }
                            }
                        }
                    }
                }

                progress?.Report(new SyncProgress("Reconciling remote items...", currentStep, totalOps));
            }

            // 5. Finalize manifest
            remoteManifest.LastModifiedUtc = DateTimeOffset.UtcNow;
            await provider.SaveRemoteManifestAsync(remoteManifest, ct);

            progress?.Report(new SyncProgress("Sync complete.", totalOps, totalOps));
            return new SyncResult(true, uploaded, downloaded, conflicts, null);
        }
        catch (Exception ex)
        {
            return new SyncResult(false, uploaded, downloaded, conflicts, ex.Message);
        }
    }

    private static ISyncProvider CreateProvider(SyncOptions options)
    {
        return options.ProviderType switch
        {
            SyncProviderType.LocalDirectory => new LocalFolderSyncProvider(options.TargetUriOrPath),
            SyncProviderType.WebDav => new WebDavSyncProvider(options.TargetUriOrPath, options.Username, options.PasswordOrToken),
            _ => throw new NotSupportedException($"Sync provider '{options.ProviderType}' is not supported.")
        };
    }

    private static string ComputeSha256(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexString(hash);
    }
}
