namespace Scriber.App.Services;

/// <summary>
/// Cross-platform implementation of external media launch and share-sheet integration (PH2.5-PLAT-001, ADR-0001, ADR-0011).
/// Resolves file associations, Android content:// URIs, and scriber:// protocol activations to trigger Flow A import.
/// </summary>
public sealed class MediaLaunchHandler : IMediaLaunchHandler
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".mp4", ".mkv", ".webm", ".wma"
    };

    public Task<bool> CanHandleAsync(Uri uri)
    {
        if (uri == null) return Task.FromResult(false);

        if (string.Equals(uri.Scheme, "scriber", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(true);
        }

        if (string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(true);
        }

        if (string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase))
        {
            string ext = Path.GetExtension(uri.LocalPath);
            return Task.FromResult(SupportedExtensions.Contains(ext));
        }

        if (Path.IsPathRooted(uri.OriginalString))
        {
            string ext = Path.GetExtension(uri.OriginalString);
            return Task.FromResult(SupportedExtensions.Contains(ext));
        }

        return Task.FromResult(false);
    }

    public async Task HandleIncomingMediaAsync(Uri uri, CancellationToken ct = default)
    {
        if (uri == null) return;

        string? resolvedFilePath = null;

        if (string.Equals(uri.Scheme, "scriber", StringComparison.OrdinalIgnoreCase))
        {
            // e.g. scriber://transcribe?path=... or scriber://transcribe?file=...
            var query = uri.Query;
            resolvedFilePath = ExtractQueryParam(query, "path") ?? ExtractQueryParam(query, "file");
        }
        else if (string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase))
        {
            resolvedFilePath = uri.LocalPath;
        }
        else if (string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase))
        {
            resolvedFilePath = await ResolveContentUriToCacheAsync(uri, ct);
        }
        else if (Path.IsPathRooted(uri.OriginalString) && File.Exists(uri.OriginalString))
        {
            resolvedFilePath = uri.OriginalString;
        }

        if (string.IsNullOrWhiteSpace(resolvedFilePath)) return;

        string targetPath = resolvedFilePath;
        if (!File.Exists(targetPath))
        {
            string unescaped = Uri.UnescapeDataString(targetPath);
            if (File.Exists(unescaped))
            {
                targetPath = unescaped;
            }
        }

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.GoToAsync($"//transcribe?file={Uri.EscapeDataString(targetPath)}");
                }
            }
            catch
            {
                // In case shell navigation fails during early startup
            }
        });
    }

    public static string? ExtractQueryParam(string? query, string key)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        string trimmed = query.TrimStart('?');
        var pairs = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }
        return null;
    }

    private static async Task<string?> ResolveContentUriToCacheAsync(Uri uri, CancellationToken ct)
    {
#if ANDROID
        try
        {
            var androidUri = Android.Net.Uri.Parse(uri.ToString());
            if (androidUri == null) return null;

            var resolver = Android.App.Application.Context.ContentResolver;
            if (resolver == null) return null;

            string fileName = "shared_audio_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ".wav";
            try
            {
                using var cursor = resolver.Query(androidUri, null, null, null, null);
                if (cursor != null && cursor.MoveToFirst())
                {
                    int nameIndex = cursor.GetColumnIndex(Android.Provider.IOpenableColumns.DisplayName);
                    if (nameIndex >= 0)
                    {
                        string? name = cursor.GetString(nameIndex);
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            fileName = name;
                        }
                    }
                }
            }
            catch { }

            string destinationPath = Path.Combine(FileSystem.CacheDirectory, fileName);
            using var inputStream = resolver.OpenInputStream(androidUri);
            if (inputStream == null) return null;

            using var outputStream = File.Create(destinationPath);
            await inputStream.CopyToAsync(outputStream, ct);
            return destinationPath;
        }
        catch
        {
            return null;
        }
#else
        await Task.CompletedTask;
        return uri.LocalPath;
#endif
    }
}
