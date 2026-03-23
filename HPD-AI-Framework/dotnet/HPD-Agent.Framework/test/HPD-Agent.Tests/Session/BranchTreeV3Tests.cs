using Microsoft.Extensions.AI;
using Xunit;
using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Session;

/// <summary>
/// V3 Branch Tree Architecture Tests
/// Tests for tree navigation, sibling ordering, atomic operations, and referential integrity.
/// </summary>
public class BranchTreeV3Tests : AgentTestBase
{
    //──────────────────────────────────────────────────────────────────
    // UNIT TESTS - SCHEMA & SERIALIZATION
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Test01_NewFields_SerializeDeserialize_RoundTrip()
    {
        // Arrange - Create branch with all V3 fields
        var branch = new Branch("session-1", "branch-1")
        {
            SiblingIndex = 2,
            TotalSiblings = 5,
            IsOriginal = false,
            OriginalBranchId = "main",
            PreviousSiblingId = "branch-0",
            NextSiblingId = "branch-2",
            ChildBranches = new List<string> { "child-1", "child-2" }
        };

        // Act - Serialize and deserialize
        var json = System.Text.Json.JsonSerializer.Serialize(branch);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<Branch>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.SiblingIndex);
        Assert.Equal(5, deserialized.TotalSiblings);
        Assert.False(deserialized.IsOriginal);
        Assert.Equal("main", deserialized.OriginalBranchId);
        Assert.Equal("branch-0", deserialized.PreviousSiblingId);
        Assert.Equal("branch-2", deserialized.NextSiblingId);
        Assert.Equal(2, deserialized.ChildBranches.Count);
        Assert.Contains("child-1", deserialized.ChildBranches);
    }

    [Fact]
    public void Test02_DefaultValues_PassInvariantChecks()
    {
        // Arrange & Act - Create new branch with defaults
        var branch = new Branch("session-1", "main");

        // Assert - Should not throw
        branch.ValidateTreeInvariants();

        // Verify defaults
        Assert.Equal(0, branch.SiblingIndex);
        Assert.Equal(1, branch.TotalSiblings);
        Assert.True(branch.IsOriginal);
        Assert.Null(branch.OriginalBranchId);
        Assert.Null(branch.PreviousSiblingId);
        Assert.Null(branch.NextSiblingId);
        Assert.Empty(branch.ChildBranches);
    }

    [Fact]
    public void Test03_BranchCreation_SetsCorrectInitialValues()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");

        // Act
        var branch = session.CreateBranch("main");

        // Assert - V3 defaults
        Assert.Equal(0, branch.SiblingIndex);
        Assert.Equal(1, branch.TotalSiblings);
        Assert.True(branch.IsOriginal);
        Assert.Null(branch.OriginalBranchId);
        Assert.Null(branch.PreviousSiblingId);
        Assert.Null(branch.NextSiblingId);
        Assert.Empty(branch.ChildBranches);
        Assert.Equal(0, branch.TotalForks);
    }

    [Fact]
    public void Test04_BackwardCompatibility_MissingV3Fields_UseSafeDefaults()
    {
        // Arrange - JSON without V3 fields (simulates old data)
        var jsonWithoutV3Fields = """
        {
            "Id": "branch-1",
            "SessionId": "session-1",
            "Messages": [],
            "ForkedFrom": null,
            "ForkedAtMessageIndex": null,
            "CreatedAt": "2025-01-01T00:00:00Z",
            "LastActivity": "2025-01-01T00:00:00Z",
            "Name": null,
            "Description": null,
            "Tags": null,
            "Ancestors": null,
            "MiddlewareState": {}
        }
        """;

        // Act
        var branch = System.Text.Json.JsonSerializer.Deserialize<Branch>(jsonWithoutV3Fields);

        // Assert - Safe defaults applied
        Assert.NotNull(branch);
        Assert.Equal(0, branch.SiblingIndex);
        Assert.Equal(1, branch.TotalSiblings);
        Assert.True(branch.IsOriginal);
        Assert.Null(branch.OriginalBranchId);
        Assert.Empty(branch.ChildBranches);
    }

    [Fact]
    public void Test05_NameField_ProperlySerializedDeserialized()
    {
        // Arrange
        var branch = new Branch("session-1", "branch-1")
        {
            Name = "Experiment A"
        };

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(branch);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<Branch>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("Experiment A", deserialized.Name);
    }

    //──────────────────────────────────────────────────────────────────
    // UNIT TESTS - VALIDATION (ValidateTreeInvariants)
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Test26_IsOriginal_MismatchWithForkedFrom_ThrowsException()
    {
        // Arrange - Invalid state: IsOriginal=true but has ForkedFrom
        var branch = new Branch("session-1", "branch-1")
        {
            IsOriginal = true,
            ForkedFrom = "main", //  Conflict!
            SiblingIndex = 0,
            TotalSiblings = 1
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => branch.ValidateTreeInvariants());
        Assert.Contains("IsOriginal=True but ForkedFrom=main", ex.Message);
    }

    [Fact]
    public void Test27_SiblingIndex_OutOfRange_ThrowsException()
    {
        // Arrange - SiblingIndex >= TotalSiblings
        var branch = new Branch("session-1", "branch-1")
        {
            SiblingIndex = 5,
            TotalSiblings = 3, //  Index out of range!
            IsOriginal = false,
            ForkedFrom = "main"
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => branch.ValidateTreeInvariants());
        Assert.Contains("out of range [0, 3)", ex.Message);
    }

    [Fact]
    public void Test28_TotalSiblings_ZeroOrNegative_ThrowsException()
    {
        // Arrange - Set SiblingIndex < TotalSiblings to bypass range check first
        var branch = new Branch("session-1", "branch-1")
        {
            SiblingIndex = -1, // Set this negative to avoid range check triggering first
            TotalSiblings = 0, //  Must be positive!
            IsOriginal = true
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => branch.ValidateTreeInvariants());
        // The validation will catch TotalSiblings <= 0 or SiblingIndex out of range
        Assert.True(ex.Message.Contains("must be positive") || ex.Message.Contains("out of range"));
    }

    [Fact]
    public void Test29_FirstSibling_WithPreviousPointer_ThrowsException()
    {
        // Arrange - First sibling should have no previous
        var branch = new Branch("session-1", "branch-1")
        {
            SiblingIndex = 0,
            TotalSiblings = 3,
            IsOriginal = true,
            PreviousSiblingId = "some-id" //  First sibling can't have previous!
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => branch.ValidateTreeInvariants());
        Assert.Contains("First sibling (index=0) has PreviousSiblingId", ex.Message);
    }

    [Fact]
    public void Test30_LastSibling_WithNextPointer_ThrowsException()
    {
        // Arrange - Last sibling should have no next
        var branch = new Branch("session-1", "branch-1")
        {
            SiblingIndex = 2,
            TotalSiblings = 3,
            IsOriginal = false,
            ForkedFrom = "main",
            NextSiblingId = "some-id" //  Last sibling can't have next!
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => branch.ValidateTreeInvariants());
        Assert.Contains("Last sibling (index=2) has NextSiblingId", ex.Message);
    }

    [Fact]
    public void Test31_MiddleSibling_MissingPreviousPointer_ThrowsException()
    {
        // Arrange - Middle sibling must have both pointers
        var branch = new Branch("session-1", "branch-1")
        {
            SiblingIndex = 1,
            TotalSiblings = 3,
            IsOriginal = false,
            ForkedFrom = "main",
            PreviousSiblingId = null, //  Middle sibling needs previous!
            NextSiblingId = "branch-2"
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => branch.ValidateTreeInvariants());
        Assert.Contains("Middle sibling (index=1) has null PreviousSiblingId", ex.Message);
    }

    [Fact]
    public void Test31b_MiddleSibling_MissingNextPointer_ThrowsException()
    {
        // Arrange - Middle sibling must have both pointers
        var branch = new Branch("session-1", "branch-1")
        {
            SiblingIndex = 1,
            TotalSiblings = 3,
            IsOriginal = false,
            ForkedFrom = "main",
            PreviousSiblingId = "main",
            NextSiblingId = null //  Middle sibling needs next!
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => branch.ValidateTreeInvariants());
        Assert.Contains("Middle sibling (index=1) has null NextSiblingId", ex.Message);
    }

    [Fact]
    public void Test32_OriginalBranchId_SetOnOriginalBranch_ThrowsException()
    {
        // Arrange - Original branch shouldn't have OriginalBranchId
        var branch = new Branch("session-1", "main")
        {
            IsOriginal = true,
            ForkedFrom = null,
            OriginalBranchId = "some-id", //  Original branches don't have this!
            SiblingIndex = 0,
            TotalSiblings = 1
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => branch.ValidateTreeInvariants());
        Assert.Contains("Original branch should have OriginalBranchId=null", ex.Message);
    }

    //──────────────────────────────────────────────────────────────────
    // UNIT TESTS - HELPER PROPERTIES
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void TestHelpers_IsLeaf_ReturnsTrue_WhenNoChildren()
    {
        // Arrange
        var branch = new Branch("session-1", "branch-1");

        // Act & Assert
        Assert.True(branch.IsLeaf);
        Assert.Equal(0, branch.TotalForks);
    }

    [Fact]
    public void TestHelpers_IsLeaf_ReturnsFalse_WhenHasChildren()
    {
        // Arrange
        var branch = new Branch("session-1", "branch-1")
        {
            ChildBranches = new List<string> { "child-1", "child-2" }
        };

        // Act & Assert
        Assert.False(branch.IsLeaf);
        Assert.Equal(2, branch.TotalForks);
    }

    [Fact]
    public void TestHelpers_IsRoot_ReturnsTrue_WhenNoParent()
    {
        // Arrange
        var branch = new Branch("session-1", "main")
        {
            ForkedFrom = null
        };

        // Act & Assert
        Assert.True(branch.IsRoot);
    }

    [Fact]
    public void TestHelpers_IsRoot_ReturnsFalse_WhenHasParent()
    {
        // Arrange
        var branch = new Branch("session-1", "fork-1")
        {
            ForkedFrom = "main"
        };

        // Act & Assert
        Assert.False(branch.IsRoot);
    }

    //──────────────────────────────────────────────────────────────────
    // INTEGRATION TESTS - ATOMIC FORK
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test09_ForkBranch_UpdatesAllSiblingMetadata_Atomically()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        main.AddMessage(AssistantMessage("Response 1"));
        main.AddMessage(UserMessage("Message 2"));
        await store.SaveBranchAsync("test-session", main);

        // Act - Fork main branch
        main.Session = session;
        var fork1 = await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 1);

        // Assert - Fork has correct metadata
        // main is sibling #0 (the original), fork-1 is sibling #1 — TotalSiblings=2
        var reloadedMain = await store.LoadBranchAsync("test-session", "main");
        var reloadedFork1 = await store.LoadBranchAsync("test-session", "fork-1");

        // Main is sibling #0 in the group it spawned
        Assert.Equal(2, reloadedMain!.TotalSiblings);
        Assert.Equal(0, reloadedMain.SiblingIndex);
        Assert.True(reloadedMain.IsOriginal);
        Assert.Null(reloadedMain.PreviousSiblingId);
        Assert.Equal("fork-1", reloadedMain.NextSiblingId);

        // Fork-1 is sibling #1
        Assert.Equal(2, reloadedFork1!.TotalSiblings);
        Assert.Equal(1, reloadedFork1.SiblingIndex);
        Assert.False(reloadedFork1.IsOriginal);
        Assert.Equal("main", reloadedFork1.PreviousSiblingId);
        Assert.Null(reloadedFork1.NextSiblingId);
    }

    [Fact]
    public async Task Test10_ForkBranch_SetsCorrectSiblingIndex_ChronologicalOrder()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        main.AddMessage(AssistantMessage("Response 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Act - Fork main THREE TIMES at the SAME index
        // This creates three siblings: fork-1, fork-2, fork-3
        // All have ForkedFrom="main", ForkedAtMessageIndex=1
        var fork1 = await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 1);
        await Task.Delay(10); // Ensure time difference

        // Reload main to get updated metadata
        main = (await store.LoadBranchAsync("test-session", "main"))!;
        main.Session = session;
        var fork2 = await agent.ForkBranchAsync(main, "fork-2", fromMessageIndex: 1);
        await Task.Delay(10);

        // Reload main again
        main = (await store.LoadBranchAsync("test-session", "main"))!;
        main.Session = session;
        var fork3 = await agent.ForkBranchAsync(main, "fork-3", fromMessageIndex: 1);

        // Assert - main is sibling #0, fork-1 is #1, fork-2 is #2, fork-3 is #3 (TotalSiblings=4)
        var reloadedMain = await store.LoadBranchAsync("test-session", "main");
        var reloadedFork1 = await store.LoadBranchAsync("test-session", "fork-1");
        var reloadedFork2 = await store.LoadBranchAsync("test-session", "fork-2");
        var reloadedFork3 = await store.LoadBranchAsync("test-session", "fork-3");

        // main is always slot 0
        Assert.Equal(0, reloadedMain!.SiblingIndex);
        Assert.Equal(4, reloadedMain.TotalSiblings);
        Assert.True(reloadedMain.IsOriginal);

        // Forks are ordered chronologically: main(0), fork-1(1), fork-2(2), fork-3(3)
        Assert.Equal(1, reloadedFork1!.SiblingIndex);
        Assert.Equal(2, reloadedFork2!.SiblingIndex);
        Assert.Equal(3, reloadedFork3!.SiblingIndex);

        // All have total = 4
        Assert.Equal(4, reloadedFork1.TotalSiblings);
        Assert.Equal(4, reloadedFork2.TotalSiblings);
        Assert.Equal(4, reloadedFork3.TotalSiblings);

        // Forks are not original
        Assert.False(reloadedFork1.IsOriginal);
        Assert.False(reloadedFork2.IsOriginal);
        Assert.False(reloadedFork3.IsOriginal);
    }

    [Fact]
    public async Task Test11_ForkBranch_LinksNavigationPointers_Bidirectionally()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Act
        var fork1 = await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 0);

        // Assert - main is sibling #0, fork-1 is sibling #1 — linked bidirectionally
        var reloadedMain = await store.LoadBranchAsync("test-session", "main");
        var reloadedFork1 = await store.LoadBranchAsync("test-session", "fork-1");

        // main: first in group, points forward to fork-1
        Assert.Null(reloadedMain!.PreviousSiblingId);
        Assert.Equal("fork-1", reloadedMain.NextSiblingId);

        // fork-1: last in group, points back to main
        Assert.Equal("main", reloadedFork1!.PreviousSiblingId);
        Assert.Null(reloadedFork1.NextSiblingId);
    }

    [Fact]
    public async Task Test12_ForkBranch_UpdatesParentChildBranches_List()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Act
        var fork1 = await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 0);

        // Assert - Parent tracks child
        var reloadedMain = await store.LoadBranchAsync("test-session", "main");
        Assert.Contains("fork-1", reloadedMain!.ChildBranches);
        Assert.Equal(1, reloadedMain.TotalForks);

        // Child references parent
        Assert.Equal("main", fork1.ForkedFrom);
    }

    //──────────────────────────────────────────────────────────────────
    // INTEGRATION TESTS - SIBLING REDESIGN (source = slot 0)
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task N1_SourceBranch_IsSlotZero_AfterFirstFork()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Act - create one fork
        await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 0);

        // Assert - main is sibling #0, fork-1 is sibling #1
        var reloadedMain = await store.LoadBranchAsync("test-session", "main");
        var reloadedFork1 = await store.LoadBranchAsync("test-session", "fork-1");

        Assert.Equal(0, reloadedMain!.SiblingIndex);
        Assert.Equal(2, reloadedMain.TotalSiblings);
        Assert.True(reloadedMain.IsOriginal);
        Assert.Null(reloadedMain.PreviousSiblingId);
        Assert.Equal("fork-1", reloadedMain.NextSiblingId);

        Assert.Equal(1, reloadedFork1!.SiblingIndex);
        Assert.Equal(2, reloadedFork1.TotalSiblings);
        Assert.False(reloadedFork1.IsOriginal);
        Assert.Equal("main", reloadedFork1.PreviousSiblingId);
        Assert.Null(reloadedFork1.NextSiblingId);
    }

    [Fact]
    public async Task N2_ForkTwiceFromSameSource_SourceRemainsSlotZero()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Act - fork twice at the same index
        await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 0);
        await Task.Delay(10);
        main = (await store.LoadBranchAsync("test-session", "main"))!;
        main.Session = session;
        await agent.ForkBranchAsync(main, "fork-2", fromMessageIndex: 0);

        // Assert - main(0), fork-1(1), fork-2(2) — TotalSiblings=3 on all
        var reloadedMain = await store.LoadBranchAsync("test-session", "main");
        var reloadedFork1 = await store.LoadBranchAsync("test-session", "fork-1");
        var reloadedFork2 = await store.LoadBranchAsync("test-session", "fork-2");

        Assert.Equal(0, reloadedMain!.SiblingIndex);
        Assert.Equal(3, reloadedMain.TotalSiblings);
        Assert.True(reloadedMain.IsOriginal);

        Assert.Equal(1, reloadedFork1!.SiblingIndex);
        Assert.Equal(3, reloadedFork1.TotalSiblings);
        Assert.False(reloadedFork1.IsOriginal);

        Assert.Equal(2, reloadedFork2!.SiblingIndex);
        Assert.Equal(3, reloadedFork2.TotalSiblings);
        Assert.False(reloadedFork2.IsOriginal);
    }

    [Fact]
    public async Task N3_ForkAtDifferentMessageIndices_IndependentGroups()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 0"));
        main.AddMessage(AssistantMessage("Response 0"));
        main.AddMessage(UserMessage("Message 2"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Act - fork at index 0 and at index 2 (independent sibling groups)
        await agent.ForkBranchAsync(main, "fork-at-0", fromMessageIndex: 0);
        main = (await store.LoadBranchAsync("test-session", "main"))!;
        main.Session = session;
        await agent.ForkBranchAsync(main, "fork-at-2", fromMessageIndex: 2);

        // Assert - two forks with different ForkedAtMessageIndex are separate groups
        var forkAt0 = await store.LoadBranchAsync("test-session", "fork-at-0");
        var forkAt2 = await store.LoadBranchAsync("test-session", "fork-at-2");

        Assert.Equal(0, forkAt0!.ForkedAtMessageIndex);
        Assert.Equal(2, forkAt2!.ForkedAtMessageIndex);
        Assert.Equal("main", forkAt0.ForkedFrom);
        Assert.Equal("main", forkAt2.ForkedFrom);

        // Each is sibling #1 in its own group (main is #0 in both)
        Assert.Equal(1, forkAt0.SiblingIndex);
        Assert.Equal(1, forkAt2.SiblingIndex);
        Assert.Equal(2, forkAt0.TotalSiblings);
        Assert.Equal(2, forkAt2.TotalSiblings);
    }

    [Fact]
    public async Task N4_ThirdForkFromSamePoint_PreviousForkIndexStable()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Act - create 3 forks; verify existing forks keep stable indices
        await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 0);
        await Task.Delay(10);
        main = (await store.LoadBranchAsync("test-session", "main"))!;
        main.Session = session;
        await agent.ForkBranchAsync(main, "fork-2", fromMessageIndex: 0);
        await Task.Delay(10);
        main = (await store.LoadBranchAsync("test-session", "main"))!;
        main.Session = session;
        await agent.ForkBranchAsync(main, "fork-3", fromMessageIndex: 0);

        // Assert - fork-1 stays at index 1 (never re-ordered)
        var f1 = await store.LoadBranchAsync("test-session", "fork-1");
        var f2 = await store.LoadBranchAsync("test-session", "fork-2");
        var f3 = await store.LoadBranchAsync("test-session", "fork-3");

        Assert.Equal(1, f1!.SiblingIndex);
        Assert.Equal(2, f2!.SiblingIndex);
        Assert.Equal(3, f3!.SiblingIndex);
        Assert.Equal(4, f1.TotalSiblings);
        Assert.Equal(4, f2.TotalSiblings);
        Assert.Equal(4, f3.TotalSiblings);
    }

    [Fact]
    public async Task N5_OriginalBranchId_IsSourceBranchId_ForAllForks()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var agent = await CreateAgentWithStore(store);
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var main = session.CreateBranch("main");
        main.AddMessage(UserMessage("Message 1"));
        await store.SaveBranchAsync("test-session", main);
        main.Session = session;

        // Act - two forks from main
        await agent.ForkBranchAsync(main, "fork-1", fromMessageIndex: 0);
        main = (await store.LoadBranchAsync("test-session", "main"))!;
        main.Session = session;
        await agent.ForkBranchAsync(main, "fork-2", fromMessageIndex: 0);

        // Assert - source (main) has no OriginalBranchId; forks point to main
        var reloadedMain = await store.LoadBranchAsync("test-session", "main");
        var reloadedFork1 = await store.LoadBranchAsync("test-session", "fork-1");
        var reloadedFork2 = await store.LoadBranchAsync("test-session", "fork-2");

        Assert.Null(reloadedMain!.OriginalBranchId);
        Assert.True(reloadedMain.IsOriginal);
        Assert.Equal("main", reloadedFork1!.OriginalBranchId);
        Assert.Equal("main", reloadedFork2!.OriginalBranchId);
        Assert.False(reloadedFork1.IsOriginal);
        Assert.False(reloadedFork2.IsOriginal);
    }

    //──────────────────────────────────────────────────────────────────
    // HELPER METHODS
    //──────────────────────────────────────────────────────────────────

    private async Task<HPD.Agent.Agent> CreateAgentWithStore(ISessionStore store)
    {
        return await new AgentBuilder(DefaultConfig(), new TestProviderRegistry(new FakeChatClient()))
            .WithSessionStore(store)
            .BuildAsync(CancellationToken.None);
    }
}
