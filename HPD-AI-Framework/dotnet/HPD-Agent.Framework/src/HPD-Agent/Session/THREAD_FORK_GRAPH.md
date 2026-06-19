# Thread Fork Graph

`ThreadForkGraph` projects durable thread lineage into user-visible fork choice
groups.

The important distinction is:

- `Thread.ForkedFrom` is literal lineage. It answers: which thread created this
  thread?
- `Thread.ForkedAtMessageId` is the durable split boundary. It answers: where
  did this thread diverge from shared conversation history?
- `ThreadForkGraph.BuildVisibleForkGroups(...)` is the navigation projection. It
  answers: which branches should users see as alternate choices at the same
  conversation point?

Do not calculate branch choices from direct parent thread ids. A fork can be
created from another fork, but still belong to the same user-visible choice
point as an older branch.

## C# DX

```csharp
using HPD.Agent;

IReadOnlyList<Thread> threads = session.Threads;
IReadOnlyList<ThreadForkGroup> groups =
    ThreadForkGraph.BuildVisibleForkGroups(threads);

foreach (var group in groups)
{
    Console.WriteLine(group.Id);
    Console.WriteLine(group.SourceThreadId);
    Console.WriteLine(group.ForkedAtMessageId ?? "root");

    foreach (var member in group.Members)
    {
        Console.WriteLine(
            $"{member.Index + 1}/{group.Members.Count} {member.Thread.Name}");
    }
}
```

`ThreadForkGroup` is intentionally small:

```csharp
public sealed record ThreadForkGroup(
    string Id,
    string SourceThreadId,
    string? ForkedAtMessageId,
    int? ForkedAtMessageIndex,
    int ChoiceMessageIndex,
    IReadOnlyList<ThreadForkGroupMember> Members);

public sealed record ThreadForkGroupMember(
    Thread Thread,
    int Index,
    bool IsSource,
    string? ChoiceMessageId,
    int? ChoiceMessageIndex);
```

Hosts, TUIs, web APIs, and desktop shells should call this projection and map it
to their own DTOs. They should not reimplement fork grouping.

`ForkedAtMessageId` and `ForkedAtMessageIndex` are lineage facts: they identify
the last copied shared message before divergence. `ChoiceMessageIndex` is the
canonical group choice point: root forks use `ChoiceMessageIndex = 0`, and
message-boundary forks use `ForkedAtMessageIndex + 1`.

Inline UI placement is member-local. `ThreadForkGroupMember.ChoiceMessageId`
and `ThreadForkGroupMember.ChoiceMessageIndex` identify the row where this
member represents the choice in its own transcript. This matters because the
same fork group can be selected through a descendant thread whose message ids
or visible continuation differ from the source member. Render controls from the
selected member anchor, not from the group index alone.

For revision-style forks, `ChoiceMessageId` is anchored to the semantic user
input row when the fork metadata contains `inputMessageId`. `ForkedAtMessageId`
is still the durable split boundary, but it is not necessarily the row where an
inline switcher belongs. If no explicit input metadata exists, the graph chooses
the first user message at or after the fork boundary. Only if no user message is
available does it fall back to the next copied message.

This prevents a later-turn edit or retry from placing the branch switcher under
the assistant response that followed the user input. The assistant message may
be the last copied boundary; the visible choice is still the user turn that was
edited or retried.

## Grouping Rules

Only ordinary visible main-agent threads are included:

```csharp
thread.Kind == ThreadKind.MainAgent &&
thread.Visibility == ThreadVisibility.Visible
```

Runtime children and hidden subagent/tool threads remain inspectable thread
scopes, but they are not branch choices in the main conversation.

Root forks group together:

```text
main
  -> retry first user turn
  -> edit first user turn
```

Those branches all share the same root choice point even when one fork was
created from another fork.

Message-boundary forks group by copied message id:

```text
main:     user A -> assistant B -> user C
fork-1:   user A -> assistant B -> edited C
fork-2:   user A -> assistant B -> retried C
```

If `assistant B` was copied into each fork with the same `MessageId`, then forks
created after `assistant B` belong to the same group. This remains true even if
`fork-2` was created from `fork-1` instead of from `main`.

Fork groups are global session facts. A selected path can pass through many
fork groups because choices are message-boundary dependent, not whole-thread
dependent. A UI should only render a group inline for a selected thread when
that selected path actually passes through the group's choice point. Reaching
the same numeric message position on a branch that forked earlier is not
enough.

```text
main:        m1 -> m2 -> m3 -> ... -> m10
early-fork:  m1 -> m2 -> x3 -> ... -> x10
late-fork:   m1 -> m2 -> m3 -> ... -> edited m10
```

When `early-fork` is selected, the `m10` group from `main` is still part of the
session graph, but it is not on the active path. Inline branch controls should
not appear at `x10`.

When a group is on the active path, the selected member supplies the render
anchor:

```text
group:
  forkedAtMessageId = m2
  choiceMessageIndex = 2

members:
  main   -> ChoiceMessageId=m3,  ChoiceMessageIndex=2
  fork-a -> ChoiceMessageId=x3,  ChoiceMessageIndex=2
  fork-b -> ChoiceMessageId=y3,  ChoiceMessageIndex=2
```

The group says these members are alternate choices after `m2`. The member says
which row to draw under for the currently selected path.

## Why Not Direct Parent Groups?

Direct parent navigation fails once users branch from branches.

```text
main
  -> fork-a at message-2
       -> fork-b at message-2
```

`fork-a` and `fork-b` do not have the same direct parent, but users experience
them as alternatives at the same message boundary. The fork graph keeps
`ForkedFrom` for exact lineage while deriving a separate navigation model for
the UI.

## Responsibilities

The core session layer owns:

- preserving fork lineage on `Thread`
- preserving copied `MessageId` values across forks
- deriving semantic fork groups from visible threads
- deriving member-local branch-control anchors
- keeping direct lineage separate from branch navigation

The TypeScript headless layer owns:

- deriving active path choices for the selected thread
- filtering out fork groups that are global but not on the selected path
- identifying whether the selected thread is the exact group member or a
  descendant of that member
- placing inline controls from `ThreadForkGroupMember.ChoiceMessageId`
- providing selectors for inline controls and broader branch pickers

The TypeScript layer intentionally does not infer inline placement from a bare
numeric message index. If a member does not provide a message id that exists in
the current timeline, the inline control is unplaced. That keeps bad or partial
graph data from rendering a switcher under the wrong message.

The host or app owns:

- where branch controls appear
- whether selecting a member switches immediately
- whether edit/retry auto-selects the new fork
- how many fork groups to show at once
- whether controls are rendered inline, in a sidebar, or in a branch map

## Invariants To Test

Keep these cases covered whenever the fork model changes:

- root retry and root edit created from different descendants appear in one root
  group
- same copied message id retried/edited from different descendants appears in
  one message-boundary group
- later fork groups from an ancestor do not become active after the selected
  path forked earlier
- a selected descendant can still show an earlier fork group, with the ancestor
  member marked as the selected path choice
- inline controls use member-local anchors and do not render from a group index
  when no member anchor exists
- `ForkedFrom` still records the direct parent thread even when the fork group
  source is an older ancestor
- hidden/runtime child threads do not appear as visible branch choices
