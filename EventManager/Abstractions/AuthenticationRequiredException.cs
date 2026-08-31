using System;

namespace EventManager.Abstractions;

/// <summary>
/// Thrown to indicate the end user is not logged in but needs to be in order to perform the operation they requested.
/// </summary>
public sealed class AuthenticationRequiredException : Exception;