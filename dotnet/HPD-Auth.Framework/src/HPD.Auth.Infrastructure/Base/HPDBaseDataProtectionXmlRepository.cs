using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using System.Xml;
using System.Xml.Linq;
using HPD.Auth.Base;
using HPD.Auth.Core.Options;
using HPD.Base;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Hosting;

namespace HPD.Auth.Infrastructure.Base;

/// <summary>
/// Persists the ASP.NET Core Data Protection key ring through the installed HPD Base
/// Auth graph while keeping synchronous reads provider-I/O-free.
/// </summary>
internal sealed class HPDBaseDataProtectionXmlRepository :
    IXmlRepository,
    IHostedService,
    IAuthDataProtectionCacheRefresh,
    IDisposable
{
    private static readonly TimeSpan StoreTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly IBaseSessionFactory _sessions;
    private readonly TimeProvider _timeProvider;
    private readonly AuthDataProtectionCacheInvalidationState _invalidation;
    private readonly string _applicationDiscriminator;
    private readonly Channel<PersistRequest> _writes = Channel.CreateBounded<PersistRequest>(new BoundedChannelOptions(32)
    {
        AllowSynchronousContinuations = false,
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
    });
    private readonly CancellationTokenSource _stopping = new();
    private CacheState? _cache;
    private Task? _worker;
    private long _cacheGeneration;
    private int _started;
    private int _disposed;

    /// <summary>Initializes the Base-backed Data Protection key repository.</summary>
    public HPDBaseDataProtectionXmlRepository(
        IBaseSessionFactory sessions,
        HPDAuthOptions options,
        TimeProvider timeProvider,
        AuthDataProtectionCacheInvalidationState invalidation)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        ArgumentNullException.ThrowIfNull(options);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _invalidation = invalidation ?? throw new ArgumentNullException(nameof(invalidation));
        _applicationDiscriminator = ValidateDiscriminator(options.AppName);
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        try
        {
            await ReloadAsync(cancellationToken).ConfigureAwait(false);
            _worker = RunWriterAsync(_stopping.Token);
        }
        catch
        {
            Volatile.Write(ref _started, 0);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _writes.Writer.TryComplete();
        Task[] pending = [_worker ?? Task.CompletedTask];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ShutdownTimeout);
        try
        {
            await Task.WhenAll(pending).WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _stopping.Cancel();
            if (cancellationToken.IsCancellationRequested)
                throw;
            throw new InvalidOperationException("HPD Auth Data Protection persistence did not drain during shutdown.");
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        CacheState cache = Volatile.Read(ref _cache)
            ?? throw new InvalidOperationException("HPD Auth Data Protection key storage is not ready.");
        if (cache.InvalidationGeneration != _invalidation.Generation)
        {
            Volatile.Write(ref _cache, null);
            throw new InvalidOperationException("HPD Auth Data Protection key storage is not ready.");
        }
        var elements = new XElement[cache.Keys.Length];
        for (int index = 0; index < cache.Keys.Length; index++)
            elements[index] = ParseOwned(cache.Keys[index].CanonicalXml);
        return elements;
    }

    /// <inheritdoc />
    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(friendlyName);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Volatile.Read(ref _cache) is null || Volatile.Read(ref _started) == 0)
            throw new InvalidOperationException("HPD Auth Data Protection key storage is not ready.");

        string normalizedName = friendlyName.Normalize(NormalizationForm.FormC);
        if (Encoding.UTF8.GetByteCount(normalizedName) > 256)
            throw new ArgumentOutOfRangeException(nameof(friendlyName));
        byte[] canonicalXml = Canonicalize(element);
        var request = new PersistRequest(normalizedName, canonicalXml);
        if (!_writes.Writer.TryWrite(request))
            throw new InvalidOperationException("HPD Auth Data Protection persistence capacity is exhausted.");
        try
        {
            request.Completion.Task.WaitAsync(StoreTimeout).GetAwaiter().GetResult();
        }
        catch (TimeoutException exception)
        {
            Volatile.Write(ref _cache, null);
            throw new InvalidOperationException("HPD Auth Data Protection key persistence timed out.", exception);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _writes.Writer.TryComplete();
        _stopping.Cancel();
        _stopping.Dispose();
    }

    private async Task RunWriterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (PersistRequest request in _writes.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await PersistAsync(request, cancellationToken).ConfigureAwait(false);
                    request.Completion.TrySetResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    request.Completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    Volatile.Write(ref _cache, null);
                    request.Completion.TrySetException(exception);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(request.CanonicalXml);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task PersistAsync(PersistRequest request, CancellationToken cancellationToken)
    {
        byte[] digest = SHA256.HashData(request.CanonicalXml);
        string id = AuthBaseDeterministicId.Create(_applicationDiscriminator, request.FriendlyName);
        var record = new AuthDataProtectionKeyRecordV1
        {
            Id = id,
            ApplicationDiscriminator = _applicationDiscriminator,
            FriendlyName = request.FriendlyName,
            CanonicalXml = BaseBinary.From(request.CanonicalXml),
            ContentDigest = BaseBinary.From(digest),
            CreatedAt = _timeProvider.GetUtcNow(),
            FormatVersion = 1,
        };
        BaseSession session = OpenSession();
        BaseMutationRequestIdentity identity = AuthBaseRuntime.MutationIdentity(
            "hpd.auth.data-protection.create.v1", Guid.Empty, id, Convert.ToHexStringLower(digest));
        BaseBatchBuilder batch = session.Atomic(identity);
        batch.Upsert(AuthDataProtectionKeyRecordV1.Collection, RecordId.Create(id), record, record,
            RecordUpsertExistenceCondition.CreateOnly);
        BaseResult<BaseBatchResult> committed = await batch.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (committed is BaseFailure<BaseBatchResult> failure)
            throw new InvalidOperationException(
                $"HPD Auth Data Protection key persistence failed ({failure.Error.Code}).");
        BaseBatchResult batchResult = committed.RequireValue();
        if (batchResult.Outcome != BaseRecordBatchOutcome.Committed)
            throw new InvalidOperationException(
                $"HPD Auth Data Protection key persistence failed ({batchResult.Error?.Code ?? "auth.dataProtection.keyCollision"}).");

        await ReloadAsync(cancellationToken).ConfigureAwait(false);
        CacheKey? persisted = Volatile.Read(ref _cache)?.Keys.SingleOrDefault(key =>
            string.Equals(key.Id, id, StringComparison.Ordinal));
        if (persisted is null || !CryptographicOperations.FixedTimeEquals(persisted.ContentDigest, digest)
            || !persisted.CanonicalXml.AsSpan().SequenceEqual(request.CanonicalXml))
            throw new InvalidOperationException("HPD Auth Data Protection key identity collided with different content.");
    }

    /// <inheritdoc />
    public async ValueTask<long> RefreshAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return await ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<long> ReloadAsync(CancellationToken cancellationToken)
    {
        long invalidationGeneration = _invalidation.Generation;
        BaseResult<BaseRegisteredReadResult<AuthDataProtectionKeysReadV1.Row>> result = await OpenSession().Reads
            .ToArrayWithAuthorityAsync(AuthDataProtectionKeysReadV1.Handle,
                new AuthDataProtectionKeysReadV1 { ApplicationDiscriminator = _applicationDiscriminator },
                cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BaseRegisteredReadResult<AuthDataProtectionKeysReadV1.Row>> failure)
            throw new InvalidOperationException(
                $"HPD Auth Data Protection key storage is unavailable ({failure.Error.Code}).");

        BaseRegisteredReadResult<AuthDataProtectionKeysReadV1.Row> read = result.RequireValue();
        _invalidation.Bind(read.Authority.LogicalStoreId);
        if (invalidationGeneration != _invalidation.Generation)
            throw new InvalidOperationException("HPD Auth Data Protection key storage changed during refresh.");
        if (read.Page.Items.Length > 256)
            throw new InvalidOperationException("HPD Auth Data Protection key storage exceeded its configured bound.");
        var keys = ImmutableArray.CreateBuilder<CacheKey>(read.Page.Items.Length);
        var identities = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        foreach (AuthDataProtectionKeysReadV1.Row row in read.Page.Items)
        {
            byte[] xml = row.CanonicalXml.ToArray();
            byte[] digest = row.ContentDigest.ToArray();
            total = checked(total + xml.Length);
            if (total > 16_777_216 || row.FormatVersion != 1 || digest.Length != 32
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(xml), digest))
                throw new InvalidOperationException("HPD Auth Data Protection key storage returned invalid material.");
            ParseOwned(xml);
            if (identities.TryGetValue(row.Id, out byte[]? prior) && !CryptographicOperations.FixedTimeEquals(prior, digest))
                throw new InvalidOperationException("HPD Auth Data Protection key storage returned conflicting identities.");
            identities[row.Id] = digest;
            keys.Add(new CacheKey(row.Id, xml, digest));
        }
        long generation = Interlocked.Increment(ref _cacheGeneration);
        Volatile.Write(ref _cache, new CacheState(
            keys.MoveToImmutable(), read.Authority, generation, invalidationGeneration));
        return generation;
    }

    private BaseSession OpenSession() => _sessions.For(new PrincipalContext
    {
        AuthenticationState = PrincipalAuthenticationState.System,
        SubjectKind = AccessSubjectKind.System,
        SubjectId = "hpd.auth",
        AuthSource = "hpd.auth.data-protection.v1",
    });

    private static byte[] Canonicalize(XElement element)
    {
        using var stream = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false, true),
            Indent = false,
            NewLineHandling = NewLineHandling.None,
            OmitXmlDeclaration = true,
            NamespaceHandling = NamespaceHandling.OmitDuplicates,
            CloseOutput = false,
        }))
            element.WriteTo(writer);
        if (stream.Length is < 1 or > 262_144)
            throw new ArgumentOutOfRangeException(nameof(element));
        return stream.ToArray();
    }

    private static XElement ParseOwned(ReadOnlySpan<byte> canonicalXml)
    {
        using var stream = new MemoryStream(canonicalXml.ToArray(), writable: false);
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false,
            MaxCharactersInDocument = 262_144,
            CloseInput = false,
        });
        return XElement.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static string ValidateDiscriminator(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Normalize(NormalizationForm.FormC);
        if (Encoding.UTF8.GetByteCount(normalized) > 256)
            throw new ArgumentOutOfRangeException(nameof(value));
        return normalized;
    }

    private sealed record CacheState(
        ImmutableArray<CacheKey> Keys,
        BaseRegisteredReadSnapshotAuthority Authority,
        long Generation,
        long InvalidationGeneration);

    private sealed record CacheKey(string Id, byte[] CanonicalXml, byte[] ContentDigest);

    private sealed class PersistRequest(string friendlyName, byte[] canonicalXml)
    {
        internal string FriendlyName { get; } = friendlyName;
        internal byte[] CanonicalXml { get; } = canonicalXml;
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
