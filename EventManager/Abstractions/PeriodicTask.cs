using System;
using System.Threading.Tasks;

namespace EventManager.Abstractions;

/// <summary>
/// A periodic task run by the system every hour or so.
/// </summary>
public abstract class PeriodicTask
{
    /// <summary>
    /// The period for all tasks.
    /// </summary>
    public static readonly TimeSpan Period = TimeSpan.FromHours(1);

    /// <summary>
    /// Runs the task, optionally returning a description of what happened.
    /// </summary>
    public abstract Task<string?> RunAsync();
}