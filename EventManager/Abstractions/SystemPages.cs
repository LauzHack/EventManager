using System;
using System.Collections.Generic;

namespace EventManager.Abstractions;

/// <summary>
/// Holds all page types in the system.
/// </summary>
public sealed class SystemPages(IReadOnlyDictionary<Type, IReadOnlyCollection<Type>> pageTypesPerUserType)
{
    /// <summary>
    /// Gets all page types, in order, for the given user type.
    /// </summary>
    public IReadOnlyCollection<Type> GetPageTypes(Type userType)
        => pageTypesPerUserType[userType];
}