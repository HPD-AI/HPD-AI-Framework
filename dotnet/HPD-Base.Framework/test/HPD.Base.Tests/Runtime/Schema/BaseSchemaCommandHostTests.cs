using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace HPD.Base.Tests.Runtime.Schema;

public sealed class BaseSchemaCommandHostTests
{
    [Fact]
    public async Task CommandsExposeFixedExitClassificationsAndSafeHumanOutput()
    {
        var manager = new CommandSchemaManager();
        var host = new BaseSchemaCommandHost(manager);

        (await Run(host, ["plan", "--store", "main"])).Should().Be((0, "schema plan completed; classification=NoChanges; operations=0\n"));
        (await Run(host, ["verify", "--store", "main"])).Item1.Should().Be(3, "a successfully observed drift is still a rejected deployment state");
        manager.PlanClassification = BaseSchemaPlanClassification.DataMigrationRequired;
        (await Run(host, ["plan", "--store", "main"])).Item1.Should().Be(3);
        manager.PlanClassification = BaseSchemaPlanClassification.Unsupported;
        (await Run(host, ["plan", "--store", "main"])).Item1.Should().Be(4);
        manager.PlanClassification = BaseSchemaPlanClassification.NoChanges;

        manager.ApplyResult = Failure<BaseSchemaApplyResult>(OperationStatus.ValidationFailed, BaseSchemaErrorCodes.PlanInvalid);
        (await Run(host, ["apply", "--artifact", Convert.ToBase64String([1])])).Item1.Should().Be(2);
        manager.ApplyResult = Failure<BaseSchemaApplyResult>(OperationStatus.Conflict, BaseSchemaErrorCodes.PlanStale);
        (await Run(host, ["apply", "--artifact", Convert.ToBase64String([1])])).Item1.Should().Be(3);
        manager.ApplyResult = Failure<BaseSchemaApplyResult>(OperationStatus.CapabilityUnavailable, BaseSchemaErrorCodes.MigrationBusy);
        (await Run(host, ["apply", "--artifact", Convert.ToBase64String([1])])).Item1.Should().Be(4);
        manager.ApplyResult = Failure<BaseSchemaApplyResult>(OperationStatus.StoreError, BaseSchemaErrorCodes.MigrationRolledBack);
        (await Run(host, ["apply", "--artifact", Convert.ToBase64String([1])])).Item1.Should().Be(5);
        manager.ApplyResult = Failure<BaseSchemaApplyResult>(OperationStatus.StoreError, BaseSchemaErrorCodes.MigrationIndeterminate);
        (await Run(host, ["apply", "--artifact", Convert.ToBase64String([1])])).Item1.Should().Be(6);

        (await Run(host, ["apply", "--artifact", "not-base64"])).Should().Be((2, "schema command rejected; code=base.schema.command.invalid\n"));
        (await Run(host, ["unknown"])).Item1.Should().Be(2);
        (await Run(host, ["plan", "--store", "main", "--unexpected", "secret-native-value"])).Item2
            .Should().NotContain("secret-native-value");
    }

    [Fact]
    public async Task JsonOutputUsesClosedSourceGeneratedResultContracts()
    {
        var manager = new CommandSchemaManager();
        var host = new BaseSchemaCommandHost(manager);

        (int exit, string text) = await Run(host, ["plan", "--store", "main", "--json"]);

        exit.Should().Be(0);
        using JsonDocument document = JsonDocument.Parse(text);
        document.RootElement.GetProperty("status").GetString().Should().Be("ok");
        document.RootElement.GetProperty("value").GetProperty("classification").GetString()
            .Should().Be(nameof(BaseSchemaPlanClassification.NoChanges));
        text.Should().NotContain("secret-native-value");

        manager.ApplyResult = Failure<BaseSchemaApplyResult>(OperationStatus.StoreError, BaseSchemaErrorCodes.MigrationRolledBack);
        (_, text) = await Run(host, ["apply", "--artifact", Convert.ToBase64String([1]), "--json"]);
        text.Should().NotContain("secret-native-value");
        JsonDocument.Parse(text).RootElement.GetProperty("error").GetProperty("message").GetString()
            .Should().Be("The schema command failed.");
    }

    private static async Task<(int, string)> Run(BaseSchemaCommandHost host, string[] arguments)
    {
        using var output = new StringWriter();
        int exit = await host.RunAsync(arguments, output);
        return (exit, output.ToString());
    }

    private static OperationResult<T> Failure<T>(OperationStatus status, string code) => new()
    {
        Status = status,
        Error = new BaseError { Code = code, Message = "secret-native-value", Category = ErrorCategory.Store },
    };

    private sealed class CommandSchemaManager : IBaseSchemaManager
    {
        public BaseSchemaPlanClassification PlanClassification { get; set; } = BaseSchemaPlanClassification.NoChanges;
        public OperationResult<BaseSchemaApplyResult> ApplyResult { get; set; } = OperationResults.Ok(new BaseSchemaApplyResult
        {
            Outcome = BaseSchemaApplyOutcome.Applied,
            Generation = 1,
            BaselineId = "baseline",
            Checksum = "checksum",
            State = BaseSchemaMigrationState.Ready,
            SubjectTombstoneMetadata = [],
        });

        public ValueTask<OperationResult<BaseSchemaPlan>> PlanAsync(BaseSchemaPlanRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OperationResults.Ok(new BaseSchemaPlan
            {
                PlanId = "plan", ApplicationId = "app", StoreId = request.StoreId, PersistedStoreInstanceId = "instance",
                ProviderId = "provider", ProviderVersion = "1", PlannerVersion = "1", ExpectedGeneration = 0,
                TargetBaselineId = "baseline", TargetChecksum = "checksum", Classification = PlanClassification,
                Operations = [], CreatedAt = DateTimeOffset.UnixEpoch, ExpiresAt = DateTimeOffset.UnixEpoch.AddHours(1),
                LogicalPlanDigest = "logical", ProviderApplyArtifactDigest = "provider", ProtectedArtifact = [1, 2, 3],
            }));

        public ValueTask<OperationResult<BaseSchemaObservedState>> VerifyAsync(BaseSchemaVerifyRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OperationResults.Ok(new BaseSchemaObservedState
            {
                StoreId = request.StoreId,
                PersistedStoreInstanceId = "instance",
                Generation = 1,
                Compatibility = BaseSchemaCompatibility.Drifted,
                Assets = [],
                MigrationState = BaseSchemaMigrationState.Failed,
            }));

        public ValueTask<OperationResult<BaseSchemaApplyResult>> ApplyAsync(BaseSchemaApplyRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ApplyResult);

        public ValueTask<OperationResult<BaseSchemaHistoryPage>> ReadHistoryAsync(string storeId, BaseSchemaHistoryRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OperationResults.Ok(new BaseSchemaHistoryPage { Items = [] }));
    }
}
