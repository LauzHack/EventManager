using System.Diagnostics;
using System.Threading.Tasks;

using EventManager.Models;
using EventManager.Tests.TestInfrastructure;

namespace EventManager.Tests.ChallengeSetterPages;

public abstract class ChallengeSetterTestsBase : TestsBase
{
    protected const string SetterName = "Awesome Company";

    protected async Task AddSetterAsync(bool isOptIn = false, string? description = null)
    {
        Db.ChallengeSetters.Add(new ChallengeSetter(SetterName, 0, isOptIn) { Description = description });
        await Db.CommitAsync();
    }

    protected async Task<ChallengeSetter> GetSetterAsync()
        => await Db.ChallengeSetters.FindAsync(SetterName) ?? throw new UnreachableException();
}