using System.Reflection;
using HPD.Auth.Base;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HPD.Auth.Base.ContractTests;

public sealed class AuthBasePublicBoundaryTests
{
    [Fact]
    public void NewLookupAndAdminReadContractsHaveDistinctExactAuthority()
    {
        Assembly assembly = typeof(AuthBaseModule).Assembly;
        object email = Definition(assembly, "HPD.Auth.Base.AuthUserByNormalizedEmailReadV1");
        object usersInRole = Definition(assembly, "HPD.Auth.Base.AuthUsersInRoleReadV1");
        object adminSessions = Definition(assembly, "HPD.Auth.Base.AuthActiveSessionsAdminReadV1");

        Assert.Equal("auth.read.userByNormalizedEmail.v1", Property<string>(email, "Id"));
        Assert.Equal("auth.read.usersInRole.v1", Property<string>(usersInRole, "Id"));
        Assert.Equal("auth.read.activeSessionsAdmin.v1", Property<string>(adminSessions, "Id"));

        object adminPlan = Property<object>(adminSessions, "Plan");
        string[] fields = ((System.Collections.IEnumerable)Property<object>(adminPlan, "Projection"))
            .Cast<object>().Select(value => Property<string>(value, "FieldId")).ToArray();
        Assert.DoesNotContain("auth.read.activeSessionsAdmin.v1.row.securityGeneration", fields);
    }

    [Fact]
    public void ReconciliationRequestRequiresCompleteCursorAndBoundedTake()
    {
        Type contract = typeof(AuthBaseModule).Assembly.GetType(
            "HPD.Auth.Base.AuthReconciliationProjectionContract", throwOnError: true)!;
        MethodInfo validate = contract.GetMethod("ValidateRequest", BindingFlags.Static | BindingFlags.NonPublic)!;

        validate.Invoke(null, [null, null, null, 1]);
        validate.Invoke(null, [Guid.Empty, Enum.Parse(
            typeof(AuthBaseModule).Assembly.GetType("HPD.Auth.Base.AuthCleanupSubjectKindV1", true)!, "user"), Guid.Empty, 200]);

        TargetInvocationException partial = Assert.Throws<TargetInvocationException>(() =>
            validate.Invoke(null, [Guid.Empty, null, Guid.Empty, 20]));
        Assert.Equal("auth.cleanup.reconciliationRequestInvalid", partial.InnerException?.Message);
        TargetInvocationException oversized = Assert.Throws<TargetInvocationException>(() =>
            validate.Invoke(null, [null, null, null, 201]));
        Assert.Equal("auth.cleanup.reconciliationRequestInvalid", oversized.InnerException?.Message);
    }

    [Fact]
    public void ReconciliationRejectsMissingAuthoritativeTombstoneTime()
    {
        Type contract = typeof(AuthBaseModule).Assembly.GetType(
            "HPD.Auth.Base.AuthReconciliationProjectionContract", throwOnError: true)!;
        MethodInfo require = contract.GetMethod("RequireTombstonedAt", BindingFlags.Static | BindingFlags.NonPublic)!;

        TargetInvocationException missing = Assert.Throws<TargetInvocationException>(() =>
            require.Invoke(null, [null]));

        Assert.Equal("auth.cleanup.reconciliationResultInvalid", missing.InnerException?.Message);
        Assert.Equal(DateTimeOffset.UnixEpoch, require.Invoke(null, [DateTimeOffset.UnixEpoch]));
    }

    [Fact]
    public void ExportedSurfaceContainsNoPrivateRecordContract()
    {
        Type[] exported = typeof(AuthBaseModule).Assembly.GetExportedTypes();

        Assert.DoesNotContain(exported, static type => type.Name.EndsWith("RecordV1", StringComparison.Ordinal));
        Assert.Contains(typeof(AuthBaseModule), exported);
        Assert.Contains(typeof(AuthSubjects), exported);
        Assert.Contains(typeof(AuthUserSubject), exported);
        Assert.Contains(typeof(AuthRoleSubject), exported);
    }

    [Fact]
    public void ExternalConsumerCannotCompileAgainstPrivateUserRecord()
    {
        SyntaxTree syntax = CSharpSyntaxTree.ParseText("""
            using HPD.Auth.Base;
            public static class ForgedConsumer
            {
                public static object Create() => new AuthUserRecordV1();
            }
            """);
        IEnumerable<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(AuthBaseModule).Assembly.Location));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ForgedConsumer",
            [syntax],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Diagnostic[] errors = compilation.GetDiagnostics().Where(static value => value.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Contains(errors, static error => error.Id == "CS0122" && error.GetMessage().Contains("AuthUserRecordV1", StringComparison.Ordinal));
    }

    private static object Definition(Assembly assembly, string typeName) =>
        assembly.GetType(typeName, throwOnError: true)!
            .GetProperty("Definition", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(null)!;

    private static T Property<T>(object instance, string name) =>
        (T)instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance)!;
}
