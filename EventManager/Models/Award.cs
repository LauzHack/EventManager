namespace EventManager.Models;

/// <summary>
/// Award given to a project by a challenge setter.
/// </summary>
/// <param name="Order">Order of the award, within the owning challenge setter's awards.</param>
/// <param name="Name">Name of the award.</param>
/// <param name="Project">ID of the project that received the award.</param>
/// <remarks>
/// It is possible although not expected that the project gets deleted after the award is created. In this case, the award is ignored.
/// </remarks>
public sealed record Award(int Order, string Name, string ProjectId);