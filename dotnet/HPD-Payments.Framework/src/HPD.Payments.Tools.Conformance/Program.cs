using HPD.Payments.Tools.Conformance;

if (args is ["proof"])
{
    await Console.Error.WriteLineAsync("L5-13 proof execution is not activated; preparatory validators cannot issue proof.").ConfigureAwait(false);
    return 2;
}

await Console.Error.WriteLineAsync("Usage: HPD.Payments.Tools.Conformance proof (requires accepted activation gate)").ConfigureAwait(false);
return 64;
