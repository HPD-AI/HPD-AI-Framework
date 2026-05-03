using HPD.Agent;

namespace HPDOS.Harneses;

[Collapse("A simple diagnostic harness for verifying harness registration")]
public class PingHarness
{
    [AIFunction]
    [AIDescription("Returns 'pong' — use this to verify the harness is registered.")]
    public string Ping() => "pong";
}
