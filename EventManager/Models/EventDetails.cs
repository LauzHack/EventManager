using System;
using System.Globalization;

namespace EventManager.Models;

/// <summary>
/// Details about an event.
/// </summary>
/// <param name="Title">The event title.</param>
/// <param name="Location">The event location.</param>
/// <param name="TimeZone">The event time zone.</param>
/// <param name="Start">The event's start date and time.</param>
/// <param name="End">The event's end date and time.</param>
/// <param name="ConfirmationText">Information for event participants.</param>
/// <param name="WebsiteUrl">The URL of the main event website.</param>
/// <param name="HelpUrl">The URL at which participants may find help.</param>
/// <param name="PrivacyPolicy">The event's privacy policy, governing participant data.</param>
public sealed record EventDetails(
    string Title,
    string Location,
    string TimeZone,
    DateTimeOffset Start,
    DateTimeOffset End,
    string ConfirmationText,
    Uri WebsiteUrl,
    Uri HelpUrl,
    string PrivacyPolicy
)
{
    /// <summary>
    /// Converts these event details to the text of a simple ICS file suitable for import in calendar software.
    /// </summary>
    /// <param name="currentTime">The date and time at which the ICS file is being generated.</param>
    /// <param name="id">The ID of the event in the ICS file, which must be unique.</param>
    public string ToIcsText(DateTimeOffset currentTime, Guid id)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        string ToIcsDate(DateTimeOffset date)
        {
            // it's much simpler to convert the timezone ourselves,
            // using ICS timezones is only really needed for recurring events
            var offset = zone.GetUtcOffset(date);
            var zonedDate = new DateTimeOffset(date.DateTime, offset);
            return zonedDate.ToUniversalTime().ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        }

        return $"""
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:{nameof(EventManager)}
        BEGIN:VEVENT
        UID:{id}
        DTSTAMP:{ToIcsDate(currentTime)}
        DTSTART:{ToIcsDate(Start)}
        DTEND:{ToIcsDate(End)}
        SUMMARY:{Title}
        LOCATION:{Location}
        END:VEVENT
        END:VCALENDAR
        """;
    }

    /// <summary>
    /// Gets the date(s) of this event, simplifying if it starts and ends on the same date.
    /// </summary>
    public string ToDateString()
    {
        const string DateFormat = "MMMM d"; // not the default 'm' format which uses 'dd', e.g., "February 03" -> ugly
        if (Start.Date == End.Date)
        {
            return Start.Date.ToString(DateFormat, CultureInfo.InvariantCulture);
        }
        // \u2013 == en-dash
        return $"{Start.Date.ToString(DateFormat, CultureInfo.InvariantCulture)} \u2013 {End.Date.ToString(DateFormat, CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Gets the title, location, and dates of this event.
    /// </summary>
    public override string ToString()
        => $"{Title}, held at {Location} on {ToDateString()}";
}