using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.Abstractions;

[TestClass]
public sealed class PageViewTests
{
    [TestMethod]
    public async Task EqualitySanityCheck()
    {
        var page = new FakePage();
        var participant = new Participant("alice@example.org");
        var first = await page.ViewAsync(participant);
        var second = await page.ViewAsync(participant);
        Assert.AreEqual(first, second);
    }

    private sealed class FakePage : Page<Participant>
    {
        public override async Task<PageView> ViewAsync(Participant participant)
            => RequiredView("Title");
    }
}