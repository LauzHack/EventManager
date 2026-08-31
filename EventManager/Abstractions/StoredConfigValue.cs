namespace EventManager.Abstractions;

/// <summary>
/// Infrastructure class to store configuration in the database.
/// </summary>
public sealed class StoredConfigValue(string typeName, string value)
{
    public string TypeName { get; set; } = typeName;
    public string Value { get; set; } = value;
}