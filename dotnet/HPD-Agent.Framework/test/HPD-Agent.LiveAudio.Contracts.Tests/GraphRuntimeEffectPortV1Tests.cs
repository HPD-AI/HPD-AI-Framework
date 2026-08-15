using System.Reflection;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphRuntimeEffectPortV1Tests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    public void CompletedReceipts_OwnExactBoundedBytes(int length)
    {
        var source = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
        var expected = source.ToArray();
        var executed = new GraphRuntimeEffectExecutionResultV1.Completed(source);
        var queried = new GraphRuntimeEffectQueryResultV1.Completed(source);

        Array.Fill(source, (byte)0);

        Assert.Equal(expected, executed.ReceiptBytes.ToArray());
        Assert.Equal(expected, queried.ReceiptBytes.ToArray());
        Assert.False(executed.ReceiptBytes.Equals(queried.ReceiptBytes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4097)]
    public void CompletedReceipts_RejectOutOfBounds(int length)
    {
        var bytes = new byte[length];
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphRuntimeEffectExecutionResultV1.Completed(bytes));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphRuntimeEffectQueryResultV1.Completed(bytes));
    }

    [Fact]
    public void ResultArms_AreClosedSeparateAndHostileToInvalidCodes()
    {
        Assert.Empty(typeof(GraphRuntimeEffectExecutionResultV1).GetConstructors());
        Assert.Empty(typeof(GraphRuntimeEffectQueryResultV1).GetConstructors());
        Assert.IsAssignableFrom<GraphRuntimeEffectExecutionResultV1>(new GraphRuntimeEffectExecutionResultV1.Completed([1]));
        Assert.IsAssignableFrom<GraphRuntimeEffectExecutionResultV1>(new GraphRuntimeEffectExecutionResultV1.Refused(new BoundedAscii("refused")));
        Assert.IsAssignableFrom<GraphRuntimeEffectExecutionResultV1>(new GraphRuntimeEffectExecutionResultV1.OutcomeUnknown(new BoundedAscii("unknown")));
        Assert.IsAssignableFrom<GraphRuntimeEffectQueryResultV1>(new GraphRuntimeEffectQueryResultV1.Completed([1]));
        Assert.IsAssignableFrom<GraphRuntimeEffectQueryResultV1>(new GraphRuntimeEffectQueryResultV1.NotObserved());
        Assert.IsAssignableFrom<GraphRuntimeEffectQueryResultV1>(new GraphRuntimeEffectQueryResultV1.Contradictory());
        Assert.IsAssignableFrom<GraphRuntimeEffectQueryResultV1>(new GraphRuntimeEffectQueryResultV1.OutcomeUnknown(new BoundedAscii("unknown")));
        Assert.Throws<ArgumentException>(() => new GraphRuntimeEffectExecutionResultV1.Refused(default));
        Assert.Throws<ArgumentException>(() => new GraphRuntimeEffectExecutionResultV1.OutcomeUnknown(default));
        Assert.Throws<ArgumentException>(() => new GraphRuntimeEffectQueryResultV1.OutcomeUnknown(default));
    }

    [Fact]
    public void QueryIdentity_IsExactAndRejectsEveryInvalidComponent()
    {
        var operation = OperationId.FromValue(Id(1));
        var hash = Hash(2);
        var query = new GraphRuntimeEffectQueryV1(operation, GraphRuntimeCommandKindV1.Activate, hash);
        Assert.Equal(operation, query.OperationId);
        Assert.Equal(GraphRuntimeCommandKindV1.Activate, query.Kind);
        Assert.Equal(hash, query.RequestHash);
        Assert.Throws<ArgumentException>(() => new GraphRuntimeEffectQueryV1(default, GraphRuntimeCommandKindV1.Activate, hash));
        Assert.Throws<ArgumentException>(() => new GraphRuntimeEffectQueryV1(operation, default, hash));
        Assert.Throws<ArgumentException>(() => new GraphRuntimeEffectQueryV1(operation, (GraphRuntimeCommandKindV1)3, hash));
        Assert.Throws<ArgumentException>(() => new GraphRuntimeEffectQueryV1(operation, GraphRuntimeCommandKindV1.Retire, default));
    }

    [Fact]
    public void PortSurface_IsInternalStaticAndCancellationAware()
    {
        var type = typeof(IGraphRuntimeEffectPortV1);
        Assert.False(type.IsPublic);
        Assert.True(type.IsInterface);
        var methods = type.GetMethods().OrderBy(method => method.Name).ToArray();
        Assert.Equal(["ExecuteAsync", "QueryAsync"], methods.Select(method => method.Name));
        AssertMethod(methods[0], typeof(GraphRuntimeEffectRequestV1), typeof(ValueTask<GraphRuntimeEffectExecutionResultV1>));
        AssertMethod(methods[1], typeof(GraphRuntimeEffectQueryV1), typeof(ValueTask<GraphRuntimeEffectQueryResultV1>));
        Assert.DoesNotContain(type.Assembly.GetTypes(), candidate =>
            candidate is { IsAbstract: false, IsInterface: false } && type.IsAssignableFrom(candidate));
    }

    [Fact]
    public void RequestSurface_IsClosedImmutableAndHasNoPublicApi()
    {
        var root = typeof(GraphRuntimeEffectRequestV1);
        Assert.False(root.IsPublic);
        Assert.True(root.IsAbstract);
        Assert.Empty(root.GetConstructors());
        Assert.Equal([typeof(GraphRuntimeEffectRequestV1.Activate), typeof(GraphRuntimeEffectRequestV1.Retire)],
            root.GetNestedTypes(BindingFlags.NonPublic).Where(type => root.IsAssignableFrom(type)).OrderBy(type => type.Name));
        foreach (var arm in new[] { typeof(GraphRuntimeEffectRequestV1.Activate), typeof(GraphRuntimeEffectRequestV1.Retire) })
        {
            var constructors = arm.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotEmpty(constructors);
            Assert.All(constructors, constructor => Assert.True(constructor.IsPrivate));
        }
        foreach (var type in new[] { root, typeof(GraphRuntimeEffectRequestV1.Activate), typeof(GraphRuntimeEffectRequestV1.Retire),
                     typeof(GraphRuntimeEffectQueryV1), typeof(GraphRuntimeEffectExecutionResultV1),
                     typeof(GraphRuntimeEffectQueryResultV1), typeof(IGraphRuntimeEffectPortV1) })
        {
            Assert.False(type.IsPublic || type.IsNestedPublic);
            Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
            Assert.DoesNotContain(type.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance),
                property => property.SetMethod is not null);
        }
    }

    private static void AssertMethod(MethodInfo method, Type request, Type result)
    {
        Assert.Equal(result, method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(request, parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
        Assert.True(parameters[1].HasDefaultValue);
        Assert.Null(parameters[1].DefaultValue);
    }

    private static StableId128 Id(byte value) => StableId128.FromBytes(Enumerable.Repeat(value, 16).ToArray());
    private static Hash256 Hash(byte value)
    {
        Hash256.TryCreate(Enumerable.Repeat(value, 32).ToArray(), out var hash);
        return hash;
    }
}
