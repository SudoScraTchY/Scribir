namespace Scriber.Storage.Sync;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

/// <summary>
/// WebDAV sync provider supporting Nextcloud, ownCloud, Apache WebDAV, and standard RFC 4918 endpoints.
/// </summary>
public sealed class WebDavSyncProvider : ISyncProvider
{
    private readonly HttpClient _client;
    private readonly bool _disposeClient;
    private readonly string _baseUrl;
    private readonly HashSet<string> _knownCollections = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public WebDavSyncProvider(string baseUrl, string? username = null, string? passwordOrToken = null, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL cannot be empty.", nameof(baseUrl));
        }

        _baseUrl = baseUrl.TrimEnd('/') + "/";
        _client = httpClient ?? new HttpClient();
        _disposeClient = httpClient == null;

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(passwordOrToken))
        {
            string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{passwordOrToken}"));
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
        else if (!string.IsNullOrEmpty(passwordOrToken))
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", passwordOrToken);
        }

        _client.DefaultRequestHeaders.Accept.Clear();
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), _baseUrl);
            req.Headers.Add("Depth", "0");
            using var resp = await _client.SendAsync(req, ct);

            if (resp.IsSuccessStatusCode || resp.StatusCode == (HttpStatusCode)207 || resp.StatusCode == HttpStatusCode.NotFound)
            {
                if (resp.StatusCode == HttpStatusCode.NotFound)
                {
                    // Attempt MKCOL on base path
                    using var mkcolReq = new HttpRequestMessage(new HttpMethod("MKCOL"), _baseUrl);
                    using var mkcolResp = await _client.SendAsync(mkcolReq, ct);
                    return mkcolResp.IsSuccessStatusCode || mkcolResp.StatusCode == HttpStatusCode.MethodNotAllowed;
                }
                return true;
            }

            // Fallback to GET/HEAD
            using var headReq = new HttpRequestMessage(HttpMethod.Head, _baseUrl);
            using var headResp = await _client.SendAsync(headReq, ct);
            return headResp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<SyncManifest?> GetRemoteManifestAsync(CancellationToken ct = default)
    {
        string manifestUrl = _baseUrl + "manifest.json";
        try
        {
            using var resp = await _client.GetAsync(manifestUrl, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            resp.EnsureSuccessStatusCode();
            byte[] bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            return JsonSerializer.Deserialize<SyncManifest>(bytes);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveRemoteManifestAsync(SyncManifest manifest, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        await EnsureDirectoryAsync("", ct);

        string manifestUrl = _baseUrl + "manifest.json";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var resp = await _client.PutAsync(manifestUrl, content, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<byte[]?> DownloadItemAsync(string relativePath, CancellationToken ct = default)
    {
        string url = _baseUrl + relativePath.TrimStart('/');
        try
        {
            using var resp = await _client.GetAsync(url, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsByteArrayAsync(ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task UploadItemAsync(string relativePath, byte[] data, CancellationToken ct = default)
    {
        string normalized = relativePath.TrimStart('/').Replace('\\', '/');
        string? parentDir = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parentDir))
        {
            await EnsureDirectoryAsync(parentDir, ct);
        }

        string url = _baseUrl + normalized;
        using var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var resp = await _client.PutAsync(url, content, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task DeleteItemAsync(string relativePath, CancellationToken ct = default)
    {
        string url = _baseUrl + relativePath.TrimStart('/').Replace('\\', '/');
        try
        {
            using var resp = await _client.DeleteAsync(url, ct);
        }
        catch { }
    }

    private async Task EnsureDirectoryAsync(string relativeDir, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(relativeDir))
        {
            return;
        }

        string cleanDir = relativeDir.Trim('/');
        if (_knownCollections.Contains(cleanDir))
        {
            return;
        }

        var segments = cleanDir.Split('/');
        string current = "";

        foreach (var seg in segments)
        {
            current = string.IsNullOrEmpty(current) ? seg : current + "/" + seg;
            if (_knownCollections.Contains(current))
            {
                continue;
            }

            string mkcolUrl = _baseUrl + current + "/";
            try
            {
                using var req = new HttpRequestMessage(new HttpMethod("MKCOL"), mkcolUrl);
                using var resp = await _client.SendAsync(req, ct);
                // 201 Created or 405 MethodNotAllowed (already exists)
                if (resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.MethodNotAllowed)
                {
                    _knownCollections.Add(current);
                }
            }
            catch
            {
                // Proceed optimistically
            }
        }
    }

    public void Dispose()
    {
        if (_disposeClient)
        {
            _client.Dispose();
        }
    }
}
