using System.Text;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

/// <summary>Bounded model-safe description of one adapter-advertised exception filter.</summary>
public sealed record DebugExceptionFilterMetadata(
    string FilterId,
    string Label,
    bool IsDefault,
    bool SupportsCondition);

/// <summary>Classified exception-filter validation failure with bounded recovery metadata.</summary>
internal sealed class DebugExceptionBreakpointValidationException(
    string message,
    IReadOnlyList<DebugExceptionFilterMetadata> availableFilters) : Exception(message)
{
    public IReadOnlyList<DebugExceptionFilterMetadata> AvailableFilters { get; } =
        availableFilters ?? throw new ArgumentNullException(nameof(availableFilters));
}

/// <summary>Validates semantic exception filters against capabilities negotiated by this session.</summary>
internal static class DebugExceptionBreakpointValidator
{
    private const int MaximumRequestedFilters = 64;
    private const int MaximumFilterIdBytes = 256;
    private const int MaximumConditionBytes = 4096;
    private const int MaximumAdvertisedMetadata = 64;

    public static IReadOnlyList<DebugExceptionFilterMetadata> Validate(
        Capabilities? capabilities,
        IReadOnlyList<DebugExceptionFilter> requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        var available = Metadata(capabilities);
        ValidateStructure(requested, available);
        var advertised = (capabilities?.ExceptionBreakpointFilters ?? [])
            .Where(filter => ValidText(filter.Filter, MaximumFilterIdBytes))
            .GroupBy(filter => filter.Filter, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        foreach (var filter in requested)
        {
            if (!advertised.TryGetValue(filter.FilterId, out var capability))
                throw Invalid("The requested exception filter is not supported by the selected adapter.", available);
            if (filter.Condition is not null &&
                capability.SupportsCondition != true)
                throw Invalid("The requested exception filter does not support a condition.", available);
        }
        return available;
    }

    public static void ValidateStructure(
        IReadOnlyList<DebugExceptionFilter> requested)
        => ValidateStructure(requested, []);

    /// <summary>Returns the bounded, model-safe exception filters advertised by the adapter.</summary>
    public static IReadOnlyList<DebugExceptionFilterMetadata> Describe(Capabilities? capabilities)
        => Metadata(capabilities);

    private static void ValidateStructure(
        IReadOnlyList<DebugExceptionFilter> requested,
        IReadOnlyList<DebugExceptionFilterMetadata> available)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (requested.Count > MaximumRequestedFilters)
            throw Invalid("Too many exception filters were requested.", available);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var filter in requested)
        {
            if (!ValidText(filter.FilterId, MaximumFilterIdBytes))
                throw Invalid("An exception filter ID is empty or exceeds its safe bound.", available);
            if (!seen.Add(filter.FilterId))
                throw Invalid("Duplicate exception filter IDs are not allowed.", available);
            if (filter.Condition is not null)
            {
                if (!ValidText(filter.Condition, MaximumConditionBytes))
                    throw Invalid("An exception filter condition is empty or exceeds its safe bound.", available);
            }
        }
    }

    private static IReadOnlyList<DebugExceptionFilterMetadata> Metadata(
        Capabilities? capabilities)
        => (capabilities?.ExceptionBreakpointFilters ?? [])
            .Where(filter => ValidText(filter.Filter, MaximumFilterIdBytes))
            .GroupBy(filter => filter.Filter, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(filter => filter.Filter, StringComparer.Ordinal)
            .Take(MaximumAdvertisedMetadata)
            .Select(filter => new DebugExceptionFilterMetadata(
                filter.Filter,
                BoundLabel(filter.Label),
                filter.Default == true,
                filter.SupportsCondition == true))
            .ToArray();

    private static string BoundLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return string.Empty;
        label = new string(label.Where(character => !char.IsControl(character)).ToArray());
        const int maximumCharacters = 256;
        return label.Length <= maximumCharacters
            ? label
            : label[..maximumCharacters];
    }

    private static bool ValidText(string? value, int maximumBytes)
        => !string.IsNullOrWhiteSpace(value) &&
           !value.Any(char.IsControl) &&
           Encoding.UTF8.GetByteCount(value) <= maximumBytes;

    private static DebugExceptionBreakpointValidationException Invalid(
        string message,
        IReadOnlyList<DebugExceptionFilterMetadata> available)
        => new(message, available);
}

/// <summary>
/// Owns validation and DAP argument construction for every exception-breakpoint mutation.
/// </summary>
internal static class DebugExceptionBreakpointProtocol
{
    public static async ValueTask<IReadOnlyList<Breakpoint>> ApplyAsync(
        DebugSession session,
        IReadOnlyList<DebugExceptionFilter> requested,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        DebugExceptionBreakpointValidator.Validate(session.Capabilities, requested);
        var response = await session.Protocol.SendAsync(
            DebugProtocolDescriptors.SetExceptionBreakpointsRequest,
            new SetExceptionBreakpointsArguments
            {
                Filters = requested.Select(filter => filter.FilterId).ToList(),
                FilterOptions = requested
                    .Where(filter => filter.Condition is not null)
                    .Select(filter => new ExceptionFilterOptions
                    {
                        FilterId = filter.FilterId,
                        Condition = filter.Condition
                    })
                    .ToList()
            },
            cancellationToken).ConfigureAwait(false);
        return response?.Breakpoints ?? [];
    }
}
