using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using EventManager.Abstractions;

namespace EventManager.Tests.TestInfrastructure;

public sealed class FakeClientSideStorage : ClientSideStorage
{
    public Dictionary<string, string> Values { get; } = [];

    public override bool TryGet(string key, [MaybeNullWhen(false)] out string value)
        => Values.TryGetValue(key, out value);

    public override void Set(string key, string value)
        => Values[key] = value;
}