using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.Abstractions;

[TestClass]
public sealed class OperationArgumentsTests
{
    private readonly Example _target = new();

    [TestInitialize]
    public void Initialize()
    {
        _target.Arguments = [];
    }

    [TestMethod]
    public void EqualWhenBothHaveSameContents()
    {
        var file = new File.InMemory("name", "some/mimetype", [0, 1, 2]);
        var op = OperationArguments.Empty
            .WithText("hello", "world")
            .WithText("hello", "!")
            .WithFile("file", file);
        var op2 = OperationArguments.Empty
            .WithText("hello", "world")
            .WithText("hello", "!")
            .WithFile("file", file);
        Assert.IsTrue(op.Equals(op2));
        Assert.AreEqual(op.GetHashCode(), op2.GetHashCode());
        Assert.IsTrue(op.Equals((object)op2));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void UnequalWhenTextDiffers(bool sameKey, bool sameLength)
    {
        var file = new File.InMemory("name", "some/mimetype", [0, 1, 2]);
        var op = OperationArguments.Empty
            .WithText(sameKey ? "hello" : "other", "world");
        if (sameLength)
        {
            op = op.WithText(sameKey ? "hello" : "other", "...");
        }
        op = op.WithFile("file", file);
        var op2 = OperationArguments.Empty
            .WithText("hello", "world")
            .WithText("hello", "!")
            .WithFile("file", file);
        Assert.IsFalse(op.Equals(op2));
        Assert.IsFalse(OperationArguments.Empty.Equals(op2));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnequalWhenFilesDiffers(bool sameKey)
    {
        var file = new File.InMemory("name", "some/mimetype", [0, 1, 2]);
        var op = OperationArguments.Empty
            .WithText("hello", "world")
            .WithText("hello", "!")
            .WithFile("file", file);
        var file2 = new File.InMemory("name", "some/mimetype2", [0, 1, 2]);
        var op2 = OperationArguments.Empty
            .WithText("hello", "world")
            .WithText("hello", "!")
            .WithFile(sameKey ? "file" : "otherfile", file2);
        Assert.IsFalse(op.Equals(op2));
        Assert.IsFalse(OperationArguments.Empty.Equals(op2));
    }

    [TestMethod]
    public void UnequalWhenOtherIsNull()
    {
        Assert.IsFalse(OperationArguments.Empty.Equals(null));
        Assert.IsFalse(OperationArguments.Empty.Equals((object?)null));
    }

    [TestMethod]
    [DataRow("abc", "def")]
    [DataRow("abc", "d/f")]
    public void TextualValuesRoundtripWithUri(string key, string value)
    {
        var args = OperationArguments.FromPairs((key, value));

        args = args.WithText("ghi", "jkl");

        var uri = args.AppendTextValuesToUri(new Uri("https://example.org/some/where"));

        var roundtripped = OperationArguments.FromUri(uri);

        Assert.IsTrue(roundtripped.TryGetText(key, out var first));
        Assert.AreEqual(value, first);
        Assert.IsTrue(roundtripped.TryGetText("ghi", out var second));
        Assert.AreEqual("jkl", second);
    }

    [TestMethod]
    public void TryGetTextIgnoresEmptyValues()
    {
        var args = OperationArguments.Empty
                                     .WithText("x", "y")
                                     .WithText("x", "")
                                     .WithText("abc", "");

        Assert.IsTrue(args.TryGetText("x", out var first));
        Assert.AreEqual("y", first);
        Assert.IsFalse(args.TryGetText("abc", out _));
    }

    [TestMethod]
    public void CreateFromPairs()
    {
        var args = OperationArguments.FromPairs(("x", "y"), ("abc", "123"));

        Assert.IsTrue(args.TryGetText("x", out var first));
        Assert.AreEqual("y", first);
        Assert.IsTrue(args.TryGetText("abc", out var second));
        Assert.AreEqual("123", second);
    }

    [TestMethod]
    public void CreateFromPairsIgnoresEmptyValues()
    {
        var args = OperationArguments.FromPairs(("x", "y"), ("abc", ""), ("x", ""));

        Assert.IsTrue(args.TryGetText("x", out var first));
        Assert.AreEqual("y", first);
        Assert.IsFalse(args.TryGetText("abc", out _));
    }

    [TestMethod]
    public void CreateFromPairsWithMultipleValuesPerKey()
    {
        var args = OperationArguments.FromPairs(("a", "X"), ("b", "Y"), ("a", "Z"));

        Assert.IsTrue(args.TryGetText("b", out var y));
        Assert.AreEqual("Y", y);
        Assert.Throws<InvalidOperationException>(() => args.TryGetText("a", out var _));
    }

    [TestMethod]
    public void Combine()
    {
        var args = OperationArguments.FromPairs(("x", "y")) + OperationArguments.FromPairs(("abc", "123"));

        Assert.IsTrue(args.TryGetText("x", out var first));
        Assert.AreEqual("y", first);
        Assert.IsTrue(args.TryGetText("abc", out var second));
        Assert.AreEqual("123", second);
    }

    [TestMethod]
    public void CombineWithMultipleValuesPerKey()
    {
        var args = OperationArguments.FromPairs(("a", "X"), ("b", "Y")) + OperationArguments.FromPairs(("a", "Z"));

        Assert.IsTrue(args.TryGetText("b", out var y));
        Assert.AreEqual("Y", y);
        Assert.Throws<InvalidOperationException>(() => args.TryGetText("a", out var _));
    }

    [TestMethod]
    [DataRow(nameof(Example.GiveString), "hello", "hello")]
    [DataRow(nameof(Example.GiveString), "  hello", "hello")]
    [DataRow(nameof(Example.GiveString), "hello\t", "hello")]
    [DataRow(nameof(Example.GiveString), " \nhello  ", "hello")]
    [DataRow(nameof(Example.GiveBool), "true", true)]
    [DataRow(nameof(Example.GiveInt), "123", 123)]
    [DataRow(nameof(Example.GiveEnum), "Yes", ExampleEnum.Yes)]
    [DataRow(nameof(Example.GiveEnum), "No", ExampleEnum.No)]
    [DataRow(nameof(Example.GiveOptionalString), "hello", "hello")]
    [DataRow(nameof(Example.GiveOptionalString), "  hello", "hello")]
    [DataRow(nameof(Example.GiveOptionalString), "hello  ", "hello")]
    [DataRow(nameof(Example.GiveOptionalString), "  hello  ", "hello")]
    [DataRow(nameof(Example.GiveOptionalString), "", null)]
    [DataRow(nameof(Example.GiveOptionalString), "  ", null)]
    [DataRow(nameof(Example.GiveOptionalString), " \t ", null)]
    [DataRow(nameof(Example.GiveOptionalBool), "true", true)]
    [DataRow(nameof(Example.GiveOptionalInt), "123", 123)]
    public void ExtractPrimitive(string methodName, string arg, object? expectedResult)
    {
        var method = typeof(Example).GetRequiredMethod(methodName);
        var args = OperationArguments.FromPairs(("value", arg));

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        Assert.AreSequenceEqual([expectedResult], _target.Arguments);
    }

    [TestMethod]
    public void ExtractUri()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveUri));
        var args = OperationArguments.FromPairs(("uri", "smtp://example.org:587"));

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        var single = Assert.ContainsSingle(_target.Arguments);
        Assert.AreEqual(new Uri("smtp://example.org:587", UriKind.Absolute), single);
    }

    [TestMethod]
    public void ExtractDateTime()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveDateTime));
        var args = OperationArguments.FromPairs(("dateTime", "2024-06-01T08:30")); // example from the MDN doc for input=datetime-local

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        var single = Assert.ContainsSingle(_target.Arguments);
        Assert.AreEqual(new DateTimeOffset(2024, 6, 1, 8, 30, 0, TimeSpan.Zero), single);
    }

    [TestMethod]
    [DataRow(nameof(Example.GiveFile))]
    [DataRow(nameof(Example.GiveOptionalFile))]
    public void ExtractFile(string methodName)
    {
        var method = typeof(Example).GetRequiredMethod(methodName);
        var file = new File.InMemory("name", "text/plain", [0]);
        var args = FileArguments(("file", file));

        var result = args.Invoke<string>(_target, method, null);

        Assert.AreEqual(file.MimeType, result);
        Assert.AreSequenceEqual([file], _target.Arguments);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(3)]
    public void ExtractArrayOfInts(int count)
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveArrayOfInts));
        var array = count <= 0 ? [] : Enumerable.Range(0, count).Select(n => n * 1000).ToArray();
        var args = count <= 0 ? OperationArguments.Empty : FromMultiPairs(("array", array.Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray()));

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        var arg = Assert.IsInstanceOfType<int[]>(_target.Arguments[0]);
        Assert.AreSequenceEqual(array, arg);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(3)]
    public void ExtractImmutableArrayOfInts(int count)
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveImmutableArrayOfInts));
        var array = count <= 0 ? [] : Enumerable.Range(0, count).Select(n => n * 1000).ToImmutableArray();
        var args = count <= 0 ? OperationArguments.Empty : FromMultiPairs(("array", array.Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray()));

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        var arg = Assert.IsInstanceOfType<ImmutableArray<int>>(_target.Arguments[0]);
        Assert.AreSequenceEqual(array, arg);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(3)]
    public void ExtractImmutableDictionary(int count)
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveImmutableDictionaryOfStringsToInts));
        var keys = Enumerable.Range(0, count).Select(n => "Key " + n.ToString(CultureInfo.InvariantCulture)).ToArray();
        var values = Enumerable.Range(0, count).Select(n => n * 1000).ToArray();
        var args = keys.Length == 0 ? OperationArguments.Empty
                                    : FromMultiPairs(("dict.Key", keys), ("dict.Value", values.Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray()));

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        var arg = Assert.IsInstanceOfType<ImmutableDictionary<string, int>>(_target.Arguments[0]);
        Assert.HasCount(count, arg);
        for (int n = 0; n < count; n++)
        {
            Assert.AreEqual(n * 1000, arg["Key " + n.ToString(CultureInfo.InvariantCulture)]);
        }
    }

    [TestMethod]
    [DataRow(1, 2)]
    [DataRow(2, 1)]
    [DataRow(0, 2)]
    [DataRow(2, 0)]
    public void InvalidOperationExceptionOnMismatchedKeyValueCountForImmutableDictionary(int keyCount, int valueCount)
    {
        var keys = Enumerable.Range(0, keyCount).Select(n => "Key " + n.ToString(CultureInfo.InvariantCulture)).ToArray();
        var values = Enumerable.Range(0, valueCount).Select(n => n * 1000).ToArray();
        var args = OperationArguments.Empty;
        if (keyCount > 0)
        {
            args += FromMultiPairs(("dict.Key", keys));
        }
        if (valueCount > 0)
        {
            args += FromMultiPairs(("dict.Value", values.Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray()));
        }

        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveImmutableDictionaryOfStringsToInts));
        Assert.Throws<InvalidOperationException>(() => args.Invoke<int>(_target, method, null));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(3)]
    public void ExtractArrayOfStrings(int count)
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveArrayOfStrings));
        var array = Enumerable.Range(0, count).Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray();
        var args = FromMultiPairs(("array", array));

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        var typedArg = Assert.IsInstanceOfType<string[]>(_target.Arguments[0]);
        Assert.AreSequenceEqual(array, typedArg);
    }

    [TestMethod]
    public void ExtractArrayOfStringsFromSingleStringUsingLines()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveArrayOfStrings));
        var args = OperationArguments.FromPairs(("array-all", "a\nbee\r\nc,d\re \n f g \n"));

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        Assert.IsInstanceOfType<string[]>(_target.Arguments[0]);
        Assert.AreSequenceEqual(["a", "bee", "c,d", "e", "f g"], (string[])_target.Arguments[0]!);
    }

    [TestMethod]
    public void ExtractArrayOfStringsIgnoresEmptyAndWhitespace()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveArrayOfStrings));
        var args = FromMultiPairs(("array", ["a", "", "  \n", "b", "  \t"]));

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        var typedArg = Assert.IsInstanceOfType<string[]>(_target.Arguments[0]);
        Assert.AreSequenceEqual(["a", "b"], typedArg);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExtractRecordComponents(bool optional)
    {
        var method = typeof(Example).GetRequiredMethod(optional ? nameof(Example.GiveOptionalRecord) : nameof(Example.GiveRecord));
        var args = OperationArguments.FromPairs(("record.Enum", "Yes"), ("record.Int", "123"), ("record.String", "abc"));

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        var rec = Assert.ContainsSingle(_target.Arguments);
        Assert.AreEqual(new ExampleRecord(ExampleEnum.Yes, 123, "abc"), rec);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExtractRecordWithOptionalComponent(bool optional)
    {
        var method = typeof(Example).GetRequiredMethod(optional ? nameof(Example.GiveOptionalRecord) : nameof(Example.GiveRecord));
        var args = OperationArguments.FromPairs(("record.Enum", "Yes"), ("record.String", "abc"));

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        var rec = Assert.ContainsSingle(_target.Arguments);
        Assert.AreEqual(new ExampleRecord(ExampleEnum.Yes, null, "abc"), rec);
    }

    [TestMethod]
    public void IgnorePartiallyMissingOptionalRecord()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveOptionalRecord));
        var args = OperationArguments.FromPairs(("record.Enum", "Yes"), ("record.Int", "123"));

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        var arg = Assert.ContainsSingle(_target.Arguments);
        Assert.IsNull(arg);
    }

    [TestMethod]
    public void ExtractRecordComponentsIncludingEmptyArray()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveRecordWithArray));
        var args = OperationArguments.FromPairs(("record.Name", "XXX"), ("record.Boolean", "false"));

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        var rec = Assert.ContainsSingle(_target.Arguments);
        var typedRec = Assert.IsInstanceOfType<RecordWithArray>(rec);
        Assert.AreEqual("XXX", typedRec.Name);
        Assert.IsFalse(typedRec.Boolean);
        Assert.IsEmpty(typedRec.Items);
    }

    [TestMethod]
    public void ExtractRecordComponentsIncludingEmptyArrayNested()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveRecordWithArrayNested));
        var args = OperationArguments.FromPairs(("record.Records.Name", "XXX"), ("record.Records.Boolean", "false"));

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        var rec = Assert.ContainsSingle(_target.Arguments);
        var typedRec = Assert.IsInstanceOfType<RecordWithArrayNested>(rec);
        var single = Assert.ContainsSingle(typedRec.Records);
        Assert.AreEqual("XXX", single.Name);
        Assert.IsFalse(single.Boolean);
        Assert.IsEmpty(single.Items);
    }

    [TestMethod]
    public void ExtractArrayOfRecordsWithArrayUsingSingleValueStringArray()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveArrayOfRecordsWithArray));
        var args = OperationArguments.FromPairs(
            ("records.Name", "FirstName"),
            ("records.Boolean", "false"),
            ("records.Items", ""),
            ("records.Name", "Second name "),
            ("records.Boolean", "true"),
            ("records.Items", " D\nE \n")
        );

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        var arr = Assert.ContainsSingle(_target.Arguments);
        var typedArr = Assert.IsInstanceOfType<RecordWithArray[]>(arr);
        Assert.HasCount(2, typedArr);
        Assert.AreEqual("FirstName", typedArr[0].Name);
        Assert.IsFalse(typedArr[0].Boolean);
        Assert.IsEmpty(typedArr[0].Items);
        Assert.AreEqual("Second name", typedArr[1].Name);
        Assert.IsTrue(typedArr[1].Boolean);
        Assert.AreSequenceEqual(["D", "E"], typedArr[1].Items);
    }

    [TestMethod]
    public void ExtractMissingOptionalRecord()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveOptionalRecord));

        var result = OperationArguments.Empty.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        var rec = Assert.ContainsSingle(_target.Arguments);
        Assert.IsNull(rec);
    }

    [TestMethod]
    public void ExtractComplexRecord()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveComplexRecord));
        var args = OperationArguments.FromPairs(
            ("record.Name", "Overall name  "),
            ("record.Nested.Enum", "Yes"),
            ("record.Nested.Int", "42"),
            ("record.Nested.String", "hello "),
            ("record.Nested.Enum", "No"),
            ("record.Nested.Int", "-123"),
            ("record.Nested.String", " world!")
        );

        var result = args.Invoke<int>(_target, method, null);
        Assert.AreEqual(42, result);
        var rec = Assert.IsInstanceOfType<ComplexRecord>(Assert.ContainsSingle(_target.Arguments));
        Assert.AreEqual("Overall name", rec.Name);
        Assert.HasCount(2, rec.Nested);
        Assert.AreEqual(ExampleEnum.Yes, rec.Nested[0].Enum);
        Assert.AreEqual(42, rec.Nested[0].Int);
        Assert.AreEqual("hello", rec.Nested[0].String);
        Assert.AreEqual(ExampleEnum.No, rec.Nested[1].Enum);
        Assert.AreEqual(-123, rec.Nested[1].Int);
        Assert.AreEqual("world!", rec.Nested[1].String);
    }

    [TestMethod]
    public void UseUser()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveFakeUser));
        var user = new FakeUser();

        var result = OperationArguments.Empty.Invoke<int>(_target, method, user);

        Assert.AreEqual(42, result);
        Assert.AreSequenceEqual([user], _target.Arguments);
    }

    [TestMethod]
    public void UseNullUserWhenOptionalNotProvided()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveOptionalFakeUser));

        var result = OperationArguments.Empty.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        Assert.AreSequenceEqual([null], _target.Arguments);
    }

    [TestMethod]
    public void ThrowAuthenticationRequiredExceptionWhenUserIsRequiredAndNotProvided()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveFakeUser));

        Assert.ThrowsExactly<AuthenticationRequiredException>(() => OperationArguments.Empty.Invoke<int>(_target, method, null));
    }

    [TestMethod]
    public void ThrowAuthenticationRequiredExceptionWhenUserIsRequiredAndNotTheTypeOfProvided()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveFakeUser2));

        Assert.ThrowsExactly<AuthenticationRequiredException>(() => OperationArguments.Empty.Invoke<int>(_target, method, null));
    }

    [TestMethod]
    public void PassThroughOperationArguments()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveOperationArguments));
        var args = OperationArguments.FromPairs(("hello", "world"));

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(123, result);
        Assert.AreSame(args, _target.Arguments[0]);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("  \t\n ")]
    public void IgnoreEmptyOrWhitespaceStrings(string value)
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveOptionalString));
        var args = OperationArguments.FromPairs(("value", value));

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        Assert.AreSequenceEqual([null], _target.Arguments);
    }

    [TestMethod]
    [DataRow(nameof(Example.GiveOptionalString))]
    [DataRow(nameof(Example.GiveOptionalBool))]
    [DataRow(nameof(Example.GiveOptionalInt))]
    public void IgnoreMissingOptionalPrimitiveArgument(string methodName)
    {
        var method = typeof(Example).GetRequiredMethod(methodName);
        var args = OperationArguments.FromPairs();

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        Assert.AreSequenceEqual([null], _target.Arguments);
    }

    [TestMethod]
    public void IgnoreMissingOptionalFileArgument()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveOptionalFile));
        var args = FileArguments();

        var result = args.Invoke<string>(_target, method, null);

        Assert.AreEqual("nope", result);
        Assert.AreSequenceEqual([null], _target.Arguments);
    }

    [TestMethod]
    public void ExtractOneRequiredAndOneOptionalAndIgnoreOneMissing()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveMultipleArguments));
        var file = new File.InMemory("name", "text/plain", [0]);
        var args = OperationArguments.Empty
            .WithText("integer", "11")
            .WithText("boolean", "false")
            .WithFile("file", file);

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        Assert.AreSequenceEqual([11, file, false], _target.Arguments);
    }

    [TestMethod]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Required for this test")]
    public void ExtractingArgumentUsesInvariantCulture()
    {
        object?[] result = [];

        var thread = new Thread(() =>
        {
            try
            {
                var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveDouble));
                var args = OperationArguments.FromPairs(("value", "6.25"));

                args.Invoke<int>(_target, method, null);

                result = _target.Arguments;
            }
            catch
            {
                // An unhandled exception on the thread will cause the test runner to crash,
                // so we catch it and manually fail the test as result will be null when we check it outside of the thread
            }
        })
        {
            CurrentCulture = new CultureInfo("fr-FR") // decimals come after a comma, not period, in French
        };

        thread.Start();
        thread.Join();
        Assert.AreSequenceEqual([6.25], result);
    }

    [TestMethod]
    public void IgnoreIrrelevantArguments()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveString));
        var args = OperationArguments.FromPairs(("value", "xyz"), ("otherValue", "abc"));

        var result = args.Invoke<int>(_target, method, null);

        Assert.AreEqual(42, result);
        Assert.AreSequenceEqual(["xyz"], _target.Arguments);
    }

    [TestMethod]
    [DataRow(nameof(Example.GiveString))]
    [DataRow(nameof(Example.GiveInt))]
    [DataRow(nameof(Example.GiveEnum))]
    public void InvalidOperationExceptionWhenRequiredPrimitiveIsMissing(string methodName)
    {
        var method = typeof(Example).GetRequiredMethod(methodName);
        var args = OperationArguments.FromPairs();

        Assert.ThrowsExactly<InvalidOperationException>(() => args.Invoke<int>(_target, method, null));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("  \t\n ")]
    public void InvalidOperationExceptionWhenRequiredStringIsEmptyOrWhitespace(string value)
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveString));
        var args = OperationArguments.FromPairs(("value", value));

        Assert.ThrowsExactly<InvalidOperationException>(() => args.Invoke<int>(_target, method, null));
    }

    [TestMethod]
    public void InvalidOperationExceptionWhenUriHasBadFormat()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveUri));
        var args = OperationArguments.FromPairs(("uri", "xyzzy"));

        Assert.ThrowsExactly<InvalidOperationException>(() => args.Invoke<int>(_target, method, null));
    }

    [TestMethod]
    public void InvalidOperationExceptionWhenDateTimeHasBadFormat()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveDateTime));
        var args = OperationArguments.FromPairs(("dateTime", "2024-06-01 08:30:10"));

        Assert.ThrowsExactly<InvalidOperationException>(() => args.Invoke<int>(_target, method, null));
    }

    [TestMethod]
    public void InvalidOperationExceptionWhenRequiredFileIsMissing()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveFile));
        var args = FileArguments();

        Assert.ThrowsExactly<InvalidOperationException>(() => args.Invoke<string>(_target, method, null));
    }

    [TestMethod]
    public void InvalidOperationExceptionWhenRequiredStringIsMissingEvenWithIrrelevantArguments()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveString));
        var args = OperationArguments.FromPairs(("other", "abc"));

        Assert.ThrowsExactly<InvalidOperationException>(() => args.Invoke<int>(_target, method, null));
    }

    [TestMethod]
    [DataRow(nameof(Example.GiveInt), "x")]
    [DataRow(nameof(Example.GiveBool), "0")]
    [DataRow(nameof(Example.GiveEnum), "Maybe")]
    public void InvalidOperationExceptionOnArgumentParsingFailure(string methodName, string arg)
    {
        var method = typeof(Example).GetRequiredMethod(methodName);
        var args = OperationArguments.FromPairs(("value", arg));

        Assert.ThrowsExactly<InvalidOperationException>(() => args.Invoke<int>(_target, method, null));
    }

    [TestMethod]
    public void InvalidOperationExceptionOnPartiallyMissingRecord()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveRecord));
        var args = OperationArguments.FromPairs(("record.Enum", "Yes"), ("record.Int", "123"));

        Assert.Throws<InvalidOperationException>(() => args.Invoke<int>(_target, method, null));
    }

    [TestMethod]
    public void InvalidOperationExceptionOnPartiallyMissingRecordDueToEmptyValue()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveRecord));
        var args = OperationArguments.FromPairs(("record.Enum", "Yes"), ("record.Int", "123"), ("record.String", ""));

        Assert.Throws<InvalidOperationException>(() => args.Invoke<int>(_target, method, null));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InvalidOperationExceptionWhenRequiredRecordIsMissing(bool partial)
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveRecord));
        var args = partial ? OperationArguments.FromPairs(("record.Enum", "Yes"), ("record.Int", "123")) : OperationArguments.Empty;

        Assert.ThrowsExactly<InvalidOperationException>(() => args.Invoke<int>(_target, method, null));
    }

    [TestMethod]
    public void InvalidOperationExceptionWhenRecordHasMultipleConstructors()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveMultiConstructorRecord));
        var args = OperationArguments.FromPairs(("record.Name", "XYZ"));

        Assert.ThrowsExactly<InvalidOperationException>(() => args.Invoke<int>(_target, method, null));
    }

    [TestMethod]
    public void ArgumentExceptionWhenArgumentIsUnparseable()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveUnparseable));
        var args = OperationArguments.FromPairs(("arg", "XYZ"));

        Assert.ThrowsExactly<ArgumentException>(() => args.Invoke<int>(_target, method, null));
    }

    [TestMethod]
    public void ArgumentExceptionOnMismatchedTargetType()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveString));
        var args = OperationArguments.FromPairs(("value", "abc"));

        Assert.ThrowsExactly<ArgumentException>(() => args.Invoke<int>(new object(), method, null));
    }

    [TestMethod]
    public void ArgumentExceptionOnMismatchedReturnType()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.GiveString));
        var args = OperationArguments.FromPairs(("value", "abc"));

        Assert.ThrowsExactly<ArgumentException>(() => args.Invoke<string>(_target, method, null));
    }

    [TestMethod]
    public void ArgumentExceptionOnNullReturnValue()
    {
        var method = typeof(Example).GetRequiredMethod(nameof(Example.ReturnNull));
        var args = OperationArguments.FromPairs();

        Assert.ThrowsExactly<ArgumentException>(() => args.Invoke<object>(_target, method, null));
    }

    private sealed class FakeUser() : User
    {
        public override string Id => "fake@example.org";
    }

    private sealed class FakeUser2() : User
    {
        public override string Id => "fake2@example.org";
    }

    private static OperationArguments FileArguments(params (string, File)[] args)
        => args.Aggregate(OperationArguments.Empty, (o, p) => o.WithFile(p.Item1, p.Item2));

    private static OperationArguments FromMultiPairs(params (string, string[])[] args)
        => args.Aggregate(OperationArguments.Empty, (o, p) => p.Item2.Aggregate(o, (o2, v) => o2.WithText(p.Item1, v)));

    private enum ExampleEnum { No = 0, Yes = 1 }

    private sealed record ExampleRecord(ExampleEnum Enum, int? Int, string String);

    private sealed record RecordWithArray(string Name, bool Boolean, ImmutableArray<string> Items);

    private sealed record RecordWithArrayNested(ImmutableArray<RecordWithArray> Records);

    private sealed record ComplexRecord(string Name, ImmutableArray<ExampleRecord> Nested);

    private sealed record MultiConstructorRecord(string Name)
    {
        public MultiConstructorRecord(string name, bool name2) : this(name + name2) { }
    }

    private sealed class NotParseable;

    private sealed class Example
    {
        public object?[] Arguments { get; set; } = [];

        private void SetArguments(object?[] args)
        {
            if (Arguments.Length != 0)
            {
                throw new InvalidOperationException("Already called");
            }
            Arguments = args;
        }

        public int GiveString(string value)
        {
            SetArguments([value]);
            return 42;
        }

        public int GiveBool(bool value)
        {
            SetArguments([value]);
            return 42;
        }

        public int GiveInt(int value)
        {
            SetArguments([value]);
            return 42;
        }

        public int GiveEnum(ExampleEnum value)
        {
            SetArguments([value]);
            return 42;
        }

        public int GiveDateTime(DateTimeOffset dateTime)
        {
            SetArguments([dateTime]);
            return 42;
        }

        public int GiveUri(Uri uri)
        {
            SetArguments([uri]);
            return 42;
        }

        public string GiveFile(File file)
        {
            SetArguments([file]);
            return file.MimeType;
        }

        public int GiveDouble(double value)
        {
            SetArguments([value]);
            return 42;
        }

        public int GiveOptionalString(string? value)
        {
            SetArguments([value]);
            return 42;
        }

        public int GiveOptionalBool(bool? value)
        {
            SetArguments([value]);
            return 42;
        }

        public int GiveOptionalInt(int? value)
        {
            SetArguments([value]);
            return 42;
        }

        public string GiveOptionalFile(File? file)
        {
            SetArguments([file]);
            return file?.MimeType ?? "nope";
        }

        public int GiveOperationArguments(OperationArguments args)
        {
            SetArguments([args]);
            return 123;
        }

        public int GiveMultipleArguments(int integer, File? file, bool? boolean)
        {
            SetArguments([integer, file, boolean]);
            return 42;
        }

        public int GiveArrayOfInts(int[] array)
        {
            SetArguments([array]);
            return 42;
        }

        public int GiveImmutableArrayOfInts(ImmutableArray<int> array)
        {
            SetArguments([array]);
            return 42;
        }

        public int GiveImmutableDictionaryOfStringsToInts(ImmutableDictionary<string, int> dict)
        {
            SetArguments([dict]);
            return 42;
        }

        public int GiveArrayOfStrings(string[] array)
        {
            SetArguments([array]);
            return 42;
        }

        public int GiveFakeUser(FakeUser user)
        {
            SetArguments([user]);
            return 42;
        }

        public int GiveFakeUser2(FakeUser2 user)
        {
            throw new InvalidOperationException("Not intended to be called");
        }

        public int GiveOptionalFakeUser(FakeUser? user)
        {
            SetArguments([user]);
            return 42;
        }

        public int GiveRecord(ExampleRecord record)
        {
            SetArguments([record]);
            return 42;
        }

        public int GiveRecordWithArray(RecordWithArray record)
        {
            SetArguments([record]);
            return 42;
        }

        public int GiveRecordWithArrayNested(RecordWithArrayNested record)
        {
            SetArguments([record]);
            return 42;
        }

        public int GiveArrayOfRecordsWithArray(RecordWithArray[] records)
        {
            SetArguments([records]);
            return 42;
        }

        public int GiveOptionalRecord(ExampleRecord? record)
        {
            SetArguments([record]);
            return 42;
        }

        public int GiveComplexRecord(ComplexRecord record)
        {
            SetArguments([record]);
            return 42;
        }

        public int GiveMultiConstructorRecord(MultiConstructorRecord record)
        {
            throw new InvalidOperationException("Not intended to be called");
        }

        public int GiveUnparseable(NotParseable arg)
        {
            throw new InvalidOperationException("Not intended to be called");
        }

        public object? ReturnNull()
        {
            SetArguments([]);
            return null;
        }
    }
}