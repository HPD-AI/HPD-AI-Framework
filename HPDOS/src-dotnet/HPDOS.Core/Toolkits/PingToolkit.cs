using HPD.Agent;

namespace HPDOS.Toolkits;

[Collapse("A simple diagnostic toolkit for verifying toolkit registration")]
public class PingToolkit
{
    [AIFunction]
    [AIDescription("Returns 'pong' — use this to verify the toolkit is registered.")]
    public string Ping() => "pong";
}
