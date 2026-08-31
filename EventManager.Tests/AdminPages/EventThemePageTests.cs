using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class EventThemePageTests : AdminTestsBase
{
    [TestMethod]
    public async Task PageIsRequiredWhenThemeIsMissing()
    {
        var config = await Config.CreateAsync(Db);
        var page = new EventThemePage(new ConfigValue<EventTheme>(config), FileStorage);

        var result = await page.ViewAsync(await GetAdminAsync());

        Assert.IsTrue(result.IsRequired);
        Assert.IsTrue(result.IsInteractable);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PageIsOptionalWhenThemeIsSet(bool isOwner)
    {
        {
            var existingConfig = await Config.CreateAsync(Db);
            existingConfig.Set(EventTheme);
            await Db.CommitAsync();
        }
        var config = await Config.CreateAsync(Db);
        var page = new EventThemePage(new ConfigValue<EventTheme>(config), FileStorage);

        var admin = isOwner ? await GetAdminAsync()
                            : await CreateNonOwnerAdminAsync();
        var result = await page.ViewAsync(admin);

        Assert.IsFalse(result.IsRequired);
        Assert.AreEqual(isOwner, result.IsInteractable);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EditSetsTheme(bool existed)
    {
        if (existed)
        {
            var oldConfig = await Config.CreateAsync(Db);
            oldConfig.Set(EventTheme);
            await Db.CommitAsync();
        }

        var logo = new File.InMemory("logo.png", "image/png", [1, 2, 3, 4]);
        var icon = new File.InMemory("icon.jpg", "image/jpg", [5, 6, 7]);

        {
            var config = await Config.CreateAsync(Db);
            var page = new EventThemePage(new ConfigValue<EventTheme>(config), FileStorage);

            var result = await page.EditAsync(RgbColor.White, logo, icon);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();
        }

        var config2 = await Config.CreateAsync(Db);
        Assert.IsNotNull(config2.EventTheme);
        Assert.AreEqual(RgbColor.White, config2.EventTheme.BackgroundColor);
        Assert.AreEqual(RgbColor.Black, config2.EventTheme.ForegroundColor);

        Assert.IsNotNull(config2.EventTheme.LogoFileId);
        var storedLogo = await FileStorage.GetFileAsync(config2.EventTheme.LogoFileId);
        Assert.IsNotNull(storedLogo);
        Assert.AreEqual(logo.MimeType, storedLogo.MimeType);

        Assert.IsNotNull(config2.EventTheme.IconFileId);
        var storedIcon = await FileStorage.GetFileAsync(config2.EventTheme.IconFileId);
        Assert.IsNotNull(storedIcon);
        Assert.AreEqual(icon.MimeType, storedIcon.MimeType);

        Assert.AreEqual(icon.MimeType, config2.EventTheme.IconMimeType);
    }

    [TestMethod]
    public async Task EditCanChangeJustBackgroundColorIfThemeIsAlreadySet()
    {
        {
            var oldConfig = await Config.CreateAsync(Db);
            oldConfig.Set(EventTheme);
            await Db.CommitAsync();
        }

        var config = await Config.CreateAsync(Db);
        var page = new EventThemePage(new ConfigValue<EventTheme>(config), FileStorage);

        var color = new RgbColor(1, 2, 3);
        var result = await page.EditAsync(color, null, null);
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var config2 = await Config.CreateAsync(Db);
        Assert.IsNotNull(config2.EventTheme);
        Assert.AreEqual(color, config2.EventTheme.BackgroundColor);
    }

    [TestMethod]
    public async Task EditFailsIfThemeIsMissingAndNoLogoIsProvided()
    {
        var config = await Config.CreateAsync(Db);
        var page = new EventThemePage(new ConfigValue<EventTheme>(config), FileStorage);

        var result = await page.EditAsync(RgbColor.Black, null, new File.InMemory("name", "image/png", [0, 1, 2, 3]));
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EditFailsIfThemeIsMissingAndNoIconIsProvided()
    {
        var config = await Config.CreateAsync(Db);
        var page = new EventThemePage(new ConfigValue<EventTheme>(config), FileStorage);

        var result = await page.EditAsync(RgbColor.Black, new File.InMemory("name", "image/png", [0, 1, 2, 3]), null);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task OldFilesAreDeletedWhenThemeExistsAndReplacementIsProvided()
    {
        var oldLogo = new File.InMemory("oldLogo.png", "image/png", [1, 2, 3, 4]);
        var oldIcon = new File.InMemory("oldIcon.jpg", "image/jpg", [5, 6, 7]);

        {
            var config = await Config.CreateAsync(Db);
            var page = new EventThemePage(new ConfigValue<EventTheme>(config), FileStorage);

            var result = await page.EditAsync(RgbColor.White, oldLogo, oldIcon);
            Assert.AreEqual(Status.Success, result.Status);
            await Db.CommitAsync();

        }

        var newLogo = new File.InMemory("newLogo.bmp", "image/bmp", [5, 5, 5]);
        var newIcon = new File.InMemory("newIcon.tif", "image/tiff", [10, 11, 12, 13]);

        var config2 = await Config.CreateAsync(Db);
        Assert.IsNotNull(config2.EventTheme);
        Assert.IsNotNull(config2.EventTheme.LogoFileId);
        Assert.IsNotNull(config2.EventTheme.IconFileId);
        string oldLogoId = config2.EventTheme.LogoFileId;
        string oldIconId = config2.EventTheme.IconFileId;

        var page2 = new EventThemePage(new ConfigValue<EventTheme>(config2), FileStorage);

        var result2 = await page2.EditAsync(RgbColor.White, newLogo, newIcon);
        Assert.AreEqual(Status.Success, result2.Status);

        Assert.IsNull(await FileStorage.GetFileAsync(oldLogoId));
        Assert.IsNull(await FileStorage.GetFileAsync(oldIconId));
    }
}