using System.Buffers;
using System.Net.Http.Headers;

namespace TypedDocumentAI;

/// <summary>A buffered, reusable document snapshot. No file is uploaded by creating this object.</summary>
public sealed class DocumentInput
{
    /// <summary>Default local input limit: 20 MiB. Provider limits are checked separately.</summary>
    public const int DefaultMaxBytes = 20 * 1024 * 1024;
    private readonly byte[] _content;

    private DocumentInput(byte[] content, string fileName, string contentType)
    {
        _content = content;
        FileName = fileName;
        ContentType = contentType;
    }

    /// <summary>The sanitized base name, without directory components.</summary>
    public string FileName { get; }
    /// <summary>The normalized media type, without parameters.</summary>
    public string ContentType { get; }
    /// <summary>The number of document bytes, before base64 encoding.</summary>
    public int Length => _content.Length;
    /// <summary>The document bytes. Consumers must not mutate their underlying storage.</summary>
    public ReadOnlyMemory<byte> Content => _content;

    /// <summary>Creates an input, defensively copying the supplied bytes.</summary>
    public static DocumentInput FromBytes(
        ReadOnlyMemory<byte> content, string fileName, string contentType,
        int maxBytes = DefaultMaxBytes)
    {
        var (name, mime) = ValidateMetadata(fileName, contentType, maxBytes);
        if (content.IsEmpty)
        {
            throw new ArgumentException("The document is empty.", nameof(content));
        }
        if (content.Length > maxBytes)
        {
            throw new DocumentLimitException("The document exceeds the local input limit.", maxBytes);
        }
        return new DocumentInput(content.ToArray(), name, mime);
    }

    /// <summary>Reads from the current stream position, with a strict byte limit. Never disposes the caller's stream.</summary>
    public static async Task<DocumentInput> FromStreamAsync(
        Stream stream, string fileName, string contentType,
        int maxBytes = DefaultMaxBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var (name, mime) = ValidateMetadata(fileName, contentType, maxBytes);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The document stream must be readable.", nameof(stream));
        }
        cancellationToken.ThrowIfCancellationRequested();
        using var buffer = new MemoryStream();
        var rented = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                // Read at most one byte beyond the limit, including for non-seekable streams.
                var count = (int)Math.Min(rented.Length, (long)maxBytes - buffer.Length + 1);
                var read = await stream.ReadAsync(rented.AsMemory(0, count), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                if (buffer.Length + read > maxBytes)
                {
                    throw new DocumentLimitException("The document exceeds the local input limit.", maxBytes);
                }
                buffer.Write(rented, 0, read);
            }
            if (buffer.Length == 0)
            {
                throw new ArgumentException("The document is empty.", nameof(stream));
            }
            return new DocumentInput(buffer.ToArray(), name, mime);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }

    /// <summary>Reads and closes a local file. Supported inferred extensions are PDF, PNG, JPG, JPEG and WEBP.</summary>
    public static async Task<DocumentInput> FromFileAsync(
        string path, string? contentType = null, int maxBytes = DefaultMaxBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        contentType ??= Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => throw new ArgumentException("Supply a media type for this file extension.", nameof(contentType))
        };
        _ = ValidateMetadata(Path.GetFileName(path), contentType, maxBytes);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await FromStreamAsync(stream, Path.GetFileName(path), contentType, maxBytes, cancellationToken)
            .ConfigureAwait(false);
    }

    private static (string Name, string Mime) ValidateMetadata(string fileName, string contentType, int maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        var name = Path.GetFileName(fileName.Trim().Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > 255 || name.Any(char.IsControl))
        {
            throw new ArgumentException("The document file name is invalid.", nameof(fileName));
        }
        if (!MediaTypeHeaderValue.TryParse(contentType, out var media) ||
            string.IsNullOrEmpty(media.MediaType) || media.MediaType.Contains('*'))
        {
            throw new ArgumentException("The document media type is invalid.", nameof(contentType));
        }
        return (name, media.MediaType.ToLowerInvariant());
    }
}
