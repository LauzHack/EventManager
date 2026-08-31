using System.Diagnostics;
using System.Threading.Tasks;

using EventManager.Models;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.ParticipantPages;

public abstract class ParticipantTestsBase : TestsBase
{
    protected const string ParticipantEmailAddress = "participant@example.org"; // not upper case, some tests depend on this

    [TestInitialize]
    public async Task DerivedInitialize()
    {
        var participant = new Participant(ParticipantEmailAddress);
        Db.Participants.Add(participant);
        await Db.CommitAsync();
    }

    protected async Task<Participant> GetParticipantAsync()
        => await Db.Participants.FindAsync(ParticipantEmailAddress)
        ?? throw new UnreachableException();

    protected async Task SetParticipantStatusAsync(ParticipantStatus status)
    {
        var participant = await GetParticipantAsync();
        participant.Status = status;
        await Db.CommitAsync();
    }
}