using System.Diagnostics;
using System.Threading.Tasks;

using EventManager.Models;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.AdminPages;

public abstract class AdminTestsBase : TestsBase
{
    protected const string AdminEmailAddress = "admin@example.org";

    [TestInitialize]
    public async Task DerivedInitialize()
    {
        var admin = new Admin(AdminEmailAddress) { IsOwner = true };
        Db.Admins.Add(admin);
        await Db.CommitAsync();
    }

    protected async Task<Admin> CreateNonOwnerAdminAsync()
    {
        var nonOwner = new Admin("not-owner@example.org") { IsEmailAddressVerified = true };
        Db.Admins.Add(nonOwner);
        await Db.CommitAsync();
        var result = await Db.Admins.FindAsync("not-owner@example.org");
        Assert.IsNotNull(result);
        return result;
    }

    protected async Task<Admin> GetAdminAsync()
        => await Db.Admins.FindAsync(AdminEmailAddress)
             ?? throw new UnreachableException();
}