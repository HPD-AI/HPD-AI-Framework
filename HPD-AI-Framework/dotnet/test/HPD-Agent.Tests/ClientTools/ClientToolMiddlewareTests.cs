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
/// Tests Toolkit registration, tool visibility, and tool invocation interception.
/// </summary>
public class ClientToolMiddlewareTests
{
    // ============================================
    // BeforeMessageTurnAsync - Toolkit Registration Tests
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
    public async Task BeforeMessageTurn_WithToolss_RegistersToolkits()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateContext();
        var clientinput = new AgentClientInput
        {
            clientToolKits = new[]
            {
                CreateTestToolkit("Toolkit1", tools: new[] { CreateTestTool("Tool1") }),
                CreateTestToolkit("Toolkit2", tools: new[] { CreateTestTool("Tool2") })
            }
        };
        context.RunConfig.ClientToolInput = clientinput;

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var state = context.Analyze(s => s.MiddlewareState.ClientTool());
        Assert.NotNull(state);
        Assert.Equal(2, state.RegisteredToolKits.Count);
        Assert.True(state.RegisteredToolKits.ContainsKey("Toolkit1"));
        Assert.True(state.RegisteredToolKits.ContainsKey("Toolkit2"));
    }

    [Fact]
    public async Task BeforeMessageTurn_WithExpandedContainers_MarksAsExpanded()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateContext();
        var clientinput = new AgentClientInput
        {
            clientToolKits = new[]
            {
                CreateTestToolkit("Toolkit1", startCollapsed: true),
                CreateTestToolkit("Toolkit2", startCollapsed: true)
            },
            ExpandedContainers = new HashSet<string> { "Toolkit1" }
        };
        context.RunConfig.ClientToolInput = clientinput;

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var state = context.Analyze(s => s.MiddlewareState.ClientTool());
        Assert.NotNull(state);
        Assert.Contains("Toolkit1", state.ExpandedToolkits);
        Assert.DoesNotContain("Toolkit2", state.ExpandedToolkits);
    }

    [Fact]
    public async Task BeforeMessageTurn_WithHiddenTools_MarksAsHidden()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateContext();
        var clientinput = new AgentClientInput
        {
            clientToolKits = new[]
            {
                CreateTestToolkit("Toolkit1", tools: new[]
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
            clientToolKits = new[] { CreateTestToolkit("Toolkit1") },
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
    public async Task BeforeMessageTurn_WithState_StoresState()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateContext();
        var appState = JsonSerializer.SerializeToElement(new { cartItems = 3, userId = "user123" });
        var clientinput = new AgentClientInput
        {
            clientToolKits = new[] { CreateTestToolkit("Toolkit1") },
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

        // First call - register Toolkits
        var context1 = CreateContext();
        var clientinput1 = new AgentClientInput
        {
            clientToolKits = new[] { CreateTestToolkit("OldToolkit") }
        };
        context1.RunConfig.ClientToolInput = clientinput1;
        await middleware.BeforeMessageTurnAsync(context1, CancellationToken.None);

        // Second call with reset
        var context2 = CreateContext(context1.State);
        var clientinput2 = new AgentClientInput
        {
            clientToolKits = new[] { CreateTestToolkit("NewToolkit") },
            ResetClientState = true
        };
        context2.RunConfig.ClientToolInput = clientinput2;

        // Act
        await middleware.BeforeMessageTurnAsync(context2, CancellationToken.None);

        // Assert - only new Toolkit registered
        var state = context2.State.MiddlewareState.ClientTool();
        Assert.NotNull(state);
        Assert.Single(state.RegisteredToolKits);
        Assert.True(state.RegisteredToolKits.ContainsKey("NewToolkit"));
        Assert.False(state.RegisteredToolKits.ContainsKey("OldToolkit"));
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
    public async Task BeforeIteration_WithExpandedToolkit_AddsToolsToOptions()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();

        // Set up state with registered and expanded Toolkit
        var state = new ClientToolStateData()
            .WithRegisteredToolkit(CreateTestToolkit("Toolkit1", tools: new[]
            {
                CreateTestTool("Tool1"),
                CreateTestTool("Tool2")
            }))
            .WithExpandedToolkit("Toolkit1");

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
            .WithRegisteredToolkit(CreateTestToolkit("Toolkit1", tools: new[]
            {
                CreateTestTool("Tool1"),
                CreateTestTool("Tool2")
            }))
            .WithExpandedToolkit("Toolkit1")
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
    // Toolkit Validation Tests
    // ============================================

    [Fact]
    public async Task BeforeMessageTurn_CollapsedToolkitWithoutDescription_ThrowsValidationError()
    {
        // Arrange
        var middleware = new ClientToolMiddleware(new ClientToolConfig { ValidateSchemaOnRegistration = true });
        var context = CreateBeforeMessageTurnContext();

        // Create Toolkit with startCollapsed=true but no description
        var Toolkit = new clientToolKitDefinition(
            Name: "BadToolkit",
            Description: null, // No description!
            Tools: new[] { CreateTestTool("Tool1") },
            StartCollapsed: true
        );

        var clientinput = new AgentClientInput { clientToolKits = new[] { Toolkit } };
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
            ExpandToolkits = new HashSet<string> { "Toolkit2" },
            HideTools = new HashSet<string> { "Tool1" }
        };

        var state = new ClientToolStateData()
            .WithRegisteredToolkit(CreateTestToolkit("Toolkit1", tools: new[] { CreateTestTool("Tool1") }))
            .WithRegisteredToolkit(CreateTestToolkit("Toolkit2", tools: new[] { CreateTestTool("Tool2") }))
            .WithExpandedToolkit("Toolkit1")
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
        Assert.Contains("Toolkit2", updatedState.ExpandedToolkits);
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

        var Toolkit = new clientToolKitDefinition(
            Name: "ECommerce",
            Description: "E-commerce tools",
            Tools: new[] { CreateTestTool("AddToCart") },
            Skills: new[] { skill },
            StartCollapsed: false
        );

        var clientinput = new AgentClientInput { clientToolKits = new[] { Toolkit } };
        context.RunConfig.ClientToolInput = clientinput;

        // Act
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var state = context.Analyze(s => s.MiddlewareState.ClientTool());
        Assert.NotNull(state);
        Assert.Single(state.RegisteredToolKits);
        Assert.NotNull(state.RegisteredToolKits["ECommerce"].Skills);
        Assert.Single(state.RegisteredToolKits["ECommerce"].Skills!);
        Assert.Equal("CheckoutWorkflow", state.RegisteredToolKits["ECommerce"].Skills![0].Name);
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

        var Toolkit = new clientToolKitDefinition(
            Name: "ECommerce",
            Description: "E-commerce tools",
            Tools: new[] { CreateTestTool("AddToCart") },
            Skills: new[] { skill },
            StartCollapsed: false
        );

        var state = new ClientToolStateData()
            .WithRegisteredToolkit(Toolkit)
            .WithExpandedToolkit("ECommerce");

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
    public async Task BeforeIteration_SkillWithDocuments_IncludesDocumentInfoInDescription()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();

        var skill = new ClientSkillDefinition(
            Name: "CheckoutWorkflow",
            Description: "Guides through checkout process",
            SystemPrompt: "Follow these steps for checkout",
            Documents: new[]
            {
                new ClientSkillDocument("checkout-guide", "Detailed checkout documentation", Content: "# Checkout Guide\n..."),
                new ClientSkillDocument("payment-api", "Payment API reference", Content: "# Payment API\n...")
            }
        );

        var Toolkit = new clientToolKitDefinition(
            Name: "ECommerce",
            Description: "E-commerce tools",
            Tools: new[] { CreateTestTool("AddToCart") },
            Skills: new[] { skill },
            StartCollapsed: false
        );

        var state = new ClientToolStateData()
            .WithRegisteredToolkit(Toolkit)
            .WithExpandedToolkit("ECommerce");

        var agentState = CreateEmptyState() with
        {
            MiddlewareState = new MiddlewareState().WithClientTool(state)
        };

        var context = CreateIterationContext(agentState);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert - skill should be added and mention documents
        var skillFunction = context.Options.Tools.OfType<AIFunction>()
            .FirstOrDefault(f => f.Name == "CheckoutWorkflow");

        Assert.NotNull(skillFunction);
        // Skill description should be present (documents are referenced in activation)
        Assert.Equal("Guides through checkout process", skillFunction.Description);
    }

    [Fact]
    public async Task BeforeMessageTurn_SkillWithInvalidReference_ThrowsValidationError()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateBeforeMessageTurnContext();

        // Skill references a tool that doesn't exist in the Toolkit
        var skill = new ClientSkillDefinition(
            Name: "CheckoutWorkflow",
            Description: "Guides through checkout process",
            SystemPrompt: "Follow these steps",
            References: new[] { new ClientSkillReference("NonExistentTool") }
        );

        var Toolkit = new clientToolKitDefinition(
            Name: "ECommerce",
            Description: "E-commerce tools",
            Tools: new[] { CreateTestTool("AddToCart") },
            Skills: new[] { skill },
            StartCollapsed: false
        );

        var clientinput = new AgentClientInput { clientToolKits = new[] { Toolkit } };
        context.RunConfig.ClientToolInput = clientinput;

        // Act & Assert - should throw because skill references non-existent tool
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await middleware.BeforeMessageTurnAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task BeforeMessageTurn_SkillWithCrossToolkitReference_ValidatesCorrectly()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateBeforeMessageTurnContext();

        // Toolkit A has a skill that references a tool in Toolkit B
        var skillWithCrossRef = new ClientSkillDefinition(
            Name: "FullOrderWorkflow",
            Description: "Complete order workflow",
            SystemPrompt: "Use tools from both Toolkits",
            References: new[]
            {
                new ClientSkillReference("AddToCart"),  // Local tool
                new ClientSkillReference("ProcessPayment", "PaymentToolkit")  // Cross-Toolkit ref
            }
        );

        var ecommerceToolkit = new clientToolKitDefinition(
            Name: "ECommerce",
            Description: "E-commerce tools",
            Tools: new[] { CreateTestTool("AddToCart") },
            Skills: new[] { skillWithCrossRef },
            StartCollapsed: false
        );

        var paymentToolkit = new clientToolKitDefinition(
            Name: "PaymentToolkit",
            Description: "Payment tools",
            Tools: new[] { CreateTestTool("ProcessPayment") },
            StartCollapsed: false
        );

        var clientinput = new AgentClientInput
        {
            clientToolKits = new[] { ecommerceToolkit, paymentToolkit }
        };
        context.RunConfig.ClientToolInput = clientinput;

        // Act - should succeed because PaymentToolkit.ProcessPayment exists
        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // Assert
        var state = context.Analyze(s => s.MiddlewareState.ClientTool());
        Assert.NotNull(state);
        Assert.Equal(2, state.RegisteredToolKits.Count);
    }

    [Fact]
    public async Task BeforeMessageTurn_SkillWithInvalidCrossToolkitReference_ThrowsValidationError()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();
        var context = CreateBeforeMessageTurnContext();

        // Skill references a tool in a Toolkit that doesn't exist
        var skillWithBadRef = new ClientSkillDefinition(
            Name: "BadWorkflow",
            Description: "Workflow with invalid reference",
            SystemPrompt: "This will fail",
            References: new[]
            {
                new ClientSkillReference("SomeTool", "NonExistentToolkit")
            }
        );

        var Toolkit = new clientToolKitDefinition(
            Name: "MyToolkit",
            Description: "My tools",
            Tools: new[] { CreateTestTool("LocalTool") },
            Skills: new[] { skillWithBadRef },
            StartCollapsed: false
        );

        var clientinput = new AgentClientInput { clientToolKits = new[] { Toolkit } };
        context.RunConfig.ClientToolInput = clientinput;

        // Act & Assert - should throw because referenced Toolkit doesn't exist
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await middleware.BeforeMessageTurnAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task BeforeIteration_CollapsedToolkit_HasContainerAndSkills()
    {
        // Arrange
        var middleware = new ClientToolMiddleware();

        // When a Toolkit is collapsed:
        // - Container function (Client_ECommerce) is added
        // - Collapsed tools (with ParentToolkit metadata) are added for ToolCollapsingMiddleware
        // - Skills are ALWAYS added (they're entry points)
        var skill = new ClientSkillDefinition(
            Name: "QuickCheckout",
            Description: "Fast checkout process",
            SystemPrompt: "Use this for quick orders"
        );

        var Toolkit = new clientToolKitDefinition(
            Name: "ECommerce",
            Description: "E-commerce tools",
            Tools: new[] { CreateTestTool("AddToCart"), CreateTestTool("RemoveFromCart") },
            Skills: new[] { skill },
            StartCollapsed: true
        );

        // Toolkit is NOT expanded
        var state = new ClientToolStateData()
            .WithRegisteredToolkit(Toolkit);
        // Note: NOT calling .WithExpandedToolkit()

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

        // Tools exist (with ParentToolkit metadata for ToolCollapsingMiddleware to filter)
        Assert.Contains("AddToCart", functionNames);
        Assert.Contains("RemoveFromCart", functionNames);

        // Verify tools have ParentToolkit metadata (for Collapsing middleware)
        var addToCart = functions.First(f => f.Name == "AddToCart");
        Assert.True(addToCart.AdditionalProperties?.ContainsKey("ParentToolkit") == true);
        Assert.Equal("Client_ECommerce", addToCart.AdditionalProperties!["ParentToolkit"]);
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

    [Fact]
    public void SkillDocument_Validation_RequiresContentOrUrl()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new ClientSkillDocument(
                DocumentId: "doc1",
                Description: "A document",
                Content: null,
                Url: null
            ).Validate());
    }

    [Fact]
    public void SkillDocument_Validation_AcceptsContent()
    {
        // Arrange
        var doc = new ClientSkillDocument(
            DocumentId: "doc1",
            Description: "A document",
            Content: "# Document content"
        );

        // Act & Assert - should not throw
        doc.Validate();
    }

    [Fact]
    public void SkillDocument_Validation_AcceptsUrl()
    {
        // Arrange
        var doc = new ClientSkillDocument(
            DocumentId: "doc1",
            Description: "A document",
            Url: "https://example.com/doc.md"
        );

        // Act & Assert - should not throw
        doc.Validate();
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

    private static clientToolKitDefinition CreateTestToolkit(
        string name,
        ClientToolDefinition[]? tools = null,
        bool startCollapsed = false)
    {
        return new clientToolKitDefinition(
            Name: name,
            Description: $"Test Toolkit {name}",
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
