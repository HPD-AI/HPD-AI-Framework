using System.Reflection;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using HPDOS.Harneses.Middleware;

namespace HPD.Agent.Harness.Coding.Tests;

public sealed class FileMutationEventTests
{
    [Fact]
    public void FileMutationAppliedEvent_DoesNotExposeRenderedUnifiedDiffText()
    {
        typeof(FileMutationAppliedEvent).GetProperty("UnifiedDiff").Should().BeNull();
        typeof(FileMutationAppliedEvent).GetProperty("UnifiedDiffTruncated").Should().BeNull();
    }

    [Fact]
    public void FileMutationAppliedEvent_ExposesLibraryNeutralDiffSourceData()
    {
        var mutationEvent = CreateWriteEvent();

        mutationEvent.Before.Text.Should().Be("class A {}\n");
        mutationEvent.After.Text.Should().Be("class A { void M() {} }\n");
        mutationEvent.TextEdits.Should().ContainSingle();
        mutationEvent.Hunks.Should().ContainSingle();
        mutationEvent.DiffStat.AddedLines.Should().Be(1);
        mutationEvent.DiffStat.RemovedLines.Should().Be(1);
    }

    [Fact]
    public void FileMutationAppliedEvent_DoesNotExposeDiffPlexSpecificTypes()
    {
        var eventTypes = new[]
        {
            typeof(FileMutationAppliedEvent),
            typeof(FileWriteAppliedEvent),
            typeof(FileEditAppliedEvent),
            typeof(FileMutationSnapshot),
            typeof(FileMutationTextEdit),
            typeof(FileMutationRange),
            typeof(FileMutationHunk),
            typeof(FileMutationDiffStat),
            typeof(FileMutationNote)
        };

        foreach (var property in eventTypes.SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)))
        {
            var propertyType = UnwrapType(property.PropertyType);
            propertyType.Namespace.Should().NotStartWith("DiffPlex", property.Name);
        }
    }

    [Fact]
    public void ConsumerCanComputeRenderedDiffFromSnapshotText()
    {
        var mutationEvent = CreateWriteEvent();

        var diff = InlineDiffBuilder.Diff(mutationEvent.Before.Text!, mutationEvent.After.Text!);

        diff.Lines.Should().Contain(line => line.Type == ChangeType.Deleted && line.Text == "class A {}");
        diff.Lines.Should().Contain(line => line.Type == ChangeType.Inserted && line.Text == "class A { void M() {} }");
    }

    [Fact]
    public void FileMutationTextEdit_CanOmitOversizedPayloadsIndependently()
    {
        var range = new FileMutationRange(1, 1, 1, 1, 0, 0);
        var textEdit = new FileMutationTextEdit(
            1,
            range,
            range,
            OldText: null,
            NewText: null,
            TextOmitted: true,
            OmissionReason: "text_edit_too_large");

        textEdit.TextOmitted.Should().BeTrue();
        textEdit.OldText.Should().BeNull();
        textEdit.NewText.Should().BeNull();
        textEdit.OmissionReason.Should().Be("text_edit_too_large");
    }

    private static Type UnwrapType(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            return Nullable.GetUnderlyingType(type)!;

        if (type != typeof(string) && type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
            return type.GetGenericArguments()[0];

        return type;
    }

    private static FileWriteAppliedEvent CreateWriteEvent()
    {
        var beforeText = "class A {}\n";
        var afterText = "class A { void M() {} }\n";
        var beforeRange = new FileMutationRange(1, 1, 2, 1, 0, beforeText.Length);
        var afterRange = new FileMutationRange(1, 1, 2, 1, 0, afterText.Length);

        return new FileWriteAppliedEvent
        {
            ToolCallId = "call_1",
            FunctionName = "WriteFile",
            Path = "/tmp/A.cs",
            DisplayPath = "A.cs",
            MutationKind = CodingFileMutationKind.Changed,
            Created = false,
            Changed = true,
            Before = new FileMutationSnapshot(
                beforeText,
                "sha256:before",
                beforeText.Length,
                1,
                "utf-8",
                HasBom: false,
                "lf",
                DateTimeOffset.UnixEpoch,
                TextOmitted: false,
                OmissionReason: null),
            After = new FileMutationSnapshot(
                afterText,
                "sha256:after",
                afterText.Length,
                1,
                "utf-8",
                HasBom: false,
                "lf",
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                TextOmitted: false,
                OmissionReason: null),
            TextEdits =
            [
                new FileMutationTextEdit(
                    1,
                    beforeRange,
                    afterRange,
                    beforeText,
                    afterText,
                    TextOmitted: false,
                    OmissionReason: null)
            ],
            Hunks =
            [
                new FileMutationHunk(
                    1,
                    1,
                    1,
                    1,
                    ["-class A {}", "+class A { void M() {} }"])
            ],
            HunksTruncated = false,
            DiffStat = new FileMutationDiffStat(1, 1, afterText.Length - beforeText.Length, 0),
            Mode = FileWriteMode.Rewrite
        };
    }
}
