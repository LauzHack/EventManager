using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class LetterDataPageTests : AdminTestsBase
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task PageIsAlwaysOptional(bool dataExists, bool isOwner)
    {
        if (dataExists)
        {
            await SetConfigValueAsync(LetterData);
        }

        var config = await Config.CreateAsync(Db);
        var page = new LetterDataPage(new ConfigValue<LetterData>(config), FileStorage);

        var admin = isOwner ? await GetAdminAsync()
                            : await CreateNonOwnerAdminAsync();
        var result = await page.ViewAsync(admin);

        Assert.IsFalse(result.IsRequired);
        Assert.AreEqual(isOwner, result.IsInteractable);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EditSetsData(bool hadPrevious)
    {
        if (hadPrevious)
        {
            await SetConfigValueAsync(new LetterData("OldAddr", "fr-FR", "Old signee", "old signee contact", "123"));
        }

        var signature = new File.InMemory("name", "image/png", [0, 42, 1, 2]);
        {
            var config = await Config.CreateAsync(Db);
            var page = new LetterDataPage(new ConfigValue<LetterData>(config), FileStorage);

            var result = await page.EditAsync("Address", "fr-CH", "Signee", "Contact", signature);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newConfig = await Config.CreateAsync(Db);
        Assert.IsNotNull(newConfig.LetterData);
        Assert.AreEqual("Address", newConfig.LetterData.Address);
        Assert.AreEqual("fr-CH", newConfig.LetterData.CultureName);
        Assert.AreEqual("Signee", newConfig.LetterData.Signee);
        Assert.AreEqual("Contact", newConfig.LetterData.SigneeContact);

        var storedSignature = await FileStorage.GetFileAsync(newConfig.LetterData.SignatureFileId);
        Assert.IsNotNull(storedSignature);
        Assert.AreEqual(signature.MimeType, storedSignature.MimeType);
    }

    [TestMethod]
    public async Task EditKeepsOldSignatureWhenNotProvided()
    {
        {
            await SetConfigValueAsync(new LetterData("OldAddr", "fr-CH", "Old signee", "old signee contact", "123"));
        }

        {
            var config = await Config.CreateAsync(Db);
            var page = new LetterDataPage(new ConfigValue<LetterData>(config), FileStorage);

            var result = await page.EditAsync("Address", "fr-CH", "Signee", "Contact", null);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var newConfig = await Config.CreateAsync(Db);
        Assert.IsNotNull(newConfig.LetterData);
        Assert.AreEqual("123", newConfig.LetterData.SignatureFileId);
    }

    [TestMethod]
    public async Task EditFailsWithoutSignatureWhenDataDoesNotExistAlready()
    {
        var config = await Config.CreateAsync(Db);
        var page = new LetterDataPage(new ConfigValue<LetterData>(config), FileStorage);

        var result = await page.EditAsync("Address", "fr-CH", "Signee", "Contact", null);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EditFailsForUnknownCulture()
    {
        var config = await Config.CreateAsync(Db);
        var page = new LetterDataPage(new ConfigValue<LetterData>(config), FileStorage);

        var signature = new File.InMemory("name", "image/png", [0, 42, 1, 2]);
        var result = await page.EditAsync("Address", "definitely-not-a-CULTURE-THAT-EXISTS", "Signee", "Contact", signature);
        Assert.AreEqual(Status.UserError, result.Status);
    }

}