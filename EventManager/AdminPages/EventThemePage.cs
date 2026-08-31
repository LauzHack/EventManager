using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class EventThemePage(ConfigValue<EventTheme> eventTheme, FileStorage storage) : Page<Admin>
{
    public override async Task<PageView> ViewAsync(Admin admin)
    {
        if (eventTheme.Value is null)
        {
            return RequiredView("Event theme");
        }
        if (admin.IsOwner)
        {
            return EditableView("Event theme", "Edit");
        }
        return ForbiddenView();
    }

    public async Task<StatusMessage> EditAsync(RgbColor backgroundColor, File? logo, File? icon)
    {
        string? logoFileId = null;
        string? iconFileId = null;
        string? iconFileMimeType = null;

        if (logo is not null)
        {
            logoFileId = await storage.StoreFileAsync(logo);
        }

        if (icon is not null)
        {
            iconFileId = await storage.StoreFileAsync(icon);
            iconFileMimeType = icon.MimeType;
        }

        if (eventTheme.Value is EventTheme existing)
        {
            if (logoFileId is null)
            {
                logoFileId = existing.LogoFileId;
            }
            else
            {
                await storage.DeleteFileAsync(existing.LogoFileId);
            }

            if (iconFileId is null)
            {
                iconFileId = existing.IconFileId;
                iconFileMimeType = existing.IconMimeType;
            }
            else
            {
                await storage.DeleteFileAsync(existing.IconFileId);
            }
        }

        if (logoFileId is null)
        {
            return Error("Missing logo file");
        }
        if (iconFileId is null || iconFileMimeType is null)
        {
            return Error("Missing icon file");
        }

        eventTheme.Set(new(backgroundColor, backgroundColor.PickForegroundColor(), logoFileId, iconFileId, iconFileMimeType));
        return Success("Event theme updated.");
    }
}