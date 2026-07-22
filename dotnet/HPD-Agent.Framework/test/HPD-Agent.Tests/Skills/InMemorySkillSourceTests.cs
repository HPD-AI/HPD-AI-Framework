namespace HPD.Agent.Tests.Skills;

public sealed class InMemorySkillSourceTests
{
    [Fact]
    public async Task Replace_BroadcastsInvalidationToEveryWatcher()
    {
        var source = new InMemorySkillSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var first = source.WatchAsync(Context(), timeout.Token).GetAsyncEnumerator();
        await using var second = source.WatchAsync(Context(), timeout.Token).GetAsyncEnumerator();
        var firstMove = first.MoveNextAsync().AsTask();
        var secondMove = second.MoveNextAsync().AsTask();
        await Task.Yield();

        source.Replace([Skill.Create(
            "updated_skill",
            "Updated skill.",
            SkillInstructions.FromText("Use the updated instructions."))]);

        Assert.True(await firstMove);
        Assert.True(await secondMove);
        Assert.Equal(SkillSourceChangeKind.Reset, first.Current.Kind);
        Assert.Equal(SkillSourceChangeKind.Reset, second.Current.Kind);
    }

    [Fact]
    public async Task ConstructorAndReplace_SnapshotCallerCollections()
    {
        var initial = new List<Skill> { CreateSkill("first") };
        var source = new InMemorySkillSource(initial);
        initial.Clear();
        Assert.Single(await source.GetSkillsAsync(Context(), default));

        var replacement = new List<Skill> { CreateSkill("second") };
        source.Replace(replacement);
        replacement.Clear();

        Assert.Equal("second", Assert.Single(await source.GetSkillsAsync(Context(), default)).Name);
    }

    private static Skill CreateSkill(string name) => Skill.Create(
        name,
        $"{name} skill.",
        SkillInstructions.FromText($"Use {name}."));

    private static SkillSourceContext Context() => new("agent", "DataTools", null, null);
}
