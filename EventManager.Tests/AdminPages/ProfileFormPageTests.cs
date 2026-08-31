using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.AdminPages;
using EventManager.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

[TestClass]
public sealed class ProfileFormPageTests : AdminTestsBase
{
    [TestMethod]
    public async Task PageIsRequiredWhenProfileFormIsMissing()
    {
        var config = await Config.CreateAsync(Db);
        var page = new ProfileFormPage(new ConfigValue<ProfileForm>(config));
        var view = await page.ViewAsync(await GetAdminAsync());

        Assert.IsTrue(view.IsRequired);
        Assert.IsTrue(view.IsInteractable);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PageIsEditableWhenProfileFormExists(bool isOwner)
    {
        var config = await Config.CreateAsync(Db);
        config.Set(new ProfileForm([], []));
        var page = new ProfileFormPage(new ConfigValue<ProfileForm>(config));

        var admin = isOwner ? await GetAdminAsync()
                            : await CreateNonOwnerAdminAsync();
        var view = await page.ViewAsync(admin);

        Assert.IsFalse(view.IsRequired);
        Assert.AreEqual(isOwner, view.IsInteractable);
    }

    [TestMethod]
    public async Task EditSetsProfileForm()
    {
        var config = await Config.CreateAsync(Db);
        var page = new ProfileFormPage(new ConfigValue<ProfileForm>(config));

        var form = new ProfileForm([new("Text", "D", true, ["z"], true, ["x", "y"])], [new("File", "D2", false, [])]);
        var result = await page.EditAsync(form);
        Assert.AreEqual(Status.Success, result.Status);
        await Db.CommitAsync();

        var config2 = await Config.CreateAsync(Db);
        Assert.IsNotNull(config2.ProfileForm);
        // ImmutableArray has references semantics :(
        var single1 = Assert.ContainsSingle(form.Choices);
        var single2 = Assert.ContainsSingle(config2.ProfileForm.Choices);
        Assert.AreEqual(single1.Name, single2.Name);
        Assert.AreEqual(single1.Description, single2.Description);
        Assert.AreEqual(single1.IsRequired, single2.IsRequired);
        Assert.AreSequenceEqual(single1.Options, single2.Options);
        Assert.AreSequenceEqual(single1.CustomOptionSuggestions, single2.CustomOptionSuggestions);
        Assert.AreSequenceEqual(form.Files, config2.ProfileForm.Files);
    }

    [TestMethod]
    public async Task EditFailsWhenMultipleChoicesHaveTheSameName()
    {
        var config = await Config.CreateAsync(Db);
        var page = new ProfileFormPage(new ConfigValue<ProfileForm>(config));

        var form = new ProfileForm([new("Name", "D", true, ["x"], true, []), new("Name", "D2", false, ["y"], true, ["z"])], []);
        var result = await page.EditAsync(form);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EditFailsWhenMultipleFilesHaveTheSameName()
    {
        var config = await Config.CreateAsync(Db);
        var page = new ProfileFormPage(new ConfigValue<ProfileForm>(config));

        var form = new ProfileForm([], [new("Name", "D", true, ["pdf"]), new("Name", "D2", false, ["png"])]);
        var result = await page.EditAsync(form);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EditFailsWhenAChoiceHasTheSameNameAsAFile()
    {
        var config = await Config.CreateAsync(Db);
        var page = new ProfileFormPage(new ConfigValue<ProfileForm>(config));

        var form = new ProfileForm([new("Name", "D", true, ["x"], true, [])], [new("Name", "D", true, ["pdf"])]);
        var result = await page.EditAsync(form);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EditFailsWhenAChoiceHasAWhitespaceOnlyName()
    {
        var config = await Config.CreateAsync(Db);
        var page = new ProfileFormPage(new ConfigValue<ProfileForm>(config));

        var form = new ProfileForm([new(" \t", "D", true, ["x"], true, [])], []);
        var result = await page.EditAsync(form);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EditFailsWhenAFileHasAWhitespaceOnlyName()
    {
        var config = await Config.CreateAsync(Db);
        var page = new ProfileFormPage(new ConfigValue<ProfileForm>(config));

        var form = new ProfileForm([], [new("\n ", "D", true, ["pdf"])]);
        var result = await page.EditAsync(form);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EditFailsWhenAChoiceHasNoOptions()
    {
        var config = await Config.CreateAsync(Db);
        var page = new ProfileFormPage(new ConfigValue<ProfileForm>(config));

        var form = new ProfileForm([new("Name", "D", true, [], false, [])], []);
        var result = await page.EditAsync(form);
        Assert.AreEqual(Status.UserError, result.Status);
    }

    [TestMethod]
    public async Task EditFailsWhenAChoiceForbidsCustomOptionsButHasSuggestionsForThem()
    {
        var config = await Config.CreateAsync(Db);
        var page = new ProfileFormPage(new ConfigValue<ProfileForm>(config));

        var form = new ProfileForm([new("Name", "D", false, ["a", "b"], false, ["x", "y"])], []);
        var result = await page.EditAsync(form);
        Assert.AreEqual(Status.UserError, result.Status);
    }
}