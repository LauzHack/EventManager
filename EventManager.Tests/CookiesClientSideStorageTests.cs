using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using EventManager.Web;

using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests;

[TestClass]
public sealed class CookiesClientSideStorageTests
{
    [TestMethod]
    public void EmptyByDefault()
    {
        var context = new DefaultHttpContext();
        var storage = new CookiesClientSideStorage(context);

        Assert.IsFalse(storage.TryGet("X", out _));
    }

    [TestMethod]
    public void GetReturnsSetValue()
    {
        var context = new DefaultHttpContext();
        var storage = new CookiesClientSideStorage(context);

        storage.Set("X", "Y");
        Assert.IsTrue(storage.TryGet("X", out var retrieved));
        Assert.AreEqual("Y", retrieved);
    }

    [TestMethod]
    public void GetReturnsValueFromCookies()
    {
        var context = new DefaultHttpContext();
        var cookies = new FakeRequestCookiesCollection();
        cookies.Values["X"] = "Y";
        context.Request.Cookies = cookies;
        var storage = new CookiesClientSideStorage(context);

        Assert.IsTrue(storage.TryGet("X", out var retrieved));
        Assert.AreEqual("Y", retrieved);
    }

    [TestMethod]
    public void GetReturnsValueFromSetEvenIfAlsoInCookies()
    {
        var context = new DefaultHttpContext();
        var cookies = new FakeRequestCookiesCollection();
        cookies.Values["X"] = "Y";
        context.Request.Cookies = cookies;
        var storage = new CookiesClientSideStorage(context);

        storage.Set("X", "Z");
        Assert.IsTrue(storage.TryGet("X", out var retrieved));
        Assert.AreEqual("Z", retrieved);
    }

    [TestMethod]
    public void SetAlsoSetsResponseCookies()
    {
        var context = new DefaultHttpContext();
        var storage = new CookiesClientSideStorage(context);

        storage.Set("X", "Y");

        var cookies = context.Response.GetTypedHeaders().SetCookie;
        var cookie = cookies.Single(c => c.Name == "X");
        Assert.IsNotNull(cookie);
        Assert.AreEqual("Y", cookie.Value);
    }

    private sealed class FakeRequestCookiesCollection : IRequestCookieCollection
    {
        public Dictionary<string, string> Values { get; } = [];

        public string? this[string key]
            => Values[key];

        public int Count
            => Values.Count;

        public ICollection<string> Keys
            => Values.Keys;

        public bool ContainsKey(string key)
            => Values.ContainsKey(key);

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
            => Values.GetEnumerator();

        public bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            => Values.TryGetValue(key, out value);

        IEnumerator IEnumerable.GetEnumerator()
            => Values.GetEnumerator();
    }
}