using HPD.Agent.TUI.Markdown;
using HPD.Agent;
using HPD.TUI.Core;
using HPD.TUI.Markdown;

namespace HPD.Agent.TUI.Tests;

public sealed class MarkdownStreamSessionTests
{
    [Fact]
    public void Append_DoesNotParseIncompletePhysicalLine()
    {
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "m1"));
        var change = session.Append("# partial");

        var update = session.Refresh();

        Assert.False(change.CompletedPhysicalLine);
        Assert.Equal(string.Empty, update.Document.Parsed.Source);
        Assert.Equal("# partial", update.Document.UnparsedTail);
        Assert.Equal("# partial", update.Document.GetCanonicalSource());
    }

    [Fact]
    public void Refresh_HoldsFinalParsedBlockMutable()
    {
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "m1"));
        session.Append("first\n\nsecond\n");

        var update = session.Refresh();

        Assert.Equal(update.Document.Parsed.Blocks[^1].SourceStart, update.StableSourceLength);
        Assert.Equal(MarkdownInvalidationKind.StableAppendAndMutableTail, update.Invalidation);
    }

    [Fact]
    public void Complete_ParsesUnterminatedTailWithoutSyntheticNewline()
    {
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "m1"));
        session.Append("final");

        var update = session.Complete();

        Assert.Equal("final", update.Document.Parsed.Source);
        Assert.Empty(update.Document.UnparsedTail);
        Assert.Equal(MarkdownMessageState.Completed, update.Document.State);
        Assert.Throws<InvalidOperationException>(() => session.Append("late"));
    }

    [Fact]
    public void DuplicateStartLineagesCannotShareProjectionIdentity()
    {
        var identity = new MarkdownStreamIdentity(MarkdownStreamKind.Assistant, "same");
        var first = new MarkdownStreamSession(identity);
        var second = new MarkdownStreamSession(identity);

        Assert.NotEqual(first.LineageId, second.LineageId);
        Assert.NotSame(first.Projection, second.Projection);
    }

    [Theory]
    [InlineData(MarkdownMessageState.Completed)]
    [InlineData(MarkdownMessageState.Interrupted)]
    [InlineData(MarkdownMessageState.Cancelled)]
    [InlineData(MarkdownMessageState.Failed)]
    public void TerminalTransitions_ParseAndRetainExactAcceptedSource(MarkdownMessageState state)
    {
        const string source = "first\r\n\r\n[link][id]\r\n\r\n[id]: https://example.com";
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "m1"));
        session.Append(source);

        var update = state switch
        {
            MarkdownMessageState.Completed => session.Complete(),
            MarkdownMessageState.Interrupted => session.Interrupt(),
            MarkdownMessageState.Cancelled => session.Cancel(),
            _ => session.Fail()
        };

        Assert.Equal(source, update.Document.GetCanonicalSource());
        Assert.Equal(source.Length, update.Document.StableSourceLength);
        Assert.Equal(state, update.Document.State);
    }

    [Fact]
    public void ReferenceDefinitions_RevokeStablePrefixAndAdvanceEpoch()
    {
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "m1"));
        session.Append("first\n\nsecond\n");
        var local = session.Refresh();
        session.Append("\n[id]: https://example.com\n");

        var global = session.Refresh();

        Assert.True(local.StableSourceLength > 0);
        Assert.Equal(0, global.StableSourceLength);
        Assert.Equal(local.Document.Epoch + 1, global.Document.Epoch);
        Assert.Equal(MarkdownInvalidationKind.FullMessage, global.Invalidation);
    }

    [Fact]
    public void Coordinator_EndBeforeQueuedRefresh_PublishesOneExactTerminalDocument()
    {
        var dispatcher = new QueuedDispatcher();
        var updates = new List<MarkdownStreamUpdate>();
        var coordinator = new MarkdownStreamCoordinator(dispatcher, (update, _) => updates.Add(update));
        var identity = new MarkdownStreamIdentity(MarkdownStreamKind.Assistant, "m1");
        coordinator.Start(identity);
        coordinator.Append(identity, "unterminated");
        coordinator.Complete(identity);

        dispatcher.Drain();

        var terminal = Assert.Single(updates);
        Assert.Equal("unterminated", terminal.Document.GetCanonicalSource());
        Assert.Equal(MarkdownMessageState.Completed, terminal.Document.State);
    }

    [Fact]
    public void Coordinator_DuplicateStartInterruptsOldLineageAndIsolatesReplacement()
    {
        var dispatcher = new QueuedDispatcher();
        var updates = new List<MarkdownStreamUpdate>();
        var diagnostics = new List<string>();
        var coordinator = new MarkdownStreamCoordinator(dispatcher, (update, _) => updates.Add(update), diagnostics.Add);
        var identity = new MarkdownStreamIdentity(MarkdownStreamKind.Assistant, "same");
        coordinator.Start(identity);
        coordinator.Append(identity, "old");
        coordinator.Start(identity);
        coordinator.Append(identity, "new");
        coordinator.Complete(identity);

        dispatcher.Drain();

        Assert.Collection(updates,
            old => { Assert.Equal("old", old.Document.GetCanonicalSource()); Assert.Equal(MarkdownMessageState.Interrupted, old.Document.State); },
            replacement => { Assert.Equal("new", replacement.Document.GetCanonicalSource()); Assert.Equal(MarkdownMessageState.Completed, replacement.Document.State); });
        Assert.NotEqual(updates[0].Document.LineageId, updates[1].Document.LineageId);
        Assert.Single(diagnostics);
    }

    [Fact]
    public void Coordinator_DeltaBeforeStartAndHiddenSourceNeverPublishContent()
    {
        var dispatcher = new QueuedDispatcher();
        var updates = new List<MarkdownStreamUpdate>();
        var diagnostics = new List<string>();
        var coordinator = new MarkdownStreamCoordinator(dispatcher, (update, _) => updates.Add(update), diagnostics.Add);
        var missing = new MarkdownStreamIdentity(MarkdownStreamKind.Reasoning, "missing");
        var hidden = new MarkdownStreamIdentity(MarkdownStreamKind.Assistant, "hidden");
        coordinator.Append(missing, "protected");
        coordinator.Start(hidden, new MarkdownMessagePresentation(Visibility: AgentMessageVisibility.Hidden));
        coordinator.Append(hidden, "secret");
        coordinator.Complete(hidden);

        dispatcher.Drain();

        Assert.Empty(updates);
        Assert.Single(diagnostics);
        Assert.DoesNotContain("protected", diagnostics[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("# Heading\n\nparagraph with [link](https://example.com)\n")]
    [InlineData("| a | b |\n|---|---|\n| 1 | 2 |\n")]
    [InlineData("> quote\n> continued\n\n- [x] task\n")]
    [InlineData("```csharp\npublic record Value(int X);\n```")]
    [InlineData("café 👨‍👩‍👧‍👦\r\n\r\nfinal")]
    public void CharacterPartition_FinalLayoutEqualsColdOneShot(string source)
    {
        var streamed = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "streamed"));
        foreach (var character in source)
        {
            var change = streamed.Append(character.ToString());
            if (change.CompletedPhysicalLine) streamed.Refresh();
        }
        var streamedDocument = streamed.Complete().Document;

        var cold = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "cold"));
        cold.Append(source);
        var coldDocument = cold.Complete().Document;
        var options = new MarkdownLayoutOptions(32, MarkdownTheme.FromTheme(Theme.Default));
        var engine = new MarkdownLayoutEngine();
        var streamedLayout = streamed.Projection.ResolveLayout(streamedDocument, options, engine);
        var coldLayout = cold.Projection.ResolveLayout(coldDocument, options, engine);

        Assert.Equal(source, streamedDocument.GetCanonicalSource());
        Assert.Equal(LayoutFingerprint(coldLayout), LayoutFingerprint(streamedLayout));
    }

    [Fact]
    public void Selection_ExcludesDecorativeListMarkerAndNeutralizesControls()
    {
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "selection"));
        session.Append("- safe\u001b[31m text");
        var document = session.Complete().Document;
        var layout = session.Projection.Prepare(document,
            new(40, MarkdownTheme.FromTheme(Theme.Default)), new MarkdownLayoutEngine());

        var copied = session.Projection.GetSafeClipboardText(layout, new(0, 0, layout.Height - 1, 40));

        Assert.DoesNotContain('•', copied);
        Assert.DoesNotContain('\u001b', copied);
        Assert.Contains("safe�[", copied);
        Assert.Equal("- safe�[31m text", document.GetSafeDisplayText());
    }

    [Fact]
    public void ExactExport_RequiresPrivilegeAndIsBoundToOneLineage()
    {
        var first = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "same"));
        first.Append("exact\u001bsource");
        var firstDocument = first.Complete().Document;
        var second = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "same"));
        second.Append("replacement");
        var secondDocument = second.Complete().Document;

        Assert.Throws<UnauthorizedAccessException>(() => MarkdownExportPolicy.AuthorizeExact(firstDocument, privileged: false));
        var authority = MarkdownExportPolicy.AuthorizeExact(firstDocument, privileged: true);
        Assert.Equal("exact\u001bsource", firstDocument.ExportExact(authority));
        Assert.Throws<UnauthorizedAccessException>(() => secondDocument.ExportExact(authority));
    }

    [Fact]
    public void ExactExport_HiddenPresentationCannotBeAuthorized()
    {
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "hidden"),
            new MarkdownMessagePresentation(Visibility: AgentMessageVisibility.Hidden));
        session.Append("secret");
        var document = session.Complete().Document;

        Assert.Throws<UnauthorizedAccessException>(() => MarkdownExportPolicy.AuthorizeExact(document, privileged: true));
    }

    [Fact]
    public void Prepare_IdenticalRevisionAndSemanticKeyReturnsSameLayout()
    {
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "memo"));
        session.Append("# retained");
        var document = session.Complete().Document;
        var options = new MarkdownLayoutOptions(40, MarkdownTheme.FromTheme(Theme.Default));
        var engine = new MarkdownLayoutEngine();

        var first = session.Projection.Prepare(document, options, engine);
        var second = session.Projection.Prepare(document, options, engine);

        Assert.Same(first, second);
    }

    [Fact]
    public void AppendOnlyGrowth_RetainsPreviouslyProvenStableBlockLayout()
    {
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "stable"));
        var options = new MarkdownLayoutOptions(40, MarkdownTheme.FromTheme(Theme.Default));
        var engine = new MarkdownLayoutEngine();
        session.Append("first\n\nsecond\n");
        var firstDocument = session.Refresh().Document;
        var firstLayout = session.Projection.ResolveLayout(firstDocument, options, engine);
        session.Append("\nthird\n");
        var secondDocument = session.Refresh().Document;
        var secondLayout = session.Projection.ResolveLayout(secondDocument, options, engine);

        Assert.Same(firstLayout.Blocks[0], secondLayout.Blocks[0]);
    }

    private static IReadOnlyList<string> LayoutFingerprint(MarkdownLayout layout) => layout.Rows
        .SelectMany(static row => row.Line.Runs.Length == 0
            ? new[] { $"{row.Kind}|{row.BlockOrdinal}|{row.SourceStart}|{row.SourceEndExclusive}|{row.IsDecorative}" }
            : row.Line.Runs.Select(run =>
                $"{row.Kind}|{row.BlockOrdinal}|{row.SourceStart}|{row.SourceEndExclusive}|{row.IsDecorative}|{run.Text}|{run.Style}|{run.Hyperlink?.Destination}"))
        .ToArray();

    private sealed class QueuedDispatcher : IAgentTuiDispatcher
    {
        private readonly Queue<Action> _callbacks = [];
        public bool CheckAccess() => true;
        public void Post(Action callback) => _callbacks.Enqueue(callback);
        public ValueTask InvokeAsync(Action callback, CancellationToken cancellationToken = default) { Post(callback); return ValueTask.CompletedTask; }
        public ValueTask InvokeAsync(Func<ValueTask> callback, CancellationToken cancellationToken = default)
        {
            Post(() => callback().GetAwaiter().GetResult());
            return ValueTask.CompletedTask;
        }
        public void Drain()
        {
            while (_callbacks.TryDequeue(out var callback)) callback();
        }
    }
}
