using System;
using System.IO.Compression;
using System.Threading.Tasks;

namespace EventManager.Abstractions;

public static class Backup
{
    /// <summary>
    /// The file extension used by backup files.
    /// Used to avoid basic user error of importing the wrong file.
    /// </summary>
    public const string FileExtension = ".backup";

    // Make sure the database entry and the file entries cannot clash
    private const string DbEntryName = "db";
    private const string FileEntryNamePrefix = "files/";

    /// <summary>
    /// Creates a backup file for the given database and file storage.
    /// The database is disposed by this method.
    /// </summary>
    public static async Task<File> ExportAsync(Db database, FileStorage fileStorage)
    {
        using var memoryStream = new System.IO.MemoryStream();
        await using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var dbEntry = archive.CreateEntry(DbEntryName);
            await using (var dbEntryStream = await dbEntry.OpenAsync())
            await using (var dbExportStream = await database.ExportAndDisposeAsync())
            {
                await dbExportStream.CopyToAsync(dbEntryStream);
            }

            await fileStorage.ExportAsync(name => archive.CreateEntry(FileEntryNamePrefix + name).OpenAsync());
        }

        return new File.InMemory($"backup{FileExtension}", "application/zip", memoryStream.ToArray());
    }

    /// <summary>
    /// Imports a backup file created by <see cref="ExportAsync "/> into the given database and file storage.
    /// </summary>
    public static async Task ImportAsync(File backup, Db database, FileStorage fileStorage)
    {
        // Sanity checks so users don't accidentally do something stupid
        if (!backup.Name.EndsWith(FileExtension, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Not a {FileExtension} backup: {backup.Name}");
        }

        await using var backupStream = backup.OpenRead();
        await using var zipArchive = new ZipArchive(backupStream, ZipArchiveMode.Read);

        var dbEntry = zipArchive.GetEntry(DbEntryName)
                   ?? throw new InvalidOperationException($"File {backup.Name} is corrupt: no database backup found"); // should never happen
        await using (var dbEntryStream = await dbEntry.OpenAsync())
        {
            await database.OverwriteAsync(dbEntryStream);
        }

        foreach (var entry in zipArchive.Entries)
        {
            if (entry.FullName.StartsWith(FileEntryNamePrefix, StringComparison.Ordinal))
            {
                await fileStorage.ImportAsync(entry.FullName[FileEntryNamePrefix.Length..], await entry.OpenAsync());
            }
        }
    }
}