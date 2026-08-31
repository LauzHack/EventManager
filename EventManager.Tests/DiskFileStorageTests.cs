using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Tests.TestInfrastructure;
using EventManager.Web;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests;

[TestClass]
public sealed class DiskFileStorageTests
{
    private string _rootPath = null!;
    private FileStorage _storage = null!;

    [TestInitialize]
    public void Initialize()
    {
        _rootPath = System.IO.Directory.CreateTempSubdirectory("DiskFileStorageTests").FullName;
        _storage = new DiskFileStorage(_rootPath);
    }

    [TestCleanup]
    public void Cleanup()
    {
        System.IO.Directory.Delete(_rootPath, recursive: true);
    }

    [TestMethod]
    public async Task EmptyByDefault()
    {
        var file = await _storage.GetFileAsync("X");
        Assert.IsNull(file);
    }

    [TestMethod]
    public async Task GetAfterStore()
    {
        byte[] data = [0, 1, 2, 3, 4];
        var file = new File.InMemory("name", "text/plain", data);
        var storedId = await _storage.StoreFileAsync(file);
        var retrieved = await _storage.GetFileAsync(storedId);
        Assert.IsNotNull(retrieved);
        Assert.AreEqual(file.Name, retrieved.Name);
        Assert.AreEqual(file.MimeType, retrieved.MimeType);
        Assert.AreEqual(file.Length, retrieved.Length);
        Assert.AreSequenceEqual(data, await retrieved.ReadAsBytesAsync());
    }

    [TestMethod]
    public async Task MissingAfterDelete()
    {
        byte[] data = [0, 1, 2, 3, 4];
        var file = new File.InMemory("name", "text/plain", data);
        var storedId = await _storage.StoreFileAsync(file);
        await _storage.DeleteFileAsync(storedId);
        Assert.IsNull(await _storage.GetFileAsync(storedId));
    }
}