namespace HPD.Agent.ModelsDev;

public sealed class ModelsDevOptions
{
    public Uri ApiUri { get; set; } = new("https://models.dev/api.json");

    public string? CachePath { get; set; }

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool UseDiskCache { get; set; } = true;
}
