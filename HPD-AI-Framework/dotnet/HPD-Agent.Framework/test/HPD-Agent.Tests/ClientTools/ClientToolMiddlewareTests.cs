// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent;
using HPD.Agent.ClientTools;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using System.Text.Json;
using Xunit;

namespace HPD.Agent.Tests.ClientTools;

/// <summary>
/// Unit tests for ClientToolMiddleware.
/// Tests ToolHarness registration, tool visibility, and tool invocation interception.
/// </summary>
public class ClientToolMiddlewareTests
{
    // ============================================
    // BeforeMessageTurnAsync - ToolHarness Registration Tests
    // ============================================

    [Fact]
    public async Task BeforeMessageTurn_NoAgentClientInput_DoesNothing()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateContext();

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert - state unchanged
        Assert.Null(context.Analyze(s => s.MiddlewareState.ClientTool()));
    }

    [Fact]
    public async Task BeforeMessageTurn_WithToolss_RegistersToolHarnesses()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateContext();
        var clientinput = new AgentClientInput
        {
            clientToolHarnesses = new[]
            {
                CreateTestToolHarness("ToolHarness1", tools: new[] { CreateTestTool("Tool1") }),
                CreateTestToolHarness("ToolHarness2", tools: new[] { CreateTestTool("Tool2") })
            }
        };
        context.RunConfig.ClientToolInput = clientinput;

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var state = context.Analyze(s => s.MiddlewareState.ClientTool());
        Assert.NotNull(state);
        Assert.Equal(2, state.RegisteredToolHarnesses.Count);
        Assert.True(state.RegisteredToolHarnesses.ContainsKey("ToolHarness1"));
        Assert.True(state.RegisteredToolHarnesses.ContainsKey("ToolHarness2"));
    }

    [Fact]
    public async Task BeforeMessageTurn_WithExpandedContainers_MarksAsExpanded()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateContext();
        var clientinput = new AgentClientInput
        {
            clientToolHarnesses = new[]
            {
                CreateTestToolHarness("ToolHarness1", startCollapsed: true),
                CreateTestToolHarness("ToolHarness2", startCollapsed: true)
            },
            ExpandedContainers = new HashSet<string> { "ToolHarness1" }
        };
        context.RunConfig.ClientToolInput = clientinput;

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var state = context.Analyze(s => s.MiddlewareState.ClientTool());
        Assert.NotNull(state);
        Assert.Contains("ToolHarness1", state.ExpandedToolHarnesses);
        Assert.DoesNotContain("ToolHarness2", state.ExpandedToolHarnesses);
    }

    [Fact]
    public async Task BeforeMessageTurn_WithHiddenTools_MarksAsHidden()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateContext();
        var clientinput = new AgentClientInput
        {
            clientToolHarnesses = new[]
            {
                CreateTestToolHarness("ToolHarness1", tools: new[]
                {
                    CreateTestTool("Tool1"),
                    CreateTestTool("Tool2")
                })
            },
            HiddenTools = new HashSet<string> { "Tool1" }
        };
        context.RunConfig.ClientToolInput = clientinput;

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var state = context.Analyze(s => s.MiddlewareState.ClientTool());
        Assert.NotNull(state);
        Assert.Contains("Tool1", state.HiddenTools);
        Assert.DoesNotContain("Tool2", state.HiddenTools);
    }

    [Fact]
    public async Task BeforeMessageTurn_WithContext_StoresContext()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateContext();
        var clientinput = new AgentClientInput
        {
            clientToolHarnesses = new[] { CreateTestToolHarness("ToolHarness1") },
            Context = new[]
            {
                new ContextItem("User preferences", "dark-theme", "prefs"),
                new ContextItem("Current page", "/dashboard", "page")
            }
        };
        context.RunConfig.ClientToolInput = clientinput;

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var state = context.Analyze(s => s.MiddlewareState.ClientTool());
        Assert.NotNull(state);
        Assert.Equal(2, state.Context.Count);
        Assert.True(state.Context.ContainsKey("prefs"));
        Assert.True(state.Context.ContainsKey("page"));
    }

    [Fact]
    public async Task BeforeMessageTurn_WithJsonContext_StoresStructuredContext()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateContext();
        var activeView = JsonSerializer.SerializeToElement(new
        {
            view = "chat",
            selectedArtifactId = "artifact-1"
        });
        var clientinput = new AgentClientInput
        {
            clientToolHarnesses = new[] { CreateTestToolHarness("ToolHarness1") },
            Context = new[]
            {
                new ContextItem("Active HPD-OS view", activeView, "hpdos.activeView")
            }
        };
        context.RunConfig.ClientToolInput = clientinput;

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var state = context.Analyze(s => s.MiddlewareState.ClientTool());
        Assert.NotNull(state);
        var item = Assert.Single(state.Context).Value;
        Assert.Equal(JsonValueKind.Object, item.Value.ValueKind);
        Assert.Equal("chat", item.Value.GetProperty("view").GetString());
        Assert.Contains("selectedArtifactId", item.ValueText);
    }

    [Fact]
    public void AgentClientInput_WithJsonContext_DeserializesStructuredContext()
    {
        // Arrange
        const string json = """
            {
              "context": [
                {
                  "key": "hpdos.activeView",
                  "description": "The current HPD-OS shell view.",
                  "value": {
                    "view": "chat",
                    "selectedArtifactId": "artifact-1"
                  }
                }
              ]
            }
            """;

        // Act
        var input = JsonSerializer.Deserialize(json, HPDJsonContext.Default.AgentClientInput);

        // Assert
        Assert.NotNull(input);
        var item = Assert.Single(input.Context!);
        Assert.Equal("hpdos.activeView", item.EffectiveKey);
        Assert.Equal(JsonValueKind.Object, item.Value.ValueKind);
        Assert.Equal("chat", item.Value.GetProperty("view").GetString());
    }

    [Fact]
    public async Task BeforeMessageTurn_WithState_StoresState()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateContext();
        var appState = JsonSerializer.SerializeToElement(new { cartItems = 3, userId = "user123" });
        var clientinput = new AgentClientInput
        {
            clientToolHarnesses = new[] { CreateTestToolHarness("ToolHarness1") },
            State = appState
        };
        context.RunConfig.ClientToolInput = clientinput;

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var state = context.Analyze(s => s.MiddlewareState.ClientTool());
        Assert.NotNull(state);
        Assert.NotNull(state.State);
    }

    [Fact]
    public async Task BeforeMessageTurn_ResetClientState_ClearsExistingState()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();

        // First call - register ToolHarnesses
        var context1 = CreateContext();
        var clientinput1 = new AgentClientInput
        {
            clientToolHarnesses = new[] { CreateTestToolHarness("OldToolHarness") }
        };
        context1.RunConfig.ClientToolInput = clientinput1;
        await middleware.BeforeMessageTurnAsync(context1, CancellationToken.None);

        // Second call with reset
        var context2 = CreateContext(context1.State);
        var clientinput2 = new AgentClientInput
        {
            clientToolHarnesses = new[] { CreateTestToolHarness("NewToolHarness") },
            ResetClientState = true
        };
        context2.RunConfig.ClientToolInput = clientinput2;

        // Act
        await middleware.BeforeMessageTurnAsync(context2, CancellationToken.None);

        // Assert - only new ToolHarness registered
        var state = context2.State.MiddlewareState.ClientTool();
        Assert.NotNull(state);
        Assert.Single(state.RegisteredToolHarnesses);
        Assert.True(state.RegisteredToolHarnesses.ContainsKey("NewToolHarness"));
        Assert.False(state.RegisteredToolHarnesses.ContainsKey("OldToolHarness"));
    }

    // ============================================
    // BeforeIterationAsync - Tool Visibility Tests
    // ============================================

    [Fact]
    public async Task BeforeIteration_NoState_DoesNothing()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateIterationContext();

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert - no exception, tools unchanged
        Assert.Empty(context.Options.Tools);
    }

    [Fact]
    public async Task BeforeIteration_WithExpandedToolHarness_AddsToolsToOptions()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();

        // Set up state with registered and expanded ToolHarness
        var state = new ClientToolStateData()
            .WithRegisteredToolHarness(CreateTestToolHarness("ToolHarness1", tools: new[]
            {
                CreateTestTool("Tool1"),
                CreateTestTool("Tool2")
            }))
            .WithExpandedToolHarness("ToolHarness1");

        var agentState = CreateEmptyState() with
        {
            MiddlewareState = new MiddlewareState().WithClientTool(state)
        };

        var context = CreateIterationContext(agentState);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert - tools should be added
        Assert.True(context.Options.Tools.Count >= 2);
    }

    [Fact]
    public async Task BeforeIteration_WithHiddenTool_ExcludesTool()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();

        var state = new ClientToolStateData()
            .WithRegisteredToolHarness(CreateTestToolHarness("ToolHarness1", tools: new[]
            {
                CreateTestTool("Tool1"),
                CreateTestTool("Tool2")
            }))
            .WithExpandedToolHarness("ToolHarness1")
            .WithHiddenTool("Tool1");

        var agentState = CreateEmptyState() with
        {
            MiddlewareState = new MiddlewareState().WithClientTool(state)
        };

        var context = CreateIterationContext(agentState);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert - Tool1 should be excluded
        var toolNames = context.Options.Tools.OfType<AIFunction>().Select(f => f.Name).ToList();
        Assert.DoesNotContain("Tool1", toolNames);
        Assert.Contains("Tool2", toolNames);
    }

    // ============================================
    // ToolHarness Validation Tests
    // ============================================

    [Fact]
    public async Task BeforeMessageTurn_CollapsedToolHarnessWithoutDescription_ThrowsValidationError()
    {
        // Arrange
        var middleware = new ClientToolMiddleware(new ClientToolConfig { ValidateSchemaOnRegistration = true });
        var context = CreateBeforeMessageTurnContext();

        // Create ToolHarness with startCollapsed=true but no description
        var ToolHarness = new clientToolHarnessDefinition(
            Name: "BadToolHarness",
            Description: null, // No description!
            Tools: new[] { CreateTestTool("Tool1") },
            StartCollapsed: true
        );

        var clientinput = new AgentClientInput { clientToolHarnesses = new[] { ToolHarness } };
        context.RunConfig.ClientToolInput = clientinput;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await middleware.BeforeMessageTurnAsync(context, CancellationToken.None));
    }

    // ============================================
    // ClientToolAugmentation Tests
    // ============================================

    [Fact]
    public async Task BeforeIteration_WithPendingAugmentation_AppliesChanges()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();

        var augmentation = new ClientToolAugmentation
        {
            ExpandToolHarnesses = new HashSet<string> { "ToolHarness2" },
            HideTools = new HashSet<string> { "Tool1" }
        };

        var state = new ClientToolStateData()
            .WithRegisteredToolHarness(CreateTestToolHarness("ToolHarness1", tools: new[] { CreateTestTool("Tool1") }))
            .WithRegisteredToolHarness(CreateTestToolHarness("ToolHarness2", tools: new[] { CreateTestTool("Tool2") }))
            .WithExpandedToolHarness("ToolHarness1")
            .WithPendingAugmentation(augmentation);

        var agentState = CreateEmptyState() with
        {
            MiddlewareState = new MiddlewareState().WithClientTool(state)
        };

        var context = CreateIterationContext(agentState);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert
        var updatedState = context.Analyze(s => s.MiddlewareState.ClientTool());
        Assert.NotNull(updatedState);

        // Augmentation should have been applied
        Assert.Contains("ToolHarness2", updatedState.ExpandedToolHarnesses);
        Assert.Contains("Tool1", updatedState.HiddenTools);

        // Augmentation should be cleared
        Assert.Null(updatedState.PendingAugmentation);
    }

    // ============================================
    // Client Skill Tests
    // ============================================

    [Fact]
    public async Task BeforeMessageTurn_WithSkills_RegistersSkills()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateBeforeMessageTurnContext();

        var skill = new ClientSkillDefinition(
            Name: "CheckoutWorkflow",
            Description: "Guides through checkout process",
            SystemPrompt: "1. Verify cart\n2. Get payment\n3. Confirm order",
            References: new[] { new ClientSkillReference("AddToCart") }
        );

        var ToolHarness = new clientToolHarnessDefinition(
            Name: "ECommerce",
            Description: "E-commerce tools",
            Tools: new[] { CreateTestTool("AddToCart") },
            Skills: new[] { skill },
            StartCollapsed: false
        );

        var clientinput = new AgentClientInput { clientToolHarnesses = new[] { ToolHarness } };
        context.RunConfig.ClientToolInput = clientinput;

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var state = context.Analyze(s => s.MiddlewareState.ClientTool());
        Assert.NotNull(state);
        Assert.Single(state.RegisteredToolHarnesses);
        Assert.NotNull(state.RegisteredToolHarnesses["ECommerce"].Skills);
        Assert.Single(state.RegisteredToolHarnesses["ECommerce"].Skills!);
        Assert.Equal("CheckoutWorkflow", state.RegisteredToolHarnesses["ECommerce"].Skills![0].Name);
    }

    [Fact]
    public async Task BeforeIteration_WithSkills_AddsSkillsAsAIFunctions()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();

        var skill = new ClientSkillDefinition(
            Name: "CheckoutWorkflow",
            Description: "Guides through checkout process",
            SystemPrompt: "1. Verify cart\n2. Get payment\n3. Confirm order"
        );

        var ToolHarness = new clientToolHarnessDefinition(
            Name: "ECommerce",
            Description: "E-commerce tools",
            Tools: new[] { CreateTestTool("AddToCart") },
            Skills: new[] { skill },
            StartCollapsed: false
        );

        var state = new ClientToolStateData()
            .WithRegisteredToolHarness(ToolHarness)
            .WithExpandedToolHarness("ECommerce");

        var agentState = CreateEmptyState() with
        {
            MiddlewareState = new MiddlewareState().WithClientTool(state)
        };

        var context = CreateIterationContext(agentState);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert - both tool and skill should be added as AIFunctions
        var functionNames = context.Options.Tools.OfType<AIFunction>().Select(f => f.Name).ToList();
        Assert.Contains("AddToCart", functionNames);
        Assert.Contains("CheckoutWorkflow", functionNames);
    }

    [Fact]
    public async Task BeforeMessageTurn_SkillWithInvalidReference_ThrowsValidationError()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateBeforeMessageTurnContext();

        // Skill references a tool that doesn't exist in the ToolHarness
        var skill = new ClientSkillDefinition(
            Name: "CheckoutWorkflow",
            Description: "Guides through checkout process",
            SystemPrompt: "Follow these steps",
            References: new[] { new ClientSkillReference("NonExistentTool") }
        );

        var ToolHarness = new clientToolHarnessDefinition(
            Name: "ECommerce",
            Description: "E-commerce tools",
            Tools: new[] { CreateTestTool("AddToCart") },
            Skills: new[] { skill },
            StartCollapsed: false
        );

        var clientinput = new AgentClientInput { clientToolHarnesses = new[] { ToolHarness } };
        context.RunConfig.ClientToolInput = clientinput;

        // Act & Assert - should throw because skill references non-existent tool
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await middleware.BeforeMessageTurnAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task BeforeMessageTurn_SkillWithCrossToolHarnessReference_ValidatesCorrectly()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateBeforeMessageTurnContext();

        // ToolHarness A has a skill that references a tool in ToolHarness B
        var skillWithCrossRef = new ClientSkillDefinition(
            Name: "FullOrderWorkflow",
            Description: "Complete order workflow",
            SystemPrompt: "Use tools from both ToolHarnesses",
            References: new[]
            {
                new ClientSkillReference("AddToCart"),  // Local tool
                new ClientSkillReference("ProcessPayment", "PaymentToolHarness")  // Cross-ToolHarness ref
            }
        );

        var ecommerceToolHarness = new clientToolHarnessDefinition(
            Name: "ECommerce",
            Description: "E-commerce tools",
            Tools: new[] { CreateTestTool("AddToCart") },
            Skills: new[] { skillWithCrossRef },
            StartCollapsed: false
        );

        var paymentToolHarness = new clientToolHarnessDefinition(
            Name: "PaymentToolHarness",
            Description: "Payment tools",
            Tools: new[] { CreateTestTool("ProcessPayment") },
            StartCollapsed: false
        );

        var clientinput = new AgentClientInput
        {
            clientToolHarnesses = new[] { ecommerceToolHarness, paymentToolHarness }
        };
        context.RunConfig.ClientToolInput = clientinput;

        // Act - should succeed because PaymentToolHarness.ProcessPayment exists
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var state = context.Analyze(s => s.MiddlewareState.ClientTool());
        Assert.NotNull(state);
        Assert.Equal(2, state.RegisteredToolHarnesses.Count);
    }

    [Fact]
    public async Task BeforeMessageTurn_SkillWithInvalidCrossToolHarnessReference_ThrowsValidationError()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateBeforeMessageTurnContext();

        // Skill references a tool in a ToolHarness that doesn't exist
        var skillWithBadRef = new ClientSkillDefinition(
            Name: "BadWorkflow",
            Description: "Workflow with invalid reference",
            SystemPrompt: "This will fail",
            References: new[]
            {
                new ClientSkillReference("SomeTool", "NonExistentToolHarness")
            }
        );

        var ToolHarness = new clientToolHarnessDefinition(
            Name: "MyToolHarness",
            Description: "My tools",
            Tools: new[] { CreateTestTool("LocalTool") },
            Skills: new[] { skillWithBadRef },
            StartCollapsed: false
        );

        var clientinput = new AgentClientInput { clientToolHarnesses = new[] { ToolHarness } };
        context.RunConfig.ClientToolInput = clientinput;

        // Act & Assert - should throw because referenced ToolHarness doesn't exist
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await middleware.BeforeMessageTurnAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task BeforeIteration_CollapsedToolHarness_HasContainerAndSkills()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();

        // When a ToolHarness is collapsed:
        // - Container function (Client_ECommerce) is added
        // - Collapsed tools (with ParentToolHarness metadata) are added for ToolCollapsingMiddleware
        // - Skills are ALWAYS added (they're entry points)
        var skill = new ClientSkillDefinition(
            Name: "QuickCheckout",
            Description: "Fast checkout process",
            SystemPrompt: "Use this for quick orders"
        );

        var ToolHarness = new clientToolHarnessDefinition(
            Name: "ECommerce",
            Description: "E-commerce tools",
            Tools: new[] { CreateTestTool("AddToCart"), CreateTestTool("RemoveFromCart") },
            Skills: new[] { skill },
            StartCollapsed: true
        );

        // ToolHarness is NOT expanded
        var state = new ClientToolStateData()
            .WithRegisteredToolHarness(ToolHarness);
        // Note: NOT calling .WithExpandedToolHarness()

        var agentState = CreateEmptyState() with
        {
            MiddlewareState = new MiddlewareState().WithClientTool(state)
        };

        var context = CreateIterationContext(agentState);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert - container and skill should be visible
        var functions = context.Options.Tools.OfType<AIFunction>().ToList();
        var functionNames = functions.Select(f => f.Name).ToList();

        // Container function exists
        Assert.Contains("Client_ECommerce", functionNames);

        // Skill is visible (skills are always available as entry points)
        Assert.Contains("QuickCheckout", functionNames);

        // Tools exist (with ParentToolHarness metadata for ToolCollapsingMiddleware to filter)
        Assert.Contains("AddToCart", functionNames);
        Assert.Contains("RemoveFromCart", functionNames);

        // Verify tools have ParentToolHarness metadata (for Collapsing middleware)
        var addToCart = functions.First(f => f.Name == "AddToCart");
        Assert.True(addToCart.AdditionalProperties?.ContainsKey("ParentToolHarness") == true);
        Assert.Equal("Client_ECommerce", addToCart.AdditionalProperties!["ParentToolHarness"]);
    }

    [Fact]
    public async Task BeforeIteration_CollapsedToolHarness_PreservesClientToolMetadata()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var ToolHarness = new clientToolHarnessDefinition(
            Name: "Browser",
            Description: "Browser artifact tools",
            Tools: new[] { CreateTestTool("write_artifact") },
            StartCollapsed: true
        );

        var state = new ClientToolStateData()
            .WithRegisteredToolHarness(ToolHarness);
        var agentState = CreateEmptyState() with
        {
            MiddlewareState = new MiddlewareState().WithClientTool(state)
        };
        var context = CreateIterationContext(agentState);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert
        var functions = context.Options.Tools.OfType<AIFunction>().ToList();
        var writeArtifact = functions.First(f => f.Name == "write_artifact");

        Assert.True(writeArtifact.AdditionalProperties?.ContainsKey("ParentToolHarness") == true);
        Assert.Equal("Client_Browser", writeArtifact.AdditionalProperties!["ParentToolHarness"]);
        Assert.NotNull(writeArtifact.AdditionalProperties);
        Assert.True(writeArtifact.AdditionalProperties.TryGetValue("IsClientTool", out var isClientTool));
        Assert.True(isClientTool is true);
        Assert.Equal("Browser", writeArtifact.AdditionalProperties!["clientToolHarnessName"]);
        Assert.Equal("clientToolHarness", writeArtifact.AdditionalProperties!["SourceType"]);
    }

    [Fact]
    public void SkillDefinition_Validation_RequiresName()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new ClientSkillDefinition(
                Name: "",
                Description: "Description",
                SystemPrompt: "Instructions"
            ).Validate());
    }

    [Fact]
    public void SkillDefinition_Validation_RequiresDescription()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new ClientSkillDefinition(
                Name: "Skill",
                Description: "",
                SystemPrompt: "Instructions"
            ).Validate());
    }

    [Fact]
    public void SkillDefinition_Validation_RequiresFunctionResultOrSystemPrompt()
    {
        // Act & Assert - at least one of FunctionResult or SystemPrompt must be provided
        Assert.Throws<ArgumentException>(() =>
            new ClientSkillDefinition(
                Name: "Skill",
                Description: "Description",
                FunctionResult: null,
                SystemPrompt: null
            ).Validate());
    }

    // ============================================
    // Helper Methods
    // ============================================

    private static BeforeMessageTurnContext CreateContext(AgentLoopState? state = null)
    {
        var agentState = state ?? CreateEmptyState();

        var session = new global::HPD.Agent.Session("test-session");
        var branch = new global::HPD.Agent.Branch("test-session");

        var agentContext = new AgentContext(
            "TestAgent",
            "test-conv-id",
            agentState,
            new HPD.Events.Core.EventCoordinator(),
            session,
            branch,
            CancellationToken.None);

        var userMessage = new ChatMessage(ChatRole.User, "test");
        return agentContext.AsBeforeMessageTurn(
            userMessage,
            new List<ChatMessage>(),
            new AgentRunConfig());
    }

    private static BeforeIterationContext CreateIterationContext(AgentLoopState? state = null)
    {
        var agentState = state ?? CreateEmptyState();

        var session = new global::HPD.Agent.Session("test-session");
        var branch = new global::HPD.Agent.Branch("test-session");

        var agentContext = new AgentContext(
            "TestAgent",
            "test-conv-id",
            agentState,
            new HPD.Events.Core.EventCoordinator(),
            session,
            branch,
            CancellationToken.None);

        return agentContext.AsBeforeIteration(
            iteration: 0,
            messages: new List<ChatMessage>(),
            options: new ChatOptions { Tools = new List<AITool>() },
            runConfig: new AgentRunConfig());
    }

    private static AgentLoopState CreateEmptyState()
    {
        return AgentLoopState.InitialSafe(
            messages: new List<ChatMessage>(),
            runId: Guid.NewGuid().ToString(),
            conversationId: "test-conv-id",
            agentName: "TestAgent");
    }

    private static clientToolHarnessDefinition CreateTestToolHarness(
        string name,
        ClientToolDefinition[]? tools = null,
        bool startCollapsed = false)
    {
        return new clientToolHarnessDefinition(
            Name: name,
            Description: $"Test ToolHarness {name}",
            Tools: tools ?? new[] { CreateTestTool($"{name}_DefaultTool") },
            StartCollapsed: startCollapsed
        );
    }

    private static ClientToolDefinition CreateTestTool(string name)
    {
        return new ClientToolDefinition(
            Name: name,
            Description: $"Test tool {name}",
            ParametersSchema: JsonDocument.Parse("{}").RootElement
        );
    }

    private static AgentContext CreateAgentContext(AgentLoopState? state = null)
    {
        var agentState = state ?? AgentLoopState.InitialSafe(
            messages: Array.Empty<ChatMessage>(),
            runId: "test-run",
            conversationId: "test-conversation",
            agentName: "TestAgent");

        var session = new global::HPD.Agent.Session("test-session");
        var branch = new global::HPD.Agent.Branch("test-session");

        return new AgentContext(
            "TestAgent",
            "test-conversation",
            agentState,
            new HPD.Events.Core.EventCoordinator(),
            session,
            branch,
            CancellationToken.None);
    }

    private static BeforeToolExecutionContext CreateBeforeToolExecutionContext(
        ChatMessage? response = null,
        List<FunctionCallContent>? toolCalls = null,
        AgentLoopState? state = null)
    {
        var agentContext = CreateAgentContext(state);
        response ??= new ChatMessage(ChatRole.Assistant, []);
        toolCalls ??= new List<FunctionCallContent>();
        return agentContext.AsBeforeToolExecution(response, toolCalls, new AgentRunConfig());
    }

    private static AfterMessageTurnContext CreateAfterMessageTurnContext(
        AgentLoopState? state = null,
        List<ChatMessage>? turnHistory = null)
    {
        var agentContext = CreateAgentContext(state);
        var finalResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Test response"));
        turnHistory ??= new List<ChatMessage>();
        return agentContext.AsAfterMessageTurn(finalResponse, turnHistory, new AgentRunConfig());
    }


    private static BeforeMessageTurnContext CreateBeforeMessageTurnContext(AgentLoopState? state = null)
    {
        var agentContext = CreateAgentContext(state);
        var userMessage = new ChatMessage(ChatRole.User, "Test message");
        var conversationHistory = new List<ChatMessage>();
        var runConfig = new AgentRunConfig();
        return agentContext.AsBeforeMessageTurn(userMessage, conversationHistory, runConfig);
    }

}
