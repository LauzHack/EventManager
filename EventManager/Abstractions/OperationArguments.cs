using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Web;

using EventManager.Models;

namespace EventManager.Abstractions;

/// <summary>
/// Arguments sent by the user to the system.
/// This is a low-level abstraction necessary when dealing with dynamic user input,
/// as opposed to values whose type and name is known in advance.
/// </summary>
/// <remarks>
/// This class does not expose its contents directly, so that it is not possible to accidentally leak
/// values other than the ones a class knows the keys of.
/// (It does allow dumping them to a URI, but that takes effort beyond "oops, I printed all the parameters somewhere")
/// </remarks>
public sealed class OperationArguments : IEquatable<OperationArguments>
{
    private readonly ImmutableDictionary<string, ImmutableArray<string>> _text;
    private readonly ImmutableDictionary<string, File> _files;

    private OperationArguments(ImmutableDictionary<string, ImmutableArray<string>> text, ImmutableDictionary<string, File> files)
    {
        _text = text;
        _files = files;
    }

    /// <summary>
    /// Empty arguments.
    /// </summary>
    public static OperationArguments Empty { get; } = new([], []);

    /// <summary>
    /// Whether these arguments contain any files with no content.
    /// </summary>
    public bool HasEmptyFiles
        => _files.Any(p => p.Value.Length == 0);

    /// <summary>
    /// Whether these arguments contain any files above the usual limit.
    /// </summary>
    public bool HasOversizedFiles
        => _files.Any(p => p.Value.Length > File.MaxSizeInBytes);

    /// <summary>
    /// Attempts to get a textual value with the given key.
    /// </summary>
    public bool TryGetText(string key, [MaybeNullWhen(false)] out string value)
    {
        value = null;
        if (_text.TryGetValue(key, out var values))
        {
            var relevant = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(s => s.Trim()).ToArray();
            if (relevant.Length == 0)
            {
                return false;
            }
            if (relevant.Length != 1)
            {
                throw new InvalidOperationException("Cannot get a single text value when there are != 1 in the corresponding array.");
            }
            value = relevant[0];
            return true;
        }
        return false;
    }

    /// <summary>
    /// Attempts to get a file with the given key.
    /// </summary>
    public bool TryGetFile(string key, [MaybeNullWhen(false)] out File value)
        => _files.TryGetValue(key, out value);

    /// <summary>
    /// Creates a new set of arguments that includes these arguments and the given textual key-value pair.
    /// </summary>
    public OperationArguments WithText(string key, string value)
    {
        if (_text.TryGetValue(key, out var values))
        {
            return new(_text.SetItem(key, [.. values, value]), _files);
        }
        return new(_text.Add(key, [value]), _files);
    }

    /// <summary>
    /// Creates a new set of arguments that includes these arguments and the given file argument.
    /// </summary>
    public OperationArguments WithFile(string key, File value)
        => new(_text, _files.SetItem(key, value));

    /// <summary>
    /// Creates a URI with the textual arguments appended to the given relative URI.
    /// </summary>
    public Uri AppendTextValuesToUri(Uri existing)
    {
        // This is a mess because Uri is a very old .NET class with dubious design.
        // In particular, UriBuilder doesn't work with relative URIs, and neither do most Uri properties...

        var full = existing.IsAbsoluteUri ? existing : new Uri(new Uri("http://example.org"), existing);
        var query = HttpUtility.ParseQueryString(full.Query);
        foreach (var (key, values) in _text)
        {
            foreach (var value in values)
            {
                query.Add(key, value);
            }
        }

        if (query.Count == 0)
        {
            return existing;
        }
        if (existing.IsAbsoluteUri)
        {
            var builder = new UriBuilder(existing)
            {
                Query = query.ToString()
            };
            return builder.Uri;
        }
        return new Uri($"{full.AbsolutePath}?{query}", UriKind.Relative);
    }


    /// <summary>
    /// Invokes the operation defined by the specific target and method using these arguments, with the given user as an optional extra argument.
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <exception cref="InvalidOperationException">The method cannot be invoked with these arguments, e.g., due to a parsing failure.</exception>
    /// <exception cref="ArgumentException">The invocation is invalid regardless of arguments, e.g., the target type is mismatched.</exception>
    public T Invoke<T>(object target, MethodInfo method, User? user)
    {
        if (method.ReflectedType != target.GetType())
        {
            throw new ArgumentException("Mismatched target type.", nameof(target));
        }
        if (method.ReturnType != typeof(T))
        {
            throw new ArgumentException("Mismatched return type.", nameof(method));
        }

        static object? ParsePrimitive(string value, Type type)
        {
            if (type == typeof(string))
            {
                return value;
            }

            if (type.IsEnum)
            {
                return Enum.TryParse(type, value, ignoreCase: false, out var result)
                    ? result
                    : throw new InvalidOperationException($"Failed to parse enum of type {type}.");
            }

            if (type == typeof(DateTimeOffset))
            {
                // parse the <input type="datetime-local"> format, always as UTC for simplicity
                if (DateTimeOffset.TryParseExact(value, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDateTime))
                {
                    return parsedDateTime;
                }
                throw new InvalidOperationException("Failed to parse date&time.");
            }

            if (type == typeof(Uri))
            {
                if (Uri.TryCreate(value, UriKind.Absolute, out var parsedUri))
                {
                    return parsedUri;
                }
                throw new InvalidOperationException("Failed to parse URI.");
            }

            // first try with a format provider for, e.g., doubles; then fall back to one without for, e.g., bools.
            try
            {
                var formatAwareParser = type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string), typeof(IFormatProvider)]);
                if (formatAwareParser is not null)
                {
                    return formatAwareParser.Invoke(null, [value, CultureInfo.InvariantCulture]);
                }

