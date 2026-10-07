using System.IO;
using System.Text;
using DropSendTo.Services;
using Xunit;

namespace DropSendTo.Tests;

public class ConfigPayloadFileTests
{
    [Fact]
    public async Task ReadAsync_ShouldRejectOversizedFile_BeforeReadingContent()
    {
        using var stream = new CountingStream(ConfigTransferService.MaxPackageBytes + 1L, seekable: true);
        await Assert.ThrowsAsync<ConfigPayloadSizeException>(() => ConfigPayloadFile.ReadAsync(stream));
        Assert.Equal(0, stream.BytesRead);
    }

    [Fact]
    public async Task ReadAsync_ShouldBoundReads_WhenLengthIsUnavailable()
    {
        using var stream = new CountingStream(ConfigTransferService.MaxPackageBytes * 2L, seekable: false);
        await Assert.ThrowsAsync<ConfigPayloadSizeException>(() => ConfigPayloadFile.ReadAsync(stream));
        Assert.Equal(ConfigTransferService.MaxPackageBytes + 1L, stream.BytesRead);
    }

    [Fact]
    public async Task ReadAsync_ShouldReadUtf8BomAndJapanese_WithoutClosingInput()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("日本語 payload")).ToArray());
        Assert.Equal("日本語 payload", await ConfigPayloadFile.ReadAsync(stream));
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task ReadAsync_ShouldAcceptExactLimit_AndHonorCancellation()
    {
        using var stream = new CountingStream(ConfigTransferService.MaxPackageBytes, seekable: false);
        Assert.Equal(ConfigTransferService.MaxPackageBytes, (await ConfigPayloadFile.ReadAsync(stream)).Length);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var unread = new CountingStream(100, seekable: false);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConfigPayloadFile.ReadAsync(unread, canceled.Token));
        Assert.Equal(0, unread.BytesRead);
    }

    [Fact]
    public async Task WriteFileAsync_ShouldPreserveExistingExport_WhenCanceled()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DropSendToTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "backup.dstcfg");
        try
        {
            await File.WriteAllTextAsync(path, "original export");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConfigPayloadFile.WriteFileAsync(path, "new export", canceled.Token));
            Assert.Equal("original export", await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(directory));
            await ConfigPayloadFile.WriteFileAsync(path, "complete export");
            Assert.Equal("complete export", await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class CountingStream(long length, bool seekable) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => seekable;
        public override bool CanWrite => false;
        public override long Length => seekable ? length : throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = (int)Math.Min(count, length - BytesRead);
            Array.Fill(buffer, (byte)'a', offset, read);
            BytesRead += read;
            return read;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = (int)Math.Min(buffer.Length, length - BytesRead);
            buffer.Span[..read].Fill((byte)'a');
            BytesRead += read;
            return ValueTask.FromResult(read);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
