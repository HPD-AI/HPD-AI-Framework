using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers;

public static class ProviderOperationAccounting
{
    public static async Task<T> ExecuteAsync<T>(
        ProviderOperationKind kind,
        ProviderClientFamily family,
        string? providerKey,
        string? modelId,
        Func<Task<T>> dispatch,
        Func<T, UsageDetails?> usage,
        Func<T, string?>? responseId = null)
    {
        var collector = ProviderOperationAccountingScope.Current;
        if (collector is null)
            return await dispatch().ConfigureAwait(false);

        var operationId = Guid.NewGuid().ToString("N");
        collector.RegisterAttempt(new(operationId, null, 1, kind, family, providerKey, modelId));
        T result;
        try
        {
            result = await dispatch().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await CommitAsync(collector, operationId, kind, family,
                exception is OperationCanceledException ? ProviderOperationOutcome.Cancelled : ProviderOperationOutcome.Failed,
                null, providerKey, modelId, null).ConfigureAwait(false);
            throw;
        }
        await CommitAsync(collector, operationId, kind, family, ProviderOperationOutcome.Succeeded,
            usage(result), providerKey, modelId, responseId?.Invoke(result)).ConfigureAwait(false);
        return result;
    }

    private static async Task CommitAsync(
        MessageTurnUsageCollector collector,
        string operationId,
        ProviderOperationKind kind,
        ProviderClientFamily family,
        ProviderOperationOutcome outcome,
        UsageDetails? usage,
        string? providerKey,
        string? modelId,
        string? responseId)
    {
        await collector.CommitTerminalAsync(new ProviderOperationUsageEvent(
            collector.MessageTurnId, operationId, null, 1, kind, family, outcome,
            usage, providerKey, modelId, responseId), CancellationToken.None).ConfigureAwait(false);
    }
}

internal sealed class UsageAccountingEmbeddingGenerator(
    IEmbeddingGenerator<string, Embedding<float>> inner,
    string? providerKey,
    string? modelId)
    : DelegatingEmbeddingGenerator<string, Embedding<float>>(inner), IEmbeddingGenerator
{
    public override Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ProviderOperationAccounting.ExecuteAsync(
            ProviderOperationKind.Embeddings, ProviderClientFamily.Embeddings, providerKey,
            options?.ModelId ?? modelId,
            () => InnerGenerator.GenerateAsync(values, options, cancellationToken),
            static result => result.Usage);

    protected override void Dispose(bool disposing) { }
}

#pragma warning disable MEAI001
internal sealed class UsageAccountingImageGenerator(
    IImageGenerator inner,
    string? providerKey,
    string? modelId) : DelegatingImageGenerator(inner)
{
    public override Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ProviderOperationAccounting.ExecuteAsync(
            ProviderOperationKind.ImageGeneration, ProviderClientFamily.ImageGeneration, providerKey,
            options?.ModelId ?? modelId,
            () => InnerGenerator.GenerateAsync(request, options, cancellationToken),
            static result => result.Usage);

    protected override void Dispose(bool disposing) { }
}

internal sealed class UsageAccountingHostedFileClient(
    IHostedFileClient inner,
    string? providerKey) : DelegatingHostedFileClient(inner)
{
    public override Task<HostedFileContent> UploadAsync(Stream content, string? mediaType = null,
        string? fileName = null, HostedFileClientOptions? options = null,
        CancellationToken cancellationToken = default) => Account(
            () => InnerClient.UploadAsync(content, mediaType, fileName, options, cancellationToken),
            static file => file.FileId);

    public override Task<HostedFileDownloadStream> DownloadAsync(string fileId,
        HostedFileClientOptions? options = null, CancellationToken cancellationToken = default) =>
        Account(() => InnerClient.DownloadAsync(fileId, options, cancellationToken));

    public override Task<HostedFileContent?> GetFileInfoAsync(string fileId,
        HostedFileClientOptions? options = null, CancellationToken cancellationToken = default) =>
        Account(() => InnerClient.GetFileInfoAsync(fileId, options, cancellationToken), static file => file?.FileId);

    public override Task<bool> DeleteAsync(string fileId, HostedFileClientOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Account(() => InnerClient.DeleteAsync(fileId, options, cancellationToken));

    public override async IAsyncEnumerable<HostedFileContent> ListFilesAsync(
        HostedFileClientOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var files = await Account(async () =>
        {
            var result = new List<HostedFileContent>();
            await foreach (var file in InnerClient.ListFilesAsync(options, cancellationToken).ConfigureAwait(false))
                result.Add(file);
            return result;
        }).ConfigureAwait(false);
        foreach (var file in files)
            yield return file;
    }

    private Task<T> Account<T>(Func<Task<T>> dispatch, Func<T, string?>? responseId = null) =>
        ProviderOperationAccounting.ExecuteAsync(
            ProviderOperationKind.HostedFileOperation, ProviderClientFamily.HostedFiles,
            providerKey, null, dispatch, static _ => null, responseId);

    protected override void Dispose(bool disposing) { }
}
#pragma warning restore MEAI001
