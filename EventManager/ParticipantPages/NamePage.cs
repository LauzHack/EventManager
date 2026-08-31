using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.ParticipantPages;

public sealed class NamePage(IQueryable<Participant> participants) : Page<Participant>
{
    /// <summary>
    /// Placeholder users must input if they have no family name,
    /// to ensure this scenario is supported while also avoiding accidental omissions.
    /// </summary>
    public const string EmptyFamilyNamePlaceholder = ".";

    public override async Task<PageView> ViewAsync(Participant participant)
    {
        if (participant.GivenName is null)
        {
            return RequiredView("Name");
        }

        var summary = new List<PageSummaryItem> { ("Given name", participant.GivenName) };
        if (participant.FamilyName is not null)
        {
            summary.Add(("Family name", participant.FamilyName));
        }

        if (participant.Status < ParticipantStatus.Finalized)
        {
            return EditableView("Name", "Edit", summary);
        }
        return SummaryOnlyView("Name", summary);
    }

    public async Task<StatusMessage> EditAsync(Participant participant, string givenName, string familyName)
    {
        if (string.IsNullOrWhiteSpace(givenName) || string.IsNullOrWhiteSpace(familyName))
        {
            return Error($"Names cannot be blank. _If you do not have a family name, please input \"{EmptyFamilyNamePlaceholder}\" as the family name._");
        }

        bool hadName = participant.GivenName != null;

        participant.GivenName = givenName;
        participant.FamilyName = familyName.Equals(EmptyFamilyNamePlaceholder, StringComparison.Ordinal) ? null : familyName;

        if (hadName)
        {
            return Success($"You changed your name to **{participant.FullName}**.");
        }

        participant.PossibleAliasEmailAddresses = await FindPossibleAliasesAsync(participant);

        return Success($"Welcome, {participant.FullName}!");
    }

    private Task<IReadOnlyCollection<string>> FindPossibleAliasesAsync(Participant participant)
    {
        // There are many possible edge cases here,
        // because we adopt the Western model of given+family name,
        // but people from cultures with !=2 name parts may have input it differently.
        // (e.g., "Volodymyr Oleksandrovych Zelenskyy" could put the patronymic in either the given or family position, or omit it altogether)
        //
        // Since we cannot support all possible cultures,
        // we will assume participants got the hint and split their name into their favorite "given" and "family" names when they registered,
        // even if their culture does not have that concept natively, and thus we only need to check two cases:
        // - Same given name, same family name
        // - Swapped given and family names
        // The latter can happen because not all cultures order them the same, so it's easy to be confused.
        // (France deserves a special mention for not even having an agreed-upon order...)
        return participants.Where(p => p.EmailAddress != participant.EmailAddress
                                    && ((p.GivenName == participant.GivenName && p.FamilyName == participant.FamilyName)
                                     || (p.GivenName == participant.FamilyName && p.FamilyName == participant.GivenName)))
                           .Select(p => p.EmailAddress)
                           .ToCollectionAsync();
    }
}