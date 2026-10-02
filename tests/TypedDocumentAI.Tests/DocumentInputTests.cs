using System.Text;

namespace TypedDocumentAI.Tests;

public sealed class DocumentInputTests
{
    [Fact]
    public void Bytes_are_defensively_copied()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var input = DocumentInput.FromBytes(bytes, "scan.png", "image/png");
        bytes[0] = 99;
        Assert.Equal((byte)1, input.Content.Span[0]);
    }

    [Theory]
    [InlineData("../../scan.pdf", "scan.pdf")]
    [InlineData("C:\\private\\scan.pdf", "scan.pdf")]
    [InlineData(" scan.pdf ", "scan.pdf")]
    public void File_names_are_sanitized(string supplied, string expected) =>
        Assert.Equal(expected, DocumentInput.FromBytes(new byte[] { 1 }, supplied, "application/pdf").FileName);

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("bad\nname.pdf")]
    public void Invalid_file_names_are_rejected(string name) =>
        Assert.Throws<ArgumentException>(() => DocumentInput.FromBytes(new byte[] { 1 }, name, "application/pdf"));

    [Fact]
    public void Media_type_is_normalized() =>
        Assert.Equal("application/pdf", DocumentInput.FromBytes(new byte[] { 1 }, "a.pdf", "Application/PDF; charset=utf-8").ContentType);

    [Theory]
    [InlineData("bad")]
    [InlineData("*/*")]
    [InlineData("application/pdf\r\nX-Test: injected")]
    public void Invalid_media_types_are_rejected(string mime) =>
        Assert.Throws<ArgumentException>(() => DocumentInput.FromBytes(new byte[] { 1 }, "a.pdf", mime));

    [Fact]
    public void Empty_bytes_are_rejected() =>
        Assert.Throws<ArgumentException>(() => DocumentInput.FromBytes(Array.Empty<byte>(), "a.pdf", "application/pdf"));

    [Fact]
    public void Bytes_over_the_limit_are_rejected() =>
        Assert.Throws<DocumentLimitException>(() => DocumentInput.FromBytes(new byte[] { 1, 2 }, "a.pdf", "application/pdf", 1));

    [Fact]
    public async Task Stream_is_read_from_current_position_and_not_disposed()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("abc"));
        stream.Position = 1;
        var document = await DocumentInput.FromStreamAsync(stream, "a.pdf", "application/pdf");
        Assert.Equal("bc", Encoding.UTF8.GetString(document.Content.Span));
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task Non_seekable_streams_are_supported_without_taking_ownership()
    {
        using var stream = new NonSeekableStream(new byte[] { 1, 2, 3 });
        var document = await DocumentInput.FromStreamAsync(stream, "a.pdf", "application/pdf");
        Assert.Equal(3, document.Length);
        Assert.False(stream.WasDisposed);
    }

    [Fact]
    public async Task Stream_limit_reads_at_most_one_extra_byte()
    {
        using var stream = new NonSeekableStream(new byte[1000]);
        await Assert.ThrowsAsync<DocumentLimitException>(() => DocumentInput.FromStreamAsync(stream, "a.pdf", "application/pdf", 10));
        Assert.Equal(11, stream.BytesRead);
        Assert.False(stream.WasDisposed);
    }

    [Fact]
    public async Task Exactly_at_limit_is_accepted()
    {
        await using var stream = new MemoryStream(new byte[] { 1, 2 });
        var document = await DocumentInput.FromStreamAsync(stream, "a.pdf", "application/pdf", 2);
        Assert.Equal(2, document.Length);
    }

    [Fact]
    public async Task Empty_stream_is_rejected()
    {
        await using var stream = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() => DocumentInput.FromStreamAsync(stream, "a.pdf", "application/pdf"));
    }

    [Fact]
    public async Task Cancellation_is_preserved()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var stream = new MemoryStream(new byte[] { 1 });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DocumentInput.FromStreamAsync(stream,
            "a.pdf", "application/pdf", cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Local_file_is_closed_after_reading()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
            var document = await DocumentInput.FromFileAsync(path);
            Assert.Equal("application/pdf", document.ContentType);
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(3, exclusive.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
