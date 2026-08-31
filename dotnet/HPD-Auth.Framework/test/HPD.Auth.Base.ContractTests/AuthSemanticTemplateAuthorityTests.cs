using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using HPD.Auth.Base;
using HPD.Base;
using Xunit;

namespace HPD.Auth.Base.ContractTests;

/// <summary>
/// Proves the exact Auth semantic-cleanup templates rather than relying only on
/// their installed checksums.
/// </summary>
public sealed class AuthSemanticTemplateAuthorityTests
{
    private static readonly string[] States =
    [
        "semantic.state.missing",
        "semantic.state.live",
        "semantic.state.retired",
        "semantic.state.compactedAbsent",
    ];

    [Theory]
    [InlineData("user")]
    [InlineData("role")]
    public void EnsureAndRetirementTemplatesDeclareExactlyFourRootSemanticStates(string kind)
    {
        foreach (BaseRegisteredModuleMutationDefinition operation in Pair(kind))
        {
            string[] stateGuards = operation.Template.Guards
                .Where(static guard => guard is BaseModuleSemanticActivationStateGuard)
                .Select(GuardId)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(States.Select(value => $"hpd.auth.cleanup.{Verb(operation)}-{kind}.{value}")
                .OrderBy(static value => value, StringComparer.Ordinal), stateGuards);
        }
    }

