namespace EventManager.Models;

/// <summary>
/// The status of a travel expense.
/// There is no "rejected" status because rejected expenses are deleted instead.
/// </summary>
public enum TravelExpenseStatus
{
    /// <summary>
    /// The expense has been submitted by a participant for approval by an admin.
    /// </summary>
    Submitted = 0,

    /// <summary>
    /// The expense has been approved by an admin after being submitted by a participant.
    /// </summary>
    Approved = 1,

    /// <summary>
    /// The expense was entered by an admin as being already reimbursed.
    /// </summary>
    Reimbursed = 2
}