namespace FileHandler.Tests.Diagnostics;

/// <summary>
/// Runs process-wide trace toggle tests separately from request tracing tests.
/// </summary>
[CollectionDefinition("Debug trace runtime toggle", DisableParallelization = true)]
public sealed class DebugTraceRuntimeCollection
{
}
