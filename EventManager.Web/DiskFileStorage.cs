using System;
using System.IO;
using System.Threading.Tasks;

namespace EventManager.Web;

public sealed class DiskFileStorage(string rootPath) : Abstractions.FileStorage
{
    private const string MetadataPrefix = "mime_";
    private const char MimeNameSeparator = '\n'; // MIME first since the name can be anything

    public override async Task<Abstractions.File?> GetFileAsync(string id)
    {
        Directory.CreateDirectory(rootPath);

        var metadataPath = Path.Join(rootPath, MetadataPrefix + id);
        var path = Path.Join(rootPath, id);

        try
        {
            var metadata = await File.ReadAllTextAsync(metadataPath);
            var splitMetadata = metadata.Split(MimeNameSeparator, 2);
            return new DiskFile(splitMetadata[1], splitMetadata[0], path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public override async Task<string> StoreFileAsync(Abstractions.File file)
    {
        Directory.CreateDirectory(rootPath);

        string id = Guid.NewGuid().ToString();
        var path = Path.Join(rootPath, id);
        await using var destination = File.Create(path);
        await file.CopyToAsync(destination);

        var mimePath = Path.Join(rootPath, MetadataPrefix + id);
        await File.WriteAllTextAsync(mimePath, file.MimeType + MimeNameSeparator + file.Name);

        return id;
    }

    public override async Task DeleteFileAsync(string id)
    {
        Directory.CreateDirectory(rootPath);

        var mimePath = Path.Join(rootPath, MetadataPrefix + id);
        var path = Path.Join(rootPath, id);

        File.Delete(mimePath);
        File.Delete(path);
    }

    public override async Task ExportAsync(Func<string, Task<Stream>> streamCreator)
    {
        foreach (var file in Directory.EnumerateFiles(rootPath))
        {
            var fileInfo = new FileInfo(file);
            await using var fileStream = fileInfo.OpenRead();
            await using var exportStream = await streamCreator(fileInfo.Name);
            await fileStream.CopyToAsync(exportStream);
        }
    }

    public override async Task ImportAsync(string name, Stream stream)
    {
        Directory.CreateDirectory(rootPath);

        await using var fileStream = File.OpenWrite(Path.Join(rootPath, name));
        await stream.CopyToAsync(fileStream);
    }

    private sealed record DiskFile(string Name, string MimeType, string Path) : Abstractions.File(Name, MimeType, new FileInfo(Path).Length)
    {
        public override Stream OpenRead()
            => File.OpenRead(Path);
    }
}