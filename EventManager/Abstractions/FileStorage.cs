using System;
using System.Threading.Tasks;

namespace EventManager.Abstractions;

/// <summary>
/// Permanent storage for files on the server, referring to files by a string ID.
/// </summary>
public abstract class FileStorage
{
    /// <summary>
    /// Gets the file with the given ID, or null if there is no such file.
    /// </summary>
    public abstract Task<File?> GetFileAsync(string id);

    /// <summary>
    /// Stores the given file, returning its new ID.
    /// </summary>
    public abstract Task<string> StoreFileAsync(File file);

    /// <summary>
    /// Deletes the file with the given ID.
    /// </summary>
    public abstract Task DeleteFileAsync(string id);

    /// <summary>
    /// Exports all files using the given name-to-stream function.
    /// The names and data formats are implementation-defined.
    /// Created streams will be disposed by this method.
    /// </summary>
    public abstract Task ExportAsync(Func<string, Task<System.IO.Stream>> streamCreator);

    /// <summary>
    /// Imports a file with the given name and stream created by <see cref="ExportAsync" />.
    /// The stream will be disposed by this method.
    /// </summary>
    public abstract Task ImportAsync(string name, System.IO.Stream stream);
}