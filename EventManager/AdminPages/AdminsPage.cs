using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class AdminsPage(DbValues<Admin> admins, EmailSender emailSender) : Page<Admin>
{
    public override bool RedisplayAfterAction
        => true;

    public override async Task<PageView> ViewAsync(Admin admin)
    {
        if (admin.IsOwner)
        {
            return EditableView("Admins", "Manage");
        }
        return ForbiddenView();
    }

    public override async Task<object?> GetModelAsync(Admin admin)
        => await admins.ToCollectionAsync();

    public async Task<StatusMessage> AddAsync(Admin admin, string emailAddress, bool isOwner)
    {
        var existing = await admins.FindAsync(emailAddress);
        if (existing == admin)
        {
            return Error("You cannot edit yourself!");
        }
        if (existing is not null)
        {
            // We could send the same email and show the same message, but it might confuse people
            if (existing.IsOwner != isOwner)
            {
                existing.IsOwner = isOwner;
                await emailSender.SendEmailAsync(
                    recipient: existing.EmailAddress,
                    subject: "Administration",
                    body: $"You have been {(isOwner ? "given" : "stripped of")} ownership rights and can log in with the link below.",
                    operation: Operation.CreatePageView<Admin>()
                );
                return Success($"**{emailAddress}** has been {(isOwner ? "given" : "stripped of")} ownership rights, and got a login link via email.");
            }

            await emailSender.SendEmailAsync(
                recipient: existing.EmailAddress,
                subject: "Administration",
                body: "You can log in with the link below.",
                operation: Operation.CreatePageView<Admin>()
            );
            return Success($"**{emailAddress}** was already an admin, and got a login link via email.");
        }

        // No explicit email verification, we trust admins to not create nonsense, and it's easy to remove any accidents anyway
        var newAdmin = new Admin(emailAddress)
        {
            IsEmailAddressVerified = true,
            IsOwner = isOwner
        };
        admins.Add(newAdmin);

        await emailSender.SendEmailAsync(
            recipient: newAdmin.EmailAddress,
            subject: "Administration",
            body: "Hi there,\n\n"
                + $"You are now an admin of this event{(isOwner ? " with ownership rights" : "")}.\n\n"
                + "Click the link below to log in, and keep this email so you can log in later if your session expires.\n\n"
                + "_If you do not know what this event is, please ignore this email._",
            operation: Operation.CreatePageView<Admin>()
        );

        return Success($"**{emailAddress}** is now an admin, and was notified of this.");
    }

    public async Task<StatusMessage> RemoveAsync(Admin admin, string emailAddress)
    {
        var adminToRemove = await admins.FindAsync(emailAddress);
        if (adminToRemove is null)
        {
            return Error($"There is no admin with email address **{emailAddress}**.");
        }
        if (admin == adminToRemove)
        {
            return Error("You cannot remove yourself!");
        }

        admins.Remove(adminToRemove);

        await emailSender.SendEmailAsync(
            recipient: adminToRemove.EmailAddress,
            subject: "Administration",
            body: "You are no longer an admin.",
            operation: null
        );

        return Success($"**{emailAddress}** is no longer an admin, and was notified of this.");
    }
}