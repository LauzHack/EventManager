using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class EmailSetupPage(Db database,
                                   ConfigValue<AuthenticationSecret> authSecret,
                                   ConfigValue<EmailSenderSettings> emailSettings,
                                   FileStorage fileStorage,
                                   EmailSender emailSender) : Page<Admin?>
{
    public override async Task<PageView> ViewAsync(Admin? admin)
    {
        // The only time this page should be available without auth is at the very beginning, when no admin has verified their email!
        // We cannot rely just on email settings being unset, as they'll be set once the email is sent.
        // (The alternative would be to encode all email settings in the email operation itself, but that's rather dubious)
        var verifiedAdmin = await database.Admins.FirstOrDefaultAsync(a => a.IsEmailAddressVerified);
        // ...with one exception: if a backup has just been restored, in which case there's an admin with the "needs reverification" flag set.
        // That admin won't log in until they use the verification email, but we still need to show them a page, not an "authentication required" one, as that would be poor UX.
        var adminNeedingReverification = await database.Admins.FirstOrDefaultAsync(a => a.NeedsReverificationAfterBackupImport);
        if (verifiedAdmin is null || emailSettings.Value is null || adminNeedingReverification is not null)
        {
            return RequiredView("Email setup");
        }
        if (admin is null || !admin.IsOwner)
        {
            return ForbiddenView();
        }
        return EditableView("Email setup", "Edit",
            ("Sending emails as", $"{emailSettings.Value.SenderName} <{emailSettings.Value.SenderAddress}>"),
            ("Users reply to", emailSettings.Value.ReplyToAddress)
        );
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Important to catch and report any possible email exception here.")]
    public async Task<StatusMessage> EditAsync(string adminEmailAddress, EmailSenderSettings settings)
    {
        AuthenticationSecret? newSecret = null;
        if (authSecret.Value is null)
        {
            var hashKey = new byte[Authenticator.DesiredKeySizeInBytes];
            // Exception to the "no global variables" policy: strong random number generation for the private key
            RandomNumberGenerator.Fill(hashKey);
            newSecret = new AuthenticationSecret([.. hashKey]);
            authSecret.Set(newSecret);
        }

        try
        {
            var email = new Email(
                adminEmailAddress,
                "Admin login",
                "Please use the link below to log in.\n\n**Keep this email, you will need it to log in again!**",
                Operation: Operation.CreatePageAction<Admin?, EmailSetupPage>(nameof(VerifyEmailAddressAsync)),
                OperationDescription: "Log in"
            );
            await emailSender.SendAsync([email], settings, newSecret);
            emailSettings.Set(settings);
        }
        catch (Exception e)
        {
            return Error($"Couldn't send an email, please try inputting the values again. (Error message: {e.Message})");
        }

        // This can be used to add new admins, that's fine, the alternative is that the main admin might misspell their email address and then can't change it...
        if (await database.Admins.FindAsync(adminEmailAddress) is null)
        {
            database.Admins.Add(new Admin(adminEmailAddress) { IsOwner = true });
        }

        return ImportantInformation("Please log in via email to continue. (If you do not receive an email, please double-check the values and try again)");
    }

    public async Task<StatusMessage> VerifyEmailAddressAsync(Admin admin)
    {
        if (!admin.IsEmailAddressVerified || admin.NeedsReverificationAfterBackupImport)
        {
            admin.IsEmailAddressVerified = true;
            admin.NeedsReverificationAfterBackupImport = false;
            return Success("You're logged in! Now you can configure the event.");
        }

        return NoChange();
    }

    public async Task<StatusMessage> ImportBackupAsync(string adminEmailAddress, File backup)
    {
        await Backup.ImportAsync(backup, database, fileStorage);

        // Force re-verification, so that the admin goes through the normal flow
        if (await database.Admins.FindAsync(adminEmailAddress) is Admin existingAdmin)
        {
            existingAdmin.NeedsReverificationAfterBackupImport = true;
        }
        else
        {
            database.Admins.Add(new Admin(adminEmailAddress) { IsOwner = true, NeedsReverificationAfterBackupImport = true });
        }

        var email = new Email(
            adminEmailAddress,
            "Admin login after restore",
            "Please use the link below to log in now that the system has been restored from a backup.\n\n**Keep this email, you will need it to log in again!**",
            Operation: Operation.CreatePageAction<Admin?, EmailSetupPage>(nameof(VerifyEmailAddressAsync)),
            OperationDescription: "Log in"
        );
        await emailSender.SendAsync([email], emailSettings.Value, authSecret.Value);

        return ImportantInformation("Backup restored. Please log in via email to continue.");
    }
}