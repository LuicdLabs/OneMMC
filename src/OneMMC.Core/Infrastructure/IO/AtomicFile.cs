using System.IO;
using System.Text;
using System.Threading;

namespace OneMMC.Core.Infrastructure.IO;

/// <summary>
/// Writes files so that readers see either the old content or the new content, never a truncated file.
/// </summary>
/// <remarks>
/// The content is written to a temporary file in the destination directory, flushed to disk, and then
/// swapped in with <see cref="File.Replace(string, string, string?, bool)"/> (<c>ReplaceFileW</c>), which
/// keeps the destination's security descriptor, attributes and creation time. That matters for files
/// such as <c>Registry.pol</c> and <c>gpt.ini</c>, whose ACLs are set by Windows.
/// </remarks>
public static class AtomicFile
{
    private const int ReplaceAttempts = 3;
    private const int ReplaceRetryDelayMilliseconds = 100;

    /// <summary>
    /// Atomically replaces <paramref name="path"/> with the content produced by <paramref name="write"/>.
    /// </summary>
    /// <param name="path">The destination file. Its directory is created if missing.</param>
    /// <param name="write">Writes the complete new content to the supplied stream.</param>
    public static void Write(string path, Action<Stream> write)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(write);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The path has no directory.", nameof(path));
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Path.GetRandomFileName()}.tmp");
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            Swap(tempPath, fullPath);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>
    /// Atomically replaces <paramref name="path"/> with <paramref name="contents"/>.
    /// </summary>
    /// <param name="path">The destination file.</param>
    /// <param name="contents">The new text content.</param>
    /// <param name="encoding">The encoding to use; UTF-8 without a byte order mark when null.</param>
    public static void WriteAllText(string path, string contents, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(contents);
        var bytes = (encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).GetBytes(contents);
        var preamble = encoding?.GetPreamble() ?? [];
        Write(path, stream =>
        {
            stream.Write(preamble);
            stream.Write(bytes);
        });
    }

    private static void Swap(string tempPath, string destinationPath)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(destinationPath))
                {
                    File.Replace(tempPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, destinationPath);
                }

                return;
            }
            catch (IOException) when (attempt < ReplaceAttempts)
            {
                // Another process (the Group Policy service, an antivirus scanner) can hold the destination
                // open for a moment without FILE_SHARE_DELETE. Back off briefly and try again.
                Thread.Sleep(ReplaceRetryDelayMilliseconds);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
