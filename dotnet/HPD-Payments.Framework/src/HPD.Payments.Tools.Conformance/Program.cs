using HPD.Payments.Tools.Conformance;

if (args is not ["proof"])
{
    await Console.Error.WriteLineAsync("Usage: HPD.Payments.Tools.Conformance proof").ConfigureAwait(false);
    return 64;
}

string root = Directory.GetCurrentDirectory();
RegistrySnapshot snapshot = RegistrySnapshot.Load(
    await File.ReadAllBytesAsync(Path.Combine(root, "eng/registry/canonical-capabilities.json")).ConfigureAwait(false),
    await File.ReadAllBytesAsync(Path.Combine(root, "eng/registry/claim-matrix.json")).ConfigureAwait(false));
CommandManifestSnapshot commands = CommandManifestSnapshot.Load(
    await File.ReadAllBytesAsync(Path.Combine(root, "eng/commands/commands.json")).ConfigureAwait(false));
commands.RequireProductRoot(root);
ProofCommandDefinition proofCommand = commands.RequireEnabled("test-conformance-proof");
SourceTreeSnapshot source = SourceTreeSnapshotter.Capture(root,
    ["src", "test", "perf", "eng/registry", "eng/commands", "Directory.Build.props", "Directory.Build.targets",
        "Directory.Packages.props", "HPD-Payments.slnx"]);
ReleaseProofResult release = await ReleaseProofRunner.RunAsync(root, snapshot, commands, source).ConfigureAwait(false);
await Console.Out.WriteLineAsync($"PASS conformance proof inventory: routes={snapshot.Routes.Count} commands={commands.Commands.Count} " +
    $"command={proofCommand.Id} source={source.InventoryDigest} manifest={release.ManifestAddress} " +
    $"receipts={release.ReceiptCount} oracles={release.OracleCount} terminal={release.TerminalReceiptAddress} releaseComplete=true").ConfigureAwait(false);
return 0;
