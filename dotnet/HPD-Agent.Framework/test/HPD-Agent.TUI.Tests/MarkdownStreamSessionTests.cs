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
    public void EnteringDocumentGlobalModeAdvancesEpochEvenWithoutPublishedStablePrefix()
    {
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "global"));
        session.Append("[id]: https://example.com\n");

        var update = session.Refresh();

        Assert.Equal(1, update.Document.Epoch);
        Assert.Equal(0, update.Document.StableSourceLength);
        Assert.Equal(MarkdownInvalidationKind.FullMessage, update.Invalidation);
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
    [InlineData(MarkdownMessageState.Completed)]
    [InlineData(MarkdownMessageState.Interrupted)]
    [InlineData(MarkdownMessageState.Cancelled)]
    [InlineData(MarkdownMessageState.Failed)]
    public void LifecycleOnlyHiddenStreamsRetainTerminalStateWithoutRetainingSource(MarkdownMessageState state)
    {
        var dispatcher = new QueuedDispatcher();
        var coordinator = new MarkdownStreamCoordinator(dispatcher, static (_, _) =>
            throw new InvalidOperationException("A hidden stream must not publish."));
        var identity = new MarkdownStreamIdentity(MarkdownStreamKind.Reasoning, state.ToString());
        coordinator.Start(identity, new MarkdownMessagePresentation(Visibility: AgentMessageVisibility.Hidden));
        coordinator.Append(identity, "secret");
        switch (state)
        {
            case MarkdownMessageState.Completed: coordinator.Complete(identity); break;
            case MarkdownMessageState.Interrupted: coordinator.Interrupt(identity); break;
            case MarkdownMessageState.Cancelled: coordinator.Cancel(identity); break;
            case MarkdownMessageState.Failed: coordinator.Fail(identity); break;
        }
        dispatcher.Drain();

        Assert.True(coordinator.TryGetTerminalState(identity, out var actual));
        Assert.Equal(state, actual);
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

        Assert.Throws<UnauthorizedAccessException>(() =>
            MarkdownExportPolicy.AuthorizeExact(firstDocument, new ExactSourceAuthorization(false)));
        var authority = MarkdownExportPolicy.AuthorizeExact(firstDocument, new ExactSourceAuthorization(true));
        Assert.Equal("exact\u001bsource", firstDocument.ExportExact(authority));
        Assert.Throws<UnauthorizedAccessException>(() => secondDocument.ExportExact(authority));
    }

    [Fact]
    public void ExactExport_HiddenPresentationCannotBeAuthorized()
    {
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "hidden"));
        session.Append("secret");
        var document = session.Complete().Document with
        {
            Presentation = new MarkdownMessagePresentation(Visibility: AgentMessageVisibility.Hidden)
        };

        Assert.Throws<UnauthorizedAccessException>(() =>
            MarkdownExportPolicy.AuthorizeExact(document, new ExactSourceAuthorization(true)));
        Assert.Throws<UnauthorizedAccessException>(() => document.GetSafeDisplayText());
        Assert.Throws<ArgumentException>(() => new MarkdownStreamSession(
            new(MarkdownStreamKind.Assistant, "hidden-source"),
            new MarkdownMessagePresentation(Visibility: AgentMessageVisibility.Hidden)));
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

    private sealed class ExactSourceAuthorization(bool allow) : IMarkdownExactSourceAuthorization
    {
        public bool CanExportExact(
            MarkdownStreamIdentity identity,
            Guid lineageId,
            MarkdownMessagePresentation presentation) => allow;
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
        Assert.True(session.Projection.Diagnostics.CacheHits > 0);
    }

    [Fact]
    public void StructuredDiagnosticsCountWorkWithoutIncludingSource()
    {
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "diagnostics"));
        session.Append("first\n\nsecond\n");
        var first = session.Refresh().Document;
        var options = new MarkdownLayoutOptions(40, MarkdownTheme.FromTheme(Theme.Default));
        var engine = new MarkdownLayoutEngine();
        _ = session.Projection.Prepare(first, options, engine);
        session.Append("\nthird\n");
        var final = session.Complete().Document;
        _ = session.Projection.Prepare(final, options, engine);

        Assert.Equal(2, session.Diagnostics.DeltasAccepted);
        Assert.Equal("first\n\nsecond\n\nthird\n".Length, session.Diagnostics.Utf16CodeUnitsAppended);
        Assert.InRange(session.Diagnostics.ParseCount, 1, session.Diagnostics.DeltasAccepted);
        Assert.Equal(2, session.Projection.Diagnostics.LayoutCount);
        Assert.True(session.Projection.Diagnostics.CacheHits > 0);
        Assert.DoesNotContain("first", session.Diagnostics.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("first", session.Projection.Diagnostics.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExcessiveNestingFallsBackToLiteralCanonicalTailWithoutLosingSource()
    {
        var source = string.Concat(Enumerable.Repeat("> ", 140)) + "deep";
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "deep"));
        session.Append(source);

        var document = session.Complete().Document;

        Assert.Equal(source, document.GetCanonicalSource());
        Assert.Equal(source, document.UnparsedTail);
        Assert.True(session.Diagnostics.ParseFallbacks > 0);
    }

    [Theory]
    [InlineData("prose\n\n# heading\n\n- one\n- two\n\n> quote\n\n```csharp\nvar x = 1;\n```", 1)]
    [InlineData("| a | b |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |\n\nafter", 7)]
    [InlineData("[use][id]\n\n[id]: https://example.com\n", 5)]
    [InlineData("<div>html</div>\n\nparagraph\r\n\r\n~~strike~~", 3)]
    public void EveryPublicationStableRegionAndEveryResizeMatchColdOracle(string source, int partition)
    {
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "oracle"));
        var engine = new MarkdownLayoutEngine();
        var parser = new MarkdownDocumentParser();
        var pipeline = MarkdownPipelineFactory.CreateDefault();
        for (var offset = 0; offset < source.Length; offset += partition)
        {
            session.Append(source.Substring(offset, Math.Min(partition, source.Length - offset)));
            var update = session.Refresh();
            foreach (var width in new[] { 12, 24, 60 })
            {
                var options = new MarkdownLayoutOptions(width, MarkdownTheme.FromTheme(Theme.Default));
                var projected = session.Projection.ResolveLayout(update.Document, options, engine);
                var coldDocument = parser.Parse(update.Document.Parsed.Source,
                    new MarkdownParseOptions { Pipeline = pipeline });
                var cold = engine.Layout(coldDocument, options);
                var stableOrdinals = update.Document.Parsed.Blocks
                    .Where(block => block.SourceEndExclusive <= update.StableSourceLength)
                    .Select(static block => block.Ordinal).ToHashSet();
                Assert.Equal(
                    BlockFingerprints(cold, stableOrdinals),
                    BlockFingerprints(projected, stableOrdinals));
            }
        }

        var final = session.Complete().Document;
        foreach (var width in new[] { 12, 24, 60 })
        {
            var options = new MarkdownLayoutOptions(width, MarkdownTheme.FromTheme(Theme.Default));
            Assert.Equal(
                LayoutFingerprint(engine.Layout(parser.Parse(source, new MarkdownParseOptions { Pipeline = pipeline }), options)),
                LayoutFingerprint(session.Projection.ResolveLayout(final, options, engine)));
        }
    }

    [Fact]
    public void WeightedProjectionCacheEvictsOldStableBlocksAndReportsReuse()
    {
        var source = string.Join("\n\n", Enumerable.Range(0, 300).Select(index => $"paragraph {index}")) + "\n";
        var session = new MarkdownStreamSession(new(MarkdownStreamKind.Assistant, "lru"));
        session.Append(source);
        var document = session.Refresh().Document;
        var options = new MarkdownLayoutOptions(40, MarkdownTheme.FromTheme(Theme.Default));
        var engine = new MarkdownLayoutEngine();

        _ = session.Projection.ResolveLayout(document, options, engine);
        _ = session.Projection.ResolveLayout(document, options, engine);

        Assert.True(session.Projection.Diagnostics.CacheEvictions > 0);
        Assert.True(session.Projection.Diagnostics.CacheMisses > session.Projection.Diagnostics.CacheHits);
    }

    private static IReadOnlyList<string> LayoutFingerprint(MarkdownLayout layout) => layout.Rows
        .SelectMany(static row => row.Line.Runs.Length == 0
            ? new[] { $"{row.Kind}|{row.BlockOrdinal}|{row.SourceStart}|{row.SourceEndExclusive}|{row.IsDecorative}" }
            : row.Line.Runs.Select(run =>
                $"{row.Kind}|{row.BlockOrdinal}|{row.SourceStart}|{row.SourceEndExclusive}|{row.IsDecorative}|{run.Text}|{run.Style}|{run.Hyperlink?.Destination}"))
        .ToArray();

    private static IReadOnlyList<string> BlockFingerprints(MarkdownLayout layout, HashSet<int> ordinals) => layout.Rows
        .Where(row => row.BlockOrdinal is { } ordinal && ordinals.Contains(ordinal))
        .SelectMany(static row => row.Line.Runs.Select(run =>
            $"{row.BlockOrdinal}|{run.Text}|{run.Style}|{run.Hyperlink?.Destination}|{run.SourceStart}|{run.SourceEndExclusive}"))
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
