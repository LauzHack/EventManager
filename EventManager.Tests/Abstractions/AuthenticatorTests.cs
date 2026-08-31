using System.Linq;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.Abstractions;

[TestClass]
public sealed class AuthenticatorTests
{
    private static readonly AuthenticationSecret _secret = new([.. Enumerable.Range(0, 64).Select(n => (byte)n)]);

    [TestMethod]
    public void EmptyStorageAndArgumentsYieldNoUser()
    {
        var storage = new FakeClientSideStorage();

        Assert.IsNull(Authenticator.LogUserIn(_secret, Operation.CreatePageView<TestUser>(), storage));
    }

    [TestMethod]
    public void LogUserInSetsStorage()
    {
        var user = new TestUser("user@example.org");
        var storage = new FakeClientSideStorage();

        var opWithUser = Authenticator.AddAuthentication(_secret, Operation.CreatePageView<TestUser>(), user.Id);
        var retrievedId = Authenticator.LogUserIn(_secret, opWithUser, storage);

        Assert.AreEqual(user.Id, retrievedId);
    }

    [TestMethod]
    public void LogUserInStoresAndRetrievesUserUsingClientSideStorage()
    {
        var user = new TestUser("user@example.org");
        var storage = new FakeClientSideStorage();

        var opWithUser = Authenticator.AddAuthentication(_secret, Operation.CreatePageView<TestUser>(), user.Id);
        Authenticator.LogUserIn(_secret, opWithUser, storage);

        var retrievedId = Authenticator.LogUserIn(_secret, Operation.CreatePageView<TestUser>(), storage);

        Assert.AreEqual(user.Id, retrievedId);
    }

    [TestMethod]
    public void LogUserInReturnsNullForPublicOperation()
    {
        var user = new TestUser("user@example.org");
        var storage = new FakeClientSideStorage();

        var opWithUser = Authenticator.AddAuthentication(_secret, Operation.CreatePageView<TestUser>(), user.Id);
        Authenticator.LogUserIn(_secret, opWithUser, storage);

        var op = Operation.Parse(new("https://example.org/404"));
        var result = Authenticator.LogUserIn(_secret, op, storage);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void AuthenticatorPrioritizesLogInOverStorage()
    {
        var user1 = new TestUser("user@example.org");
        var user2 = new TestUser("other@example.org");
        var storage = new FakeClientSideStorage();

        var opWithUser1 = Authenticator.AddAuthentication(_secret, Operation.CreatePageView<TestUser>(), user1.Id);
        Authenticator.LogUserIn(_secret, opWithUser1, storage);

        var opWithUser2 = Authenticator.AddAuthentication(_secret, Operation.CreatePageView<TestUser>(), user2.Id);
        var retrievedId = Authenticator.LogUserIn(_secret, opWithUser2, storage);

        Assert.AreEqual(user2.Id, retrievedId);
    }

    [TestMethod]
    public void CannotUseHashFromDifferentUserOrType()
    {
        var user1 = new TestUser("user@example.org");
        var user2 = new TestUser("other@example.org");
        var storage = new FakeClientSideStorage();

        var opWithUser1 = Authenticator.AddAuthentication(_secret, Operation.CreatePageView<TestUser>(), user1.Id);

        // very ugly but we want to simulate an attack where the attacker knows the implementation details
        string idKey = (string)typeof(Authenticator).GetRequiredMethod("IdKey").Invoke(null, [typeof(TestUser)])!;
        string hashedIdKey = (string)typeof(Authenticator).GetRequiredMethod("HashedIdKey").Invoke(null, [typeof(TestUser)])!;

        Assert.IsTrue(opWithUser1.Arguments.TryGetText(hashedIdKey, out var hash));
        Assert.IsNotNull(Authenticator.LogUserIn(_secret, opWithUser1, storage));

        var attemptedOpWithUser2 = Operation.CreatePageView<TestUser>()
                                            .WithExtraTextArgument(idKey, user2.Id)
                                            .WithExtraTextArgument(hashedIdKey, hash);

        Assert.IsNull(Authenticator.LogUserIn(_secret, attemptedOpWithUser2, storage));
        Assert.IsNull(Authenticator.LogUserIn(_secret, Operation.CreatePageView<TestUser2>() with { Arguments = opWithUser1.Arguments }, storage));
    }

    private sealed class TestUser(string id) : User
    {
        public override string Id => id;
    }
    private sealed class TestUser2(string id) : User
    {
        public override string Id => id;
    }
}