                var parser = type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)])
                          ?? throw new ArgumentException($"No parse method for primitive of type {type}", nameof(value));
                return parser.Invoke(null, [value]);
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException($"Failed to parse primitive of type {type}", ex);
            }
        }

        object? ParseCompositeOrPrimitive(string name, int? index, Type type)
        {
            string? GetValueAt(string name, int? index)
                => _text.TryGetValue(name, out var values) && (index is null || index < values.Length) ? values[index ?? 0] : null;

            // Arrays
            if (type.IsArray)
            {
                var elementType = type.GetElementType()!;
                // For string arrays, allow the '-all' form of one argument whose values are lines
                // This is the only way we can parse nested arrays, so don't requre "-all" in that case
                string suffix = index is null ? "-all" : "";
                // This is particularly useful for nested inputs
                if (elementType == typeof(string) && GetValueAt(name + suffix, index) is string singleValue)
                {
                    return singleValue.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }
                // For non-nested arrays, allow elements to be passed individually
                if (index is null)
                {
                    var values = new List<object>();
                    int arrayIndex = 0;
                    while (ParseCompositeOrPrimitive(name, arrayIndex, elementType) is object item)
                    {
                        // Ignore empty strings, but keep looking for further items,
                        // e.g., the user might've sent ["a", "", "b"]
                        if (item is not string s || s.Length > 0)
                        {
                            values.Add(item);
                        }
                        arrayIndex++;
                    }
                    var array = Array.CreateInstance(elementType, values.Count);
                    for (int n = 0; n < array.Length; n++)
                    {
                        array.SetValue(values[n], n);
                    }
                    return array;
                }
                return Array.CreateInstance(elementType, 0);
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>))
            {
                var elementType = type.GenericTypeArguments[0];
                var arrayType = elementType.MakeArrayType();
                return typeof(ImmutableArray).GetMethods(BindingFlags.Public | BindingFlags.Static)
                                             .Single(m => m.Name.Equals("Create", StringComparison.Ordinal) && m.GetParameters() is [{ ParameterType.IsArray: true }])
                                             .MakeGenericMethod(elementType)
                                             .Invoke(null, [ParseCompositeOrPrimitive(name, index, arrayType)]);
            }

            // Dictionaries
            if (index is null && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableDictionary<,>))
            {
                var keyType = type.GenericTypeArguments[0];
                var valueType = type.GenericTypeArguments[1];
                bool hasKeys = _text.TryGetValue(name + ".Key", out var keys);
                bool hasValues = _text.TryGetValue(name + ".Value", out var values);
                if (hasKeys != hasValues)
                {
                    throw new InvalidOperationException($"Mismatched key-value count for: {name}");
                }
                var dict = type.GetField("Empty", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
                if (!hasKeys)
                {
                    return dict;
                }
                if (keys.Length != values.Length)
                {
                    throw new InvalidOperationException($"Mismatched key-value count for: {name}");
                }
                var addMethod = type.GetMethod("Add", BindingFlags.Public | BindingFlags.Instance)!;
                for (int n = 0; n < keys.Length; n++)
                {
                    var key = ParsePrimitive(keys[n], keyType);
                    var value = ParsePrimitive(values[n], valueType);
                    dict = addMethod.Invoke(dict, [key, value]);
                }
                return dict;
            }

            // Nullables
            var innerType = NullableHelper.GetNullableInnerType(type);
            if (innerType is not null)
            {
                if (ParseCompositeOrPrimitive(name, index, innerType) is object inner)
                {
                    return type.GetConstructor([innerType])!.Invoke([inner]);
                }
                return null;
            }

            // Primitives
            if (GetValueAt(name, index) is string argValue)
            {
                return ParsePrimitive(argValue.Trim(), type);
            }

            // Records
            // this comes _after_ parsing a primitive so that if a record is also parseable as a primitive, it is parsed that way
            // only records have an EqualityContract property, or record-like things
            if (type.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance) is not null)
            {
                var ctors = type.GetConstructors();
                if (ctors is not [var singleCtor])
                {
                    throw new InvalidOperationException($"Type {type} has multiple constructors");
                }

                var parameters = singleCtor.GetParameters();
                var args = new object?[parameters.Length];
                for (int n = 0; n < parameters.Length; n++)
                {
                    var isArgOptional = NullableHelper.IsNullable(parameters[n]);
                    var arg = ParseCompositeOrPrimitive($"{name}.{parameters[n].Name}", index, parameters[n].ParameterType);
                    // Ignore empty strings; we can't ignore them further down the stack because other parts care about the "empty vs absent" distinction.
                    if (arg is null or "" && !isArgOptional)
                    {
                        return null;
                    }
                    args[n] = arg;
                }
                return singleCtor.Invoke(args);
            }

            return null;
        }

        object? ParseTopLevel(string name, Type type)
        {
            if (type == typeof(OperationArguments))
            {
                return this;
            }

            if (type.IsAssignableTo(typeof(User)))
            {
                if (type == user?.GetType())
                {
                    return user;
                }
                return null;
            }

            if (type == typeof(File))
            {
                if (TryGetFile(name, out var file))
                {
                    return file;
                }
                return null;
            }

            return ParseCompositeOrPrimitive(name, null, type);
        }

        object? ParseParameter(ParameterInfo param)
        {
            var result = ParseTopLevel(param.Name!, param.ParameterType);

            if (param.ParameterType == typeof(string) && result is "")
            {
                // Ignore empty strings, at this level and not below because collection parsing must still consume them
                result = null;
            }

            if (result is null && !NullableHelper.IsNullable(param))
            {
                if (param.ParameterType.IsAssignableTo(typeof(User)))
                {
                    throw new AuthenticationRequiredException();
                }
                throw new InvalidOperationException($"Missing primitive argument '{param.Name}'.");
            }

            return result;
        }

        return (T?)method.Invoke(target, [.. method.GetParameters().Select(ParseParameter)])
                ?? throw new ArgumentException("Method should not return null.", nameof(method));
    }


    /// <summary>
    /// Creates a set of arguments with textual values extracted from the given URI.
    /// </summary>
    public static OperationArguments FromUri(Uri uri)
    {
        var query = HttpUtility.ParseQueryString(uri.Query);
        var builder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.Ordinal);
        foreach (var key in query.AllKeys)
        {
            // `!` because URI parsing is old .NET, it doesn't seem possible for these to be null
            builder.Add(key!, [.. query.GetValues(key)!]);
        }
        return new(builder.ToImmutable(), []);
    }

    /// <summary>
    /// Creates arguments from key-value pairs.
    /// </summary>
    public static OperationArguments FromPairs(params (string K, string V)[] pairs)
    {
        var result = Empty;
        foreach (var (k, v) in pairs)
        {
            result = result.WithText(k, v);
        }
        return result;
    }

    /// <summary>
    /// Combines two operation arguments.
    /// </summary>
    public static OperationArguments operator +(OperationArguments left, OperationArguments right)
    {
        var resultText = left._text;
        foreach (var (k, v) in right._text)
        {
            if (resultText.TryGetValue(k, out var existing))
            {
                resultText = resultText.SetItem(k, [.. existing, .. v]);
            }
            else
            {
                resultText = resultText.SetItem(k, v);
            }
        }
        return new(resultText, left._files.AddRange(right._files));
    }

    /// <summary>
    /// Indicates whether whether this instance is equal to the given one.
    /// </summary>
    public bool Equals(OperationArguments? other)
    {
        // ImmutableDictionary and ImmutableArray have reference rather than value semantics so this is a lot more complex than it should be

        if (other is null)
        {
            return false;
        }
        if (other._text.Count != _text.Count || other._files.Count != _files.Count)
        {
            return false;
        }
        foreach (var (key, values) in _text)
        {
            if (!other._text.TryGetValue(key, out var otherValues))
            {
                return false;
            }
            if (!StructuralComparisons.StructuralEqualityComparer.Equals(values, otherValues))
            {
                return false;
            }
        }
        foreach (var (key, file) in _files)
        {
            if (!other._files.TryGetValue(key, out var otherFile))
            {
                return false;
            }
            if (!file.Equals(otherFile))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// <inheritdoc />
    /// </summary>
    public override bool Equals(object? obj)
        => obj is OperationArguments other && Equals(other);

    /// <summary>
    /// <inheritdoc />
    /// </summary>
    public override int GetHashCode()
    {
        int result = 17;
        foreach (var key in _text.Keys)
        {
            result = HashCode.Combine(result, key);
        }
        foreach (var key in _files.Keys)
        {
            result = HashCode.Combine(result, key);
        }
        return result;
    }

    private static class NullableHelper
    {
        private static readonly NullabilityInfoContext _context = new();

        public static bool IsNullable(ParameterInfo parameter)
        {
            if (GetNullableInnerType(parameter.ParameterType) is not null)
            {
                return true;
            }

            var info = _context.Create(parameter);
            return info.WriteState == NullabilityState.Nullable;
        }

        public static Type? GetNullableInnerType(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                return type.GetGenericArguments()[0];
            }
            return null;
        }
    }
}