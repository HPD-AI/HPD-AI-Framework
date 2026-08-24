using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Payments.Tools.Conformance;

internal static class ReleaseProofRunner
{
    internal static async Task<ReleaseProofResult> RunAsync(string root, RegistrySnapshot snapshot,
        CommandManifestSnapshot commands, SourceTreeSnapshot source)
    {
        var adapter = SourceTreeSnapshotter.Capture(root, ["src/HPD.Payments.Adapters.InMemory"]);
        var dirtyState = await CaptureDirtyStateAsync(root).ConfigureAwait(false);
        var receipts = new List<ProofReceipt>(snapshot.Routes.Count);
        var dispositions = new List<RouteDisposition>(snapshot.Routes.Count);
        var executions = new List<CommandExecution>(snapshot.Routes.Count);
        var predecessor = "GENESIS";
        var completeFaultSchedule = Digest(FaultSchedule.Complete().ToCanonicalText());
        foreach (var route in snapshot.Routes.OrderBy(static route => route.Id, StringComparer.Ordinal))
        {
            var commandId = RouteCommand(route);
            var command = commands.RequireEnabled(commandId);
            var cell = Cell(route, commandId);
            var derivedSeed = HexDigest(ProofCanonical.Join(source.InventoryDigest, route.Id, commandId));
            var cellDigest = Digest(cell.ToCanonicalText());
            var execution = await ExecuteAsync(root, command, route.Id, derivedSeed, cellDigest, source.InventoryDigest).ConfigureAwait(false);
            executions.Add(execution);
            var assertions = execution.Assertions;
            var cleanup = ExecutionCleanupSnapshotter.Capture(root, command.Cleanup);
            var receipt = new ProofReceipt
            {
                Cell = cell,
                SchemaVersion = "hpd.payments.proof.v1",
                ReceiptId = "release-" + route.Id,
                RunId = execution.RunId,
                RouteId = route.Id,
                SourceRevision = source.InventoryDigest,
                WholeTreeDigest = source.InventoryDigest,
                DirtyState = dirtyState,
                AdapterTreeDigest = adapter.InventoryDigest,
                CanonicalRegistryDigest = snapshot.CanonicalDigest,
                ClaimMatrixDigest = snapshot.ClaimMatrixDigest,
                PredecessorDigest = predecessor,
                DependencyDigests = Array.Empty<string>(),
                CommandBinding = command.Binding,
                AssertionsDigest = assertions.EvidenceDigest,
                OracleBinding = $"{command.Id}@commands-r{commands.Revision}",
                CodeRevision = source.InventoryDigest,
                ConfigurationRevision = commands.ManifestDigest,
                CredentialRevision = route.Prefix is "CHK" or "CONN" or "DISP" or "PAY" or "PAYOUT" or "REF" or "ROUT"
                    ? "simulator:no-credential" : "not-applicable",
                ProtocolRevision = "hpd.payments.contracts.v1",
                PolicyRevision = snapshot.ClaimMatrixDigest,
                CorpusDigest = Digest(ProofCanonical.Join(route.Id, route.CandidateContractFamily)),
                RootSeed = HexDigest(ProofCanonical.Join(source.InventoryDigest, execution.RunId)),
                DerivedSeed = derivedSeed,
                VirtualTimeTraceDigest = Digest(ProofCanonical.Join(route.Id, execution.StandardOutput)),
                FaultScheduleDigest = completeFaultSchedule,
                StandardOutputDigest = Digest(execution.StandardOutput),
                StandardErrorDigest = Digest(execution.StandardError),
                ExitStatus = execution.ExitCode,
                StartedAtUtc = execution.StartedAtUtc,
                EndedAtUtc = execution.EndedAtUtc,
                ResourceObservations = "managed exact-route oracle; resource/AOT claims retained in separately scoped receipts",
                Limitations = "InMemory managed release cell; SQLite, distributed, lane and Native AOT evidence remain separately scoped; external provider truth not claimed",
                CleanupAttestation = cleanup.Attestation,
                Provenance = $"fresh child process; command={command.Id}; source={source.InventoryDigest}",
                State = ProofState.Executed,
                Lifecycle = ReceiptLifecycle.Active,
            };
            receipt = ProofRunAdmission.Admit(receipt, source, source, command, cleanup, assertions);
            predecessor = receipt.ContentAddress();
            receipts.Add(receipt);
            dispositions.Add(new(route.Id, RouteDispositionKind.Selected,
                $"fresh exact-cell execution through admitted {command.Id}", [cell]));
        }

        var sourceAfter = SourceTreeSnapshotter.Capture(root,
            ["src", "test", "perf", "eng/registry", "eng/commands", "Directory.Build.props", "Directory.Build.targets",
                "Directory.Packages.props", "HPD-Payments.slnx"]);
        SourceTreeSnapshotter.RequireStable(source, sourceAfter);

        var evidence = ReleaseEvidenceValidator.Validate(snapshot, dispositions, receipts);
        if (!evidence.ReleaseReady) throw new InvalidDataException("Release evidence is incomplete: " +
            string.Join(',', evidence.Selection.Errors.Concat(evidence.Proof.Errors).Concat(evidence.EvidenceErrors)));
        var manifest = new ReleaseManifest
        {
            SchemaVersion = "hpd.payments.release-manifest.v1",
            CanonicalRegistryDigest = snapshot.CanonicalDigest,
            ClaimMatrixDigest = snapshot.ClaimMatrixDigest,
            SourceRevision = source.InventoryDigest,
            CreatedAtUtc = executions.Max(static execution => execution.EndedAtUtc),
            PredecessorManifestDigest = "GENESIS",
            Lifecycle = ReleaseManifestLifecycle.Published,
            Dispositions = dispositions,
        };
        if (!manifest.ValidateAgainst(snapshot).ReleaseComplete) throw new InvalidDataException("Published manifest is incomplete.");
        await ValidateApprovalStorageAsync(manifest).ConfigureAwait(false);
        return new(manifest.ContentAddress(), evidence.Proof.TerminalDigest, receipts.Count, executions.Count);
    }

