using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.V2.Tests.TestInfrastructure;

public sealed class MathToolHarness
{
    [AIFunction]
    [Description("Adds two integers.")]
    public int Add(int left, int right) => left + right;

    [AIFunction]
    [Description("Multiplies two integers.")]
    public int Multiply(int left, int right) => left * right;

    [AIFunction]
    [Description("Subtracts right from left.")]
    public int Subtract(int left, int right) => left - right;
}
