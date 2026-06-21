using HPD.Agent;
using HPD.Agent.Providers.OpenRouter;
using Microsoft.Extensions.AI;

// ═══════════════════════════════════════════════════════════════
// Simple HPD Agent Test
// This demonstrates a basic agent conversation with streaming
// ═══════════════════════════════════════════════════════════════

Console.WriteLine("╔════════════════════════════════════════════╗");
Console.WriteLine("║   HPD Agent Framework - Simple Test       ║");
Console.WriteLine("╚════════════════════════════════════════════╝");
Console.WriteLine();

// Check if API key is set

try
{
    // Create a simple agent
    var consoleEvents = new SimpleConsoleEventSink();
    var agent = await new AgentBuilder()
        .WithProvider("openrouter", "minimax/minimax-m2.1")  // Using mini for cost efficiency
        .WithName("TestAssistant")
        .WithInstructions("You are a helpful assistant. Keep your responses concise and friendly.")
        .BuildAsync();
    using var subscription = agent.SubscribeAny(consoleEvents.HandleAsync);

    Console.WriteLine(" Agent created successfully!");
    Console.WriteLine("📝 Asking: 'What is 2 + 2? Explain briefly.'");
    Console.WriteLine();
    Console.WriteLine("═══ Agent Response ═══");
    Console.WriteLine();

    // Run the agent with a simple question
    await agent.RunAsync("What is 2 + 2? Explain briefly.");

    Console.WriteLine();
    Console.WriteLine("═══════════════════════════════════════════");
    Console.WriteLine(" Test completed successfully!");
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine($" Error: {ex.Message}");
    Console.WriteLine($"   Type: {ex.GetType().Name}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"   Inner: {ex.InnerException.Message}");
    }
}

// ═══════════════════════════════════════════════════════════════
// Simple event sink - Displays text streaming
// ═══════════════════════════════════════════════════════════════

public class SimpleConsoleEventSink
{
    private readonly System.Text.StringBuilder _reasoningBuffer = new();
    private bool _isReasoning = false;

    public ValueTask HandleAsync(AgentEvent evt)
    {
        // Filter out observability events (internal diagnostics)
        if (evt is IObservabilityEvent) return ValueTask.CompletedTask;

        switch (evt)
        {
            case ReasoningMessageStartEvent:
                // Start buffering reasoning
                _isReasoning = true;
                _reasoningBuffer.Clear();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("💭 [Thinking: ");
                break;

            case ReasoningDeltaEvent reasoning:
                // Buffer reasoning text and display in real-time
                _reasoningBuffer.Append(reasoning.Text);
                Console.Write(reasoning.Text);
                break;

            case ReasoningMessageEndEvent:
                // Finish reasoning display
                Console.Write("]");
                Console.ResetColor();
                Console.WriteLine();
                _isReasoning = false;
                break;

            case TextDeltaEvent textDelta:
                // Stream text as it comes in (without newlines)
                Console.Write(textDelta.Text);
                break;

            case TextMessageEndEvent:
                // Add newline when message is complete
                Console.WriteLine();
                break;

            case MessageTurnErrorEvent errorEvt:
                // Display errors
                Console.WriteLine();
                Console.WriteLine($"  Error: {errorEvt.Message}");
                break;

            case MessageTurnFinishedEvent:
                // Agent is completely done
                Console.WriteLine();
                break;

            case ToolCallStartEvent toolStart:
                // Show when tools are called
                Console.WriteLine();
                Console.WriteLine($" Calling tool: {toolStart.Name}");
                break;

            case ToolCallResultEvent toolResult:
                // Show tool completion
                Console.WriteLine($"   ✓ Tool result: {FormatToolResult(toolResult.Result)}");
                break;
        }

        return ValueTask.CompletedTask;
    }

    private static string FormatToolResult(ToolResultPayload result) =>
        result.Text ?? result.Json?.GetRawText() ?? string.Empty;
}
