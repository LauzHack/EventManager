namespace EventManager.Models;

/// <summary>
/// Theming information for the event.
/// </summary>
/// <param name="BackgroundColor">The main background color of the event.</param>
/// <param name="ForegroundColor">The main foreground color of the event.</param>
/// <param name="LogoFileId">The ID of the uploaded file for the event logo.</param>
/// <param name="IconFileId">The ID of the uploaded file for the event icon.</param>
/// <param name="IconMimeType">The MIME type of the uploaded file for the event icon, cached for efficiency.</param>
public sealed record EventTheme(RgbColor BackgroundColor, RgbColor ForegroundColor, string LogoFileId, string IconFileId, string IconMimeType);