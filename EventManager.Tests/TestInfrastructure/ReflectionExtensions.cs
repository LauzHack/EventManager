using System;
using System.Reflection;

namespace EventManager.Tests.TestInfrastructure;

public static class ReflectionExtensions
{
    public static MethodInfo GetRequiredMethod(this Type type, string methodName)
    {
        return type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new ArgumentException($"No method named '{methodName}' on {type}", nameof(methodName));
    }
}