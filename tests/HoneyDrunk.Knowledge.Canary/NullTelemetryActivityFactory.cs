using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Kernel.Abstractions.Telemetry;
using System.Diagnostics;

namespace HoneyDrunk.Knowledge.Canary;

/// <summary>An activity factory that samples nothing, for the checks that do not look at telemetry.</summary>
internal sealed class NullTelemetryActivityFactory : ITelemetryActivityFactory
{
    /// <inheritdoc />
    public Activity? Start(string name, IReadOnlyDictionary<string, object?>? additionalTags = null) => null;

    /// <inheritdoc />
    public Activity? StartExplicit(string name, IGridContext gridContext, IOperationContext? operationContext = null, IReadOnlyDictionary<string, object?>? additionalTags = null) => null;
}
