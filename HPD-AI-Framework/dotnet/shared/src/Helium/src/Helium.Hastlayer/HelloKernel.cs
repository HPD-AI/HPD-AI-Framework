using Hast.Transformer.SimpleMemory;

namespace Helium.Hastlayer;

/// <summary>
/// Minimal Hastlayer-compatible kernel used as the first integration gate.
/// Layout:
/// cell 0: input uint
/// cell 1: output uint = input + 1
/// </summary>
public class HelloKernel
{
    public const int CellCount = 2;
    public const int InputCell = 0;
    public const int OutputCell = 1;

    public static int RequiredCellCount() => CellCount;

    public virtual void Execute(SimpleMemory32 memory)
    {
        ExecuteCore(memory);
    }

    public virtual void Execute(SimpleMemory memory)
    {
        var input = memory.ReadUInt32(InputCell);
        memory.WriteUInt32(OutputCell, input + 1);
    }

    public static void ExecuteCore(ICellMemory32 memory)
    {
        var input = memory.ReadUInt32(InputCell);
        memory.WriteUInt32(OutputCell, input + 1);
    }

    public uint ExecuteSoftware(uint input)
        => new HastlayerKernelHost().RunHello(input);
}
