namespace HPD.Agent.ModelsDev;

public sealed record ModelsDevStoreDiagnostic(string Code, string Message, Exception? Exception = null);

public sealed class ModelsDevOptions
{
    public Uri ApiUri { get; set; } = new("https://models.dev/api.json");

    public string? CachePath { get; set; }

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool UseDiskCache { get; set; } = true;

    public int MaxTransientRetries { get; set; } = 2;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan RetryJitter { get; set; } = TimeSpan.FromMilliseconds(50);

    public Action<ModelsDevStoreDiagnostic>? DiagnosticSink { get; set; }
}
