using System;

using EventManager.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.Models;

[TestClass]
public sealed class RgbColorTests
{
    [TestMethod]
    public void ParseThreeDigits()
    {
        var color = RgbColor.Parse("#e0A");
        Assert.AreEqual("#EE00AA", color.ToString());
    }

    [TestMethod]
    public void ParseSixDigits()
    {
        var color = RgbColor.Parse("#a1B2c3");
        Assert.AreEqual("#A1B2C3", color.ToString());
    }

    [TestMethod]
    public void ParseFailure()
    {
        Assert.Throws<ArgumentException>(() => RgbColor.Parse("#12"));
    }

    [TestMethod]
    public void ForegroundOfBlackBackgroundIsWhite()
    {
        Assert.AreEqual(RgbColor.White, RgbColor.Black.PickForegroundColor());
    }

    [TestMethod]
    public void ForegroundOfWhiteBackgroundIsBlack()
    {
        Assert.AreEqual(RgbColor.Black, RgbColor.White.PickForegroundColor());
    }

    [TestMethod]
    public void ForegroundOfSwissRedIsWhite()
    {
        var swissRed = RgbColor.Parse("#DA291C");
        Assert.AreEqual(RgbColor.White, swissRed.PickForegroundColor());
    }
}