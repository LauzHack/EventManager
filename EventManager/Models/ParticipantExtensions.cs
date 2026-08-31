using System;
using System.Collections.Generic;
using System.Linq;

namespace EventManager.Models;

/// <summary>
/// Extension methods related to <see cref="Participant" />.
/// </summary>
public static class ParticipantExtensions
{
    // We always put participants with a full name first, easier to read for humans

    // We have to provide two overloads for IQueryable because providers might give you an IQueryable that is an IOrderedQueryable at runtime
    // even if it hasn't gone through any OrderBy yet... yay.

    /// <summary>
    /// Orders the given sequence of participants by given name, family name, then email address.
    /// </summary>
    public static IOrderedQueryable<Participant> OrderByName(this IQueryable<Participant> source)
        => source.OrderBy(p => p.GivenName == null)
                 .ThenBy(p => p.GivenName ?? p.EmailAddress)
                 .ThenBy(p => p.FamilyName)
                 .ThenBy(p => p.EmailAddress);

    /// <summary>
    /// Orders the given ordered sequence of participants by given name, family name, then email address.
    /// </summary>
    public static IOrderedQueryable<Participant> ThenByName(this IOrderedQueryable<Participant> source)
        => source.ThenBy(p => p.GivenName == null)
                 .ThenBy(p => p.GivenName ?? p.EmailAddress)
                 .ThenBy(p => p.FamilyName)
                 .ThenBy(p => p.EmailAddress);

    /// <summary>
    /// Orders the given sequence of participants by given name, family name, then email address.
    /// </summary>
    public static IOrderedEnumerable<Participant> OrderByName(this IEnumerable<Participant> source)
        => source.OrderBy(p => p.GivenName == null)
                 .ThenBy(p => p.GivenName ?? p.EmailAddress, StringComparer.OrdinalIgnoreCase)
                 .ThenBy(p => p.FamilyName, StringComparer.OrdinalIgnoreCase)
                 .ThenBy(p => p.EmailAddress, StringComparer.OrdinalIgnoreCase);
}