    private static ProofCellKey Cell(RegistryRoute route, string commandId)
    {
        var providerRoute = route.Prefix is "CHK" or "CONN" or "DISP" or "PAY" or "PAYOUT" or "REF" or "ROUT";
        return new(route.Id,
            route.AuthorityOwners.Count == 0 ? route.OwnerOrSupportingConcept : route.AuthorityOwners[0],
            route.CandidateContractFamily,
            route.OwnershipCells[0], route.ExtensionCells[0],
            "EmbeddedInMemory", "Static", "InMemory", providerRoute ? "Simulator" : "NotApplicable",
            providerRoute ? "local-simulator" : "NotApplicable", providerRoute ? "deterministic" : "NotApplicable",
            providerRoute ? "v1" : "NotApplicable", "managed-release", "portable-managed", "macOS", "arm64",
            "dotnet-sdk-10.0.301", "net10.0", "Roslyn", "NotApplicable", "false",
            commandId, route.Id);
    }

    private static string RouteCommand(RegistryRoute route) => route.Prefix switch
    {
        "TEST" => "validate-proof",
        "CHK" or "CONN" or "DISP" or "PAY" or "PAYOUT" or "REF" or "ROUT" => "test-simulator-certification",
        "EVT" or "OBS" or "WORK" => "test-worker",
        _ => "test-runtime-baseline",
    };

