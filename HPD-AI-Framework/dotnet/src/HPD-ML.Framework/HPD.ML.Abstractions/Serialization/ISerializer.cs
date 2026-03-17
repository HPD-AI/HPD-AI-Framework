namespace HPD.ML.Abstractions;

/// <summary>Save and load models. Native AOT compatible — no reflection.</summary>
public interface ISerializer
{
    void Save(SaveRequest request);
    IModel Load(LoadRequest request);
}
