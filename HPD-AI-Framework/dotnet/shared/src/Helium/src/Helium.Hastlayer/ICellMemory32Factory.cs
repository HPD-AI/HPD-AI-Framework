namespace Helium.Hastlayer;

public interface ICellMemory32Factory
{
    ICellMemory32 Create(int cellCount);
}

public sealed class SimpleMemory32Factory : ICellMemory32Factory
{
    public static SimpleMemory32Factory Instance { get; } = new();

    private SimpleMemory32Factory() { }

    public ICellMemory32 Create(int cellCount) => SimpleMemory32.CreateSoftwareMemory(cellCount);
}
