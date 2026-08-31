using System;
using System.Linq;

using EventManager.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.Models;

[TestClass]
public sealed class ParticipantStatusTests
{
    [TestMethod]
    public void ToDisplayStringDoesNotDistinguishStatusesEquivalentToCreated()
    {
        Assert.AreEqual(ParticipantStatus.Created.ToDisplayString(), ParticipantStatus.ProfileFilled.ToDisplayString());
        Assert.AreEqual(ParticipantStatus.Created.ToDisplayString(), ParticipantStatus.EmailAddressVerified.ToDisplayString());
    }

    [TestMethod]
    public void ToDisplayStringDistinguishesOtherStatuses()
    {
        var set = Enum.GetValues<ParticipantStatus>().Select(s => s.ToDisplayString()).Distinct();
        Assert.HasCount(Enum.GetValues<ParticipantStatus>().Length - 2, set);
    }

    [TestMethod]
    public void ToDisplayStringDoesNotCrashForUnknownStatus()
    {
        Assert.IsNotNull(((ParticipantStatus)99999).ToDisplayString());
    }
}