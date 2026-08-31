using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class LetterDataPage(ConfigValue<LetterData> letterData, FileStorage fileStorage) : Page<Admin>
{
    public override async Task<PageView> ViewAsync(Admin admin)
    {
        if (!admin.IsOwner)
        {
            return ForbiddenView();
        }
        if (letterData.Value is null)
        {
            return EditableView("Letters", "Configure", ("Disabled", ""));
        }
        return EditableView("Letters", "Edit", ("Signee", letterData.Value.Signee));
    }

    public async Task<StatusMessage> EditAsync(string address, string cultureName, string signee, string contact, File? signature)
    {
        var known = CultureInfo.GetCultures(CultureTypes.AllCultures).Any(c => c.Name.Equals(cultureName, StringComparison.OrdinalIgnoreCase));

        if (!known)
        {
            return Error($"Invalid culture name: {cultureName}");
        }

        string signatureId;
        if (signature is null)
        {
            if (letterData.Value is null)
            {
                return Error("Please provide a signature");
            }
            signatureId = letterData.Value.SignatureFileId;
        }
        else
        {
            signatureId = await fileStorage.StoreFileAsync(signature);
            if (letterData.Value?.SignatureFileId is string existingId)
            {
                await fileStorage.DeleteFileAsync(existingId);
            }
        }

        letterData.Set(new(address, cultureName, signee, contact, signatureId));
        return Success("Letter data updated.");
    }
}