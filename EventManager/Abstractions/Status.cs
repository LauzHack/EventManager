namespace EventManager.Abstractions;

/// <summary>
/// Status of an action execution.
/// </summary>
public enum Status
{
    /// <summary>
    /// Nothing happened.
    /// For instance, an idempotent action was performed a second time.
    /// </summary>
    None = 0,

    /// <summary>
    /// Successful result.
    /// </summary>
    Success = 1,

    /// <summary>
    /// Successful result the user must pay particular attention to.
    /// </summary>
    ImportantInformation = 2,

    /// <summary>
    /// User error, not the system's fault.
    /// </summary>
    UserError = 3,

    /// <summary>
    /// System error, may happen when dependencies are not working, such as remote resources not being available,
    /// but generally a cause for worry.
    /// </summary>
    SystemError = 4
}