    [Theory]
    [InlineData("user")]
    [InlineData("role")]
    public void TerminalEnsureAndDuplicateRetirementBranchesPerformNoRecordMutation(string kind)
    {
        BaseRegisteredModuleMutationDefinition ensure = Pair(kind)[0];
        BaseRegisteredModuleMutationDefinition retire = Pair(kind)[1];

        AssertTerminalBranchHasNoMutation(ensure.Template.Body, $"initialize-{kind}", "retired");
        AssertTerminalBranchHasNoMutation(ensure.Template.Body, $"initialize-{kind}", "compactedAbsent");
        AssertTerminalBranchHasNoMutation(retire.Template.Body, $"retire-{kind}", "retired");
        AssertTerminalBranchHasNoMutation(retire.Template.Body, $"retire-{kind}", "compactedAbsent");

        string[] ensureSemanticIds = ExpressionIds(ensure.Template.Result)
            .Where(static value => value.Contains("semanticActivationId", StringComparison.Ordinal))
            .ToArray();
        Assert.Contains(ensureSemanticIds,
            value => value.EndsWith("semanticActivationId.retired", StringComparison.Ordinal));
        Assert.Contains(ensureSemanticIds,
            value => value.EndsWith("semanticActivationId.absent", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("user")]
    [InlineData("role")]
    public void RetirementUsesTheSameRequestAuthorityAndStableLifetimeFieldsAsEnsure(string kind)
    {
        BaseRegisteredModuleMutationDefinition ensure = Pair(kind)[0];
        BaseRegisteredModuleMutationDefinition retire = Pair(kind)[1];
        BaseSemanticActivationKeyDefinition semantic = Registration(kind).Definition;

        Assert.Equal(ensure.RequestTypeId, retire.RequestTypeId);
        Assert.Equal(ensure.RequestTypeId, semantic.RequestTypeId);
        Assert.Equal(ensure.Id, semantic.EnsureOperation.OperationId);
        Assert.Equal(retire.Id, semantic.RetirementOperation.OperationId);
        Assert.Equal(Convert.ToHexStringLower(ensure.Checksum.ToArray()),
            semantic.EnsureOperation.OperationChecksum);
        Assert.Equal(Convert.ToHexStringLower(retire.Checksum.ToArray()),
            semantic.RetirementOperation.OperationChecksum);

        string[] retirementRequestPaths = ExpressionRequestPaths(retire.Template)
            .Distinct(StringComparer.Ordinal).ToArray();
        foreach (string propertyFragment in new[]
                 {
                     "cleanupWork", "tenant", "subjectId", ".subject.", "incarnation",
                     "tombstoneSequence", "tombstoneRevision", "workflowVersion",
                     "retirementReceiptScope", "operationTime",
                 })
            Assert.Contains(retirementRequestPaths, value =>
                value.Contains(propertyFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("user")]
    [InlineData("role")]
    public void SemanticOperationsUseOneNonretryableAuthRequirementFailure(string kind)
    {
        foreach (BaseRegisteredModuleMutationDefinition operation in Pair(kind))
        {
            string[] requirements = Statements(operation.Template.Body)
                .OfType<BaseModuleRequireStatement>()
                .Select(RequirementId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(["auth.cleanup.reconcileConflict"], requirements);
            Assert.DoesNotContain(requirements,
                static value => value.StartsWith("base.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Nonterminal_semantic_parent_maps_only_to_the_installed_retryable_failure()
    {
        BaseActivationHandlerResult<AuthCleanupRetirementResultV1> mapped =
            AuthCleanupRetirementActivationHandler.MapFailure(new BaseError
            {
                Code = BaseSemanticActivationErrorCodes.ActivationNotTerminal,
                Message = "safe",
                Category = ErrorCategory.Conflict,
            });

        BaseActivationFailed<AuthCleanupRetirementResultV1> failure =
            Assert.IsType<BaseActivationFailed<AuthCleanupRetirementResultV1>>(mapped);
        Assert.True(failure.Retryable);
        Assert.Equal("auth.cleanup.semanticRetirementPending", failure.FailureCode);
        Assert.Contains(failure.FailureCode,
            AuthLifecycleActivationDeclarations.RetireUser.Definition.Retry.RetryableFailureCodes);
        Assert.Contains(failure.FailureCode,
            AuthLifecycleActivationDeclarations.RetireRole.Definition.Retry.RetryableFailureCodes);
    }

    [Fact]
    public void SemanticKeysAreKindSeparatedAndExcludeMutableAuthorityLeaves()
    {
        BaseSemanticActivationKeyDefinition user = AuthCleanupSemanticActivations.User.Definition;
        BaseSemanticActivationKeyDefinition role = AuthCleanupSemanticActivations.Role.Definition;

        Assert.NotEqual(user.KeyExpressionChecksum, role.KeyExpressionChecksum);
        Assert.Equal(BaseSubjectScopeKind.Tenant, user.ScopeKind);
        Assert.Equal(BaseSubjectScopeKind.Tenant, role.ScopeKind);
        Assert.NotEqual(user.Compaction, role.Compaction);

        foreach (BaseSemanticActivationKeyDefinition definition in new[] { user, role })
        {
            Assert.DoesNotContain("epoch", definition.RequestTypeId, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("restore", definition.RequestTypeId, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(32, definition.KeyExpressionChecksum.Length);
            Assert.InRange(definition.Limits.MaximumCanonicalKeyBytes, 1, 256);
        }
    }

    private static BaseRegisteredModuleMutationDefinition[] Pair(string kind) => kind switch
    {
        "user" =>
        [
            AuthUserCleanupInitializeOperationV1.Definition,
            AuthUserCleanupRetireOperationV1.Definition,
        ],
        "role" =>
        [
            AuthRoleCleanupInitializeOperationV1.Definition,
            AuthRoleCleanupRetireOperationV1.Definition,
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static dynamic Registration(string kind) => kind switch
    {
        "user" => AuthCleanupSemanticActivations.User,
        "role" => AuthCleanupSemanticActivations.Role,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string Verb(BaseRegisteredModuleMutationDefinition operation) =>
        operation.Id.Contains("initialize", StringComparison.Ordinal) ? "initialize" : "retire";

    private static void AssertTerminalBranchHasNoMutation(
        BaseModuleMutationBlock root,
        string operation,
        string state)
    {
        string suffix = $"{operation}.statement.require.{state}";
        BaseModuleRequireStatement terminal = Assert.Single(Statements(root)
            .OfType<BaseModuleRequireStatement>(),
            statement => StatementId(statement).EndsWith(suffix, StringComparison.Ordinal));
        Assert.EndsWith($"{operation}.semantic.state.{state}", RequireGuardId(terminal),
            StringComparison.Ordinal);

        BaseModuleMutationBlock containing = FindContainingBlock(root, terminal);
        Assert.Single(containing.Statements);
        Assert.IsType<BaseModuleRequireStatement>(containing.Statements[0]);
    }

    private static BaseModuleMutationBlock FindContainingBlock(
        BaseModuleMutationBlock block,
        BaseModuleStatement target)
    {
        if (block.Statements.Contains(target))
            return block;
        foreach (BaseModuleIfStatement branch in block.Statements.OfType<BaseModuleIfStatement>())
        {
            BaseModuleMutationBlock? found = TryFind(BlockProperty(branch, "WhenTrue"), target)
                ?? TryFind(BlockProperty(branch, "WhenFalse"), target);
            if (found is not null)
                return found;
        }
        throw new Xunit.Sdk.XunitException($"Containing block was not found for {StatementId(target)}.");
    }

    private static BaseModuleMutationBlock? TryFind(BaseModuleMutationBlock block, BaseModuleStatement target)
    {
        if (block.Statements.Contains(target))
            return block;
        foreach (BaseModuleIfStatement branch in block.Statements.OfType<BaseModuleIfStatement>())
        {
            BaseModuleMutationBlock? found = TryFind(BlockProperty(branch, "WhenTrue"), target)
                ?? TryFind(BlockProperty(branch, "WhenFalse"), target);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static IEnumerable<BaseModuleStatement> Statements(BaseModuleMutationBlock block)
    {
        foreach (BaseModuleStatement statement in block.Statements)
        {
            yield return statement;
            if (statement is not BaseModuleIfStatement branch)
                continue;
            foreach (BaseModuleStatement nested in Statements(BlockProperty(branch, "WhenTrue")))
                yield return nested;
            foreach (BaseModuleStatement nested in Statements(BlockProperty(branch, "WhenFalse")))
                yield return nested;
        }
    }

    private static IEnumerable<string> ExpressionRequestPaths(BaseModuleMutationTemplate template) =>
        Walk(template).OfType<BaseModuleRequestPropertyExpression>()
            .Select(value => Property(value, "Property"))
            .SelectMany(value => ((IEnumerable)Property(value, "StablePropertyPath"))
                .Cast<object>().Select(static item => (string)item));

    private static IEnumerable<string> ExpressionIds(object root) => Walk(root)
        .OfType<BaseModuleValueExpression>()
        .Select(static value => value.Id);

    private static IEnumerable<object> Walk(object? value)
    {
        if (value is null || value is string || value.GetType().IsPrimitive || value is Enum)
            yield break;
        yield return value;
        if (value is IEnumerable sequence)
        {
            foreach (object? element in sequence)
                foreach (object child in Walk(element))
                    yield return child;
            yield break;
        }
        if (!value.GetType().Namespace?.StartsWith("HPD.Base", StringComparison.Ordinal) ?? true)
            yield break;
        foreach (PropertyInfo property in value.GetType().GetProperties(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (property.GetIndexParameters().Length != 0)
                continue;
            foreach (object child in Walk(property.GetValue(value)))
                yield return child;
        }
    }

    private static object Property(object value, string name) => value.GetType()
        .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
        .GetValue(value)!;

    private static string GuardId(object value) => (string)Property(value, "Id");
    private static string StatementId(object value) => (string)Property(value, "Id");
    private static string RequirementId(object value) => (string)Property(value, "RequirementId");
    private static string RequireGuardId(object value) => (string)Property(value, "GuardId");
    private static BaseModuleMutationBlock BlockProperty(object value, string name) =>
        (BaseModuleMutationBlock)Property(value, name);
}
