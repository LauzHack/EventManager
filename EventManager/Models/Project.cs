using System.Collections.Generic;

namespace EventManager.Models;

/// <summary>
/// A project from a team of participants.
/// </summary>
public sealed class Project(string id, string title, string shortDescription, string longDescription, string link, string thumbnailId)
{
    public const uint MaxTitleLength = 40;
    public const uint MaxShortDescriptionLength = 100;
    public const uint MaxLongDescriptionLength = 1000;
    public const uint MaxThumbnailSizeInBytes = 300 * 1024; // 300 KB should be more than enough

    /// <summary>
    /// The project's ID.
    /// </summary>
    public string Id { get; } = id;

    /// <summary>
    /// The title of the project.
    /// </summary>
    public string Title { get; set; } = title;

    /// <summary>
    /// A short description of the project, suitable for a judging spreadsheet.
    /// </summary>
    public string ShortDescription { get; set; } = shortDescription;

    /// <summary>
    /// A long description of the project, suitable for an event summary.
    /// </summary>
    public string LongDescription { get; set; } = longDescription;

    /// <summary>
    /// The team behind the project.
    /// </summary>
    public ISet<Participant> Team { get; } = new HashSet<Participant>();

    /// <summary>
    /// The participants invited to join the project.
    /// </summary>
    public ISet<Participant> InvitedParticipants { get; } = new HashSet<Participant>();

    /// <summary>
    /// A link to the project's home page, such as a source code repository, if any.
    /// </summary>
    public string Link { get; set; } = link;

    /// <summary>
    /// The ID of a thumbnail picture associated with the project, if any.
    /// </summary>
    public string ThumbnailId { get; set; } = thumbnailId;

    /// <summary>
    /// Challenges the project has opted into.
    /// </summary>
    public string[] Challenges { get; set; } = [];
}