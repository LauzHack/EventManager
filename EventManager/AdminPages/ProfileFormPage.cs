using System;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class ProfileFormPage(ConfigValue<ProfileForm> profileForm) : Page<Admin>
{
    public override async Task<PageView> ViewAsync(Admin admin)
    {
        if (profileForm.Value is null)
        {
            return RequiredView("Profile form");
        }
        if (admin.IsOwner)
        {
            return EditableView("Profile form", "Edit");
        }
        return ForbiddenView();
    }

    public async Task<StatusMessage> EditAsync(ProfileForm form)
    {
        if (form.Choices.Select(c => c.Name).Concat(form.Files.Select(f => f.Name)).GroupBy(n => n, StringComparer.Ordinal).Any(c => c.Skip(1).Any()))
        {
            return Error("Multiple choices or files cannot have the same name.");
        }

        if (form.Choices.Any(c => string.IsNullOrWhiteSpace(c.Name)))
        {
            return Error("A choice cannot have an empty name.");
        }
        if (form.Choices.Any(c => c.Options is [] && !c.AllowsCustomOption))
        {
            return Error("A choice cannot have no options if custom options are disallowed.");
        }
        if (form.Choices.Any(c => !c.AllowsCustomOption && c.CustomOptionSuggestions is not []))
        {
            return Error("A choice cannot have custom option suggestions if custom options are disallowed.");
        }

        if (form.Files.Any(f => string.IsNullOrWhiteSpace(f.Name)))
        {
            return Error("A file cannot have an empty name.");
        }

        profileForm.Set(form);
        return Success("Profile form updated.");
    }
}