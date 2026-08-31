using System.Threading.Tasks;

using EventManager.Abstractions;

namespace EventManager.Tests.TestInfrastructure;

public static class FileExtensions
{
    public static async Task<byte[]> ReadAsBytesAsync(this File file)
    {
        using var memoryStream = new System.IO.MemoryStream();
        await file.CopyToAsync(memoryStream);
        return memoryStream.ToArray();
    }
}