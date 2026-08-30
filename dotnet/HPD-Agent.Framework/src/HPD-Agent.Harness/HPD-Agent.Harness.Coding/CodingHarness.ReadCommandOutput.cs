using System.ComponentModel;
using System.Text;
using HPD.Agent;
using HPD.Agent.Middleware;

public sealed record ReadCommandOutputRequest(
    ContentAddress Address,
    int MaxBytes = 262_144);

public sealed record ReadCommandOutputResult(
    string ContentType,
    long StoredBytes,
    string Encoding,
    string Content);

public partial class CodingToolHarness
{
    [AIFunction]
    [Description("Reads an exact restart-durable execute-command output address. The address must belong to the current session and retain its version and SHA-256 constraints.")]
    public async Task<ReadCommandOutputResult> ReadCommandOutput(
        ReadCommandOutputRequest request,
        FunctionExecutionContext context = null!,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxBytes is <= 0 or > 1_048_576)
            throw new ArgumentOutOfRangeException(nameof(request), "MaxBytes must be between 1 and 1048576.");
        if (context.ContentStore is null)
            throw new InvalidOperationException("No content store is configured.");
        if (context.ContentStore.PersistenceCapability != ContentStorePersistenceCapability.RestartDurable)
            throw new InvalidOperationException("Command output retrieval requires a restart-durable content store.");
        if (string.IsNullOrWhiteSpace(context.SessionId) ||
            !StringComparer.Ordinal.Equals(request.Address.Scope.Value, context.SessionId))
            throw new UnauthorizedAccessException("The command output address is outside the current session scope.");
        if (string.IsNullOrWhiteSpace(request.Address.Version) || string.IsNullOrWhiteSpace(request.Address.Sha256))
            throw new InvalidOperationException("An exact command output address requires version and SHA-256 constraints.");

        await using var opened = await context.ContentStore.OpenReadAsync(request.Address, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new FileNotFoundException("The referenced command output was not found.");
        var limit = (int)Math.Min(request.MaxBytes, opened.Info.SizeBytes);
        var bytes = new byte[limit];
        var read = 0;
        while (read < bytes.Length)
        {
            var count = await opened.Content.ReadAsync(bytes.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (count == 0)
                break;
            read += count;
        }
        if (read != bytes.Length)
            Array.Resize(ref bytes, read);

        var isText = opened.Info.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
            opened.Info.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase);
        return new ReadCommandOutputResult(
            opened.Info.ContentType,
            opened.Info.SizeBytes,
            isText ? "utf-8" : "base64",
            isText ? Encoding.UTF8.GetString(bytes) : Convert.ToBase64String(bytes));
    }
}
