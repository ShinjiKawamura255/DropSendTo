using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DropSendTo.Services;

internal interface IConfigFileSystem
{
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    bool FileExists(string path);
    string ReadAllText(string path);
    IEnumerable<string> EnumerateFiles(string path, string pattern);
    void WriteAllText(string path, string contents);
    void FlushFile(string path);
    void ReplaceFile(string sourcePath, string destinationPath, string? backupPath);
    void MoveFile(string sourcePath, string destinationPath, bool overwrite = false);
    void DeleteFile(string path);
}

internal sealed class PhysicalConfigFileSystem : IConfigFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public bool FileExists(string path) => File.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public IEnumerable<string> EnumerateFiles(string path, string pattern) => Directory.GetFiles(path, pattern);

    public void WriteAllText(string path, string contents)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(contents);
        writer.Flush();
    }

    public void FlushFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Flush(flushToDisk: true);
    }

    public void ReplaceFile(string sourcePath, string destinationPath, string? backupPath) =>
        File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors: true);

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false) =>
        File.Move(sourcePath, destinationPath, overwrite);

    public void DeleteFile(string path) => File.Delete(path);
}
