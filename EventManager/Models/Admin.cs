namespace EventManager.Models;

/// <summary>
/// An administrator of the system.
/// </summary>
public sealed class Admin(string emailAddress) : User
{
    /// <summary>
    /// Admins are identified by their email address.
    /// </summary>
    public override string Id => EmailAddress;

    /// <summary>
    /// The admin's email address, which uniquely identifies them.
    /// </summary>
    public string EmailAddress { get; private set; } = emailAddress;

    /// <summary>
    /// Whether this admin's email address is verified.
    /// </summary>
    public bool IsEmailAddressVerified { get; set; }

    /// <summary>
    /// Whether this admin's email address needs re-verification due to importing a backup.
    /// </summary>
    /// <remarks>
    /// We could reuse <see cref="IsEmailAddressVerified" /> for this, but because the presence of an admin with this property set
    /// is used to give anyone access to the admin email setup page,
    /// we really don't want any bug elsewhere on the website to accidentally allow anyone to edit that page.
    /// </remarks>
    public bool NeedsReverificationAfterBackupImport { get; set; }

    /// <summary>
    /// Whether this admin is an owner of the system,
    /// i.e., can edit fundamental event properties, change the event's status, add new admins, and send mass emails.
    /// This is more of an "accident prevention" permission than anything else, since all admins can read and write most data.
    /// </summary>
    public bool IsOwner { get; set; }
}