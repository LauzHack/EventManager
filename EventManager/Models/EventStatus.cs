namespace EventManager.Models;

/// <summary>
/// The status of the overall event, from the point of view of the system.
/// </summary>
/// <remarks>
/// These are ordered to make comparisons easy: anything after ApplicationsClosed is "logically more" than ApplicationsClosed,
/// so one can do ">= ApplicationsClosed" to also check for CheckInStarted, CheckInClosed, etc.
/// </remarks>
public enum EventStatus
{
    /// <summary>
    /// The event is still being configured, only admins can interact with the website.
    /// </summary>
    Configuring = 0,

    /// <summary>
    /// Applications are open, participants can apply.
    /// </summary>
    ApplicationsOpen = 1,

    /// <summary>
    /// Applications are closed, though participants can still interact with the website for tasks such as requesting a visa invitation letter.
    /// </summary>
    ApplicationsClosed = 2,

    /// <summary>
    /// Check-in has started.
    /// </summary>
    CheckInStarted = 3,

    /// <summary>
    /// Check-in has closed, though late participants can still be checked in if necessary.
    /// </summary>
    CheckInClosed = 4,

    /// <summary>
    /// The event has entered the judging phase.
    /// Travel expense submission is no longer possible.
    /// </summary>
    JudgingStarted = 5,

    /// <summary>
    /// The event is over. Judging results are now public.
    /// </summary>
    Finished = 6
}