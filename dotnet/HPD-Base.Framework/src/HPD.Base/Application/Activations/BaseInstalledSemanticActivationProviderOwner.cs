namespace HPD.Base;

internal sealed class BaseInstalledSemanticActivationProviderOwner
{
    private readonly Lock _gate = new();
    private BaseInstalledSemanticActivationProviderDescriptor? _value;
    internal BaseInstalledSemanticActivationProviderDescriptor? Value => Volatile.Read(ref _value);
    internal void Publish(BaseInstalledSemanticActivationProviderDescriptor value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Interlocked.CompareExchange(ref _value, value, null) is not null)
            throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
    }

    internal void Rebind(string expectedStoreInstanceId, string resultingStoreInstanceId)
    {
        lock (_gate)
        {
            BaseInstalledSemanticActivationProviderDescriptor current = _value
                ?? throw new InvalidOperationException("base.semanticActivation.certificationUnavailable");
            if (!string.Equals(current.StoreInstanceId, expectedStoreInstanceId, StringComparison.Ordinal))
                throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
            Volatile.Write(ref _value,
                BaseSemanticActivationCertificationContract.RebindInstalled(current, resultingStoreInstanceId));
        }
    }
}
