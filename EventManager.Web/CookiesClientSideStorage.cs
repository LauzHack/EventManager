using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using EventManager.Abstractions;

using Microsoft.AspNetCore.Http;

namespace EventManager.Web;

public sealed class CookiesClientSideStorage(HttpContext httpContext) : ClientSideStorage
{
    // so that set-then-get works, since we're using different cookie collections
    private readonly Dictionary<string, string> _current = new(StringComparer.Ordinal);

    public override bool TryGet(string key, [MaybeNullWhen(false)] out string value)
        => _current.TryGetValue(key, out value) // order is important, set takes priority over some old value!
        || httpContext.Request.Cookies.TryGetValue(key, out value);

    public override void Set(string key, string value)
    {
        _current[key] = value;
        httpContext.Response.Cookies.Append(key, value);
    }
}