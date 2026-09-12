namespace FileHandler.Api.Diagnostics;

/// <summary>
/// Marks a controller or endpoint action as eligible for automatic diagnostic request tracing.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class TraceableAttribute : Attribute
{
}
