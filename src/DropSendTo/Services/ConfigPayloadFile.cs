using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DropSendTo.Services;

internal sealed class ConfigPayloadSizeException : InvalidOperationException
{
    internal ConfigPayloadSizeException() : base("The import file exceeds the 16 MiB limit.") { }
}

internal static class ConfigPayloadFile
{
    internal static async Task<string> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const int limit = ConfigTransferService.MaxPackageBytes;
        if (stream.CanSeek && stream.Length - stream.Position > limit) throw TooLarge();
        using var contents = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            // Reading one extra byte distinguishes an exact-limit file from a growing or non-seekable input.
            int remaining = (int)(limit - contents.Length + 1);
            int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (contents.Length + read > limit) throw TooLarge();
            contents.Write(buffer, 0, read);
        }
        contents.Position = 0;
        using var reader = new StreamReader(contents);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ConfigPayloadSizeException TooLarge() => new();

    internal static async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ReadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task WriteFileAsync(string path, string payload, CancellationToken cancellationToken = default)
    {
        string destination = Path.GetFullPath(path);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, payload, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
