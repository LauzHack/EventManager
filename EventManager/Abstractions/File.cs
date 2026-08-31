using System.IO;
using System.Threading.Tasks;

namespace EventManager.Abstractions;

/// <summary>
/// Read-only view of a file.
/// Cannot be empty nor larger than <see cref="MaxSizeInBytes" />.
/// </summary>
/// <param name="Name">The file's name.</param>
/// <param name="MimeType">The file's MIME type.</param>
/// <param name="Length">The length of the file, in bytes.</param>
public abstract record File(string Name, string MimeType, long Length)
{
    /// <summary>
    /// Each file may be at most 2 MB, which should be more than enough for CVs, passport photos, project pictures, etc.
    /// </summary>
    public const uint MaxSizeInBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Opens the file for reading.
    /// </summary>
    public abstract Stream OpenRead();

    /// <summary>
    /// Copies this file's contents to the given stream.
    /// The destination stream is left open.
    /// </summary>
    public async Task CopyToAsync(Stream destination)
    {
        await using var stream = OpenRead();
        await stream.CopyToAsync(destination);
    }

    /// <summary>
    /// In-memory file based on an array of bytes.
    /// Should only be used when I/O is not involved, i.e., do not load a file's entire contents from storage into memory for no reason!
    /// </summary>
    /// <param name="Name">The file's name.</param>
    /// <param name="MimeType">The file's MIME type.</param>
    /// <param name="Contents">The file's contents.</param>
    public sealed record InMemory(string Name, string MimeType, byte[] Contents) : File(Name, MimeType, Contents.Length)
    {
        public override Stream OpenRead()
            => new MemoryStream(Contents, writable: false);
    }
}