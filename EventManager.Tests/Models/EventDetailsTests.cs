using System;

using EventManager.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.Models;

[TestClass]
public sealed class EventDetailsTests
{
    [TestMethod]
    public void ToIcsGeneratesValidIcs()
    {
        var details = new EventDetails(
            "My event title",
            "Location somewhere over the rainbow",
            "Europe/Zurich",
            new DateTimeOffset(2025, 11, 22, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 11, 23, 17, 0, 0, TimeSpan.Zero),
            "<confirmation text unused here>",
            new("https://example.org/unused-here"),
            new("https://example.org/unused-here"),
            "<privacy policy unused here>"
        );
        var id = Guid.NewGuid();
        var currentTime = new DateTimeOffset(2025, 12, 25, 15, 43, 12, TimeSpan.Zero);
        // This file content has been manually tested with Google Calendar
        string expected = $"""
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:{nameof(EventManager)}
        BEGIN:VEVENT
        UID:{id}
        DTSTAMP:20251225T144312Z
        DTSTART:20251122T070000Z
        DTEND:20251123T160000Z
        SUMMARY:My event title
        LOCATION:Location somewhere over the rainbow
        END:VEVENT
        END:VCALENDAR
        """;
        Assert.AreEqual(expected, details.ToIcsText(currentTime, id));
    }

    [TestMethod]
    public void ToStringUsesSingleDateForSameDay()
    {
        var details = new EventDetails("Title", "Location", "Europe/Zurich", new(2001, 2, 3, 18, 0, 0, TimeSpan.Zero), new(2001, 2, 3, 23, 0, 0, TimeSpan.Zero), "", new("https://example.org"), new("https://example.org"), "");
        Assert.AreEqual("Title, held at Location on February 3", details.ToString());
    }

    [TestMethod]
    public void ToStringUsesBothDatesForDifferentDays()
    {
        var details = new EventDetails("Title", "Location", "Europe/Zurich", new(2001, 2, 3, 18, 0, 0, TimeSpan.Zero), new(2001, 2, 5, 23, 0, 0, TimeSpan.Zero), "", new("https://example.org"), new("https://example.org"), "");
        Assert.AreEqual("Title, held at Location on February 3 \u2013 February 5", details.ToString());
    }
}