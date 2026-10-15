using Scriber.Core.Domain;

namespace Scriber.Export;

/// <summary>
/// Common contract for transcript format exporters.
/// </summary>
public interface ITranscriptExporter
{
    string FormatName { get; }
    string FileExtension { get; }
    string MimeType { get; }

    string Export(Transcript transcript, ExportOptions? options = null);
    Task ExportAsync(Transcript transcript, Stream outputStream, ExportOptions? options = null, CancellationToken ct = default);
}
