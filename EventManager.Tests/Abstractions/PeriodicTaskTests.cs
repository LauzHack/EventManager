using System;

using EventManager.Abstractions;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.Abstractions;

[TestClass]
public sealed class PeriodicTaskTests
{
    [TestMethod]
    public void PeriodIsPositive()
    {
        // somewhat silly test just for coverage
        Assert.IsGreaterThan(TimeSpan.Zero, PeriodicTask.Period);
    }
}