using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Pipeline;
using Xunit;

namespace HPD.RAG.Core.Tests;

/// <summary>T-053 through T-054: IMragProcessor / IMragRouter / MragRouteResult tests.</summary>
public class TierInterfaceTests
{
    // T-053
    [Fact]
    public void MragRouteResult_To_ReturnsCorrectPortAndData()
    {
        var data = Array.Empty<MragChunkDto>();
        var result = MragRouteResult.To(1, data);

        Assert.Equal(1, result.Port);
        Assert.Same(data, result.Data);
    }

    // T-054
    [Fact]
    public void MragRouteResult_To_ThrowsOnNullData()
    {
        Assert.Throws<ArgumentNullException>(() => MragRouteResult.To(0, null!));
    }
}