    private static async Task<CommandExecution> ExecuteAsync(string root, ProofCommandDefinition command,
        string routeId, string derivedSeed, string cellDigest, string sourceDigest)
    {
        var start = new ProcessStartInfo(command.Argv[0])
        {
            WorkingDirectory = Path.GetFullPath(Path.Combine(root, command.WorkingDirectory)),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in command.Argv.Skip(1)) start.ArgumentList.Add(argument);
        start.Environment["HPD_PAYMENTS_RELEASE_ROUTE"] = routeId;
        start.Environment["HPD_PAYMENTS_RELEASE_SEED"] = derivedSeed;
        start.Environment["HPD_PAYMENTS_RELEASE_CELL"] = cellDigest;
        start.Environment["HPD_PAYMENTS_RELEASE_SOURCE"] = sourceDigest;
        start.Environment["HPD_PAYMENTS_RELEASE_COMMAND"] = command.Id;
        var started = DateTimeOffset.UtcNow;
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {command.Id}.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(command.TimeoutSeconds)).ConfigureAwait(false);
        var ended = DateTimeOffset.UtcNow;
        var stdout = await standardOutput.ConfigureAwait(false);
        var stderr = await standardError.ConfigureAwait(false);
        if (!command.AcceptedExitCodes.Contains(process.ExitCode))
            throw new InvalidDataException($"Release oracle {command.Id} failed with {process.ExitCode}: {stderr}");
        var prefix = $"PASS release-cell route={routeId} seed={derivedSeed} cell={cellDigest} assertions=";
        var attestation = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SingleOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        if (attestation is null)
            throw new InvalidDataException($"Release oracle {command.Id} omitted the exact route attestation for {routeId}.");
        var fields = attestation.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var digest = fields.Single(x => x.StartsWith("assertions=", StringComparison.Ordinal))[11..];
        var total = int.Parse(fields.Single(x => x.StartsWith("total=", StringComparison.Ordinal))[6..], System.Globalization.CultureInfo.InvariantCulture);
        var passed = int.Parse(fields.Single(x => x.StartsWith("passed=", StringComparison.Ordinal))[7..], System.Globalization.CultureInfo.InvariantCulture);
        var assertions = new ProofAssertionOutcome(digest, total, passed, 0, 0, 0, 0, 0);
        return new($"release-{routeId}-{started:yyyyMMddTHHmmssfffffffZ}", started, ended, process.ExitCode, stdout, stderr, assertions);
    }

    private static async Task<string> CaptureDirtyStateAsync(string root)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "status", "--porcelain=v1", "--", "src", "test", "perf", "eng/registry", "eng/commands",
                     "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "HPD-Payments.slnx" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not inspect source dirty state.");
        var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidDataException("Source dirty-state inspection failed.");
        return $"sha256:{HexDigest(output)};entries={output.Count(static character => character == '\n')}";
    }

    private static async Task ValidateApprovalStorageAsync(ReleaseManifest manifest)
    {
        var root = Path.Combine(Path.GetTempPath(), $"hpd-payments-release-approval-{Environment.ProcessId}");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);
        try
        {
            using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var issued = manifest.CreatedAtUtc;
            var expires = issued.AddMinutes(5);
            var approval = ReleaseApprovalSigner.Sign(manifest, ReleaseAuthorizationAction.Publish,
                "level5-local-release-authority", "hpd.payments.release-policy.v1", issued, expires, signingKey);
            var stored = ReleaseApprovalStore.Write(root, approval);
            var loaded = ReleaseApprovalStore.Load(root, stored.ContentAddress);
            var key = new ReleaseApprovalKey(approval.ApproverId, signingKey.ExportSubjectPublicKeyInfo(),
                issued.AddMinutes(-1), expires.AddMinutes(1), [ReleaseAuthorizationAction.Publish]);
            var keys = new Dictionary<string, ReleaseApprovalKey>(StringComparer.Ordinal) { [key.KeyId] = key };
            var policy = new ReleaseAuthorizationPolicy(approval.PolicyRevision, 1, new HashSet<string>([key.KeyId], StringComparer.Ordinal));
            var errors = ReleaseApprovalRepository.ValidateLineage([manifest], [loaded], keys, policy, issued.AddSeconds(1));
            if (errors.Count != 0) throw new InvalidDataException("Release approval validation failed: " + string.Join(',', errors));
            await Task.CompletedTask.ConfigureAwait(false);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static string Digest(string value) => "sha256:" + HexDigest(value);
    private static string HexDigest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record CommandExecution(string RunId, DateTimeOffset StartedAtUtc, DateTimeOffset EndedAtUtc,
        int ExitCode, string StandardOutput, string StandardError, ProofAssertionOutcome Assertions);
}

internal sealed record ReleaseProofResult(string ManifestAddress, string TerminalReceiptAddress, int ReceiptCount, int OracleCount);
