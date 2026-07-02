using FluentAssertions;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Tests;

public sealed class AgentTuiDialogFlowContextTests
{
    [Fact]
    public async Task SelectAsync_EscapeReturnsBack()
    {
        var flow = new AgentTuiDialogFlowContext(new QueuedDialogService([null]));

        var result = await flow.SelectAsync(
            "Root",
            ["one"],
            static value => value);

        result.IsBack.Should().BeTrue();
        result.IsCanceled.Should().BeFalse();
    }

    [Fact]
    public async Task SelectAsync_CanceledChildStepGoesBack()
    {
        var flow = new AgentTuiDialogFlowContext(new QueuedDialogService(["one", null]));

        var root = await flow.SelectAsync(
            "Root",
            ["one"],
            static value => value);
        var child = await flow.SelectAsync(
            "Child",
            ["two"],
            static value => value);

        root.IsSubmitted.Should().BeTrue();
        root.Value.Should().Be("one");
        child.IsBack.Should().BeTrue();
        child.IsCanceled.Should().BeFalse();
    }

    private sealed class QueuedDialogService(IReadOnlyList<string?> selections) : IAgentTuiDialogService
    {
        private int _selectionIndex;

        public bool HasOpenDialog => false;

        public Task<AgentTuiDialogResult<TResult>> ShowAsync<TResult>(
            string key,
            Func<AgentTuiDialogContext<TResult>, IComponent> componentFactory,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<TResult>.Dismissed());

        public bool Close(string key) => false;

        public bool CloseTop() => false;

        public Task<AgentTuiDialogResult<bool>> ConfirmAsync(
            string title,
            bool? defaultValue = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(defaultValue is null
                ? AgentTuiDialogResult<bool>.Dismissed()
                : AgentTuiDialogResult<bool>.Submitted(defaultValue.Value));

        public Task<AgentTuiDialogResult<T>> SelectAsync<T>(
            string title,
            IReadOnlyList<T> options,
            Func<T, string> titleSelector,
            CancellationToken cancellationToken = default)
        {
            var selected = _selectionIndex < selections.Count
                ? selections[_selectionIndex++]
                : null;
            if (selected is null)
                return Task.FromResult(AgentTuiDialogResult<T>.Dismissed());

            var match = options.FirstOrDefault(option =>
                string.Equals(titleSelector(option), selected, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(match is null
                ? AgentTuiDialogResult<T>.Dismissed()
                : AgentTuiDialogResult<T>.Submitted(match));
        }

        public Task<AgentTuiDialogResult<string>> InputAsync(
            string title,
            string? defaultValue = null,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(defaultValue is null
                ? AgentTuiDialogResult<string>.Dismissed()
                : AgentTuiDialogResult<string>.Submitted(defaultValue));

        public Task<AgentTuiDialogResult<string>> SecretInputAsync(
            string title,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<string>.Dismissed());
    }
}
