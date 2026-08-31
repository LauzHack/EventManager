namespace EventManager.Abstractions;

/// <summary>
/// Provides read and write access to a configuration value.
/// </summary>
/// <typeparam name="T">The type of the configuration value.</typeparam>
/// <param name="config">The configuration provider.</param>
public sealed class ConfigValue<T>(Config config)
    where T : notnull
{
    /// <summary>
    /// Gets the configuration value, or null if it is missing.
    /// </summary>
    public T? Value
        => config.Get<T>();

    /// <summary>
    /// Sets the configuration value.
    /// </summary>
    public void Set(T value)
        => config.Set(value);
}