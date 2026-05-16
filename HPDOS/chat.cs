#!/usr/bin/env dotnet
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property PublishAot=false
// Reference your custom TUI Framework
#:project ../HPD-AI-Framework/dotnet/HPD.TUI/src/HPD.TUI/HPD.TUI.csproj

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;


// HPD TUI Namespaces
using HPD.TUI.Rendering;
using HPD.TUI.Flows;
using HPD.TUI.Terminal;
using HPD.TUI.Content;

var backendUrl = "http://127.0.0.1:4317"; // Backend running on port 4317
using var client = new HttpClient { BaseAddress = new Uri(backendUrl) };

// 1. Initialize the TUI Application with ConsoleTerminal
var terminal = new ConsoleTerminal();
using var tuiApp = new TuiApplication(terminal);

try
{
    // Create and display a simple welcome message
    var welcomeMarkdown = new MarkdownBlock("# **HPD-OS TUI Chat Client**\n\nConnecting to backend...", null);
    tuiApp.SetRoot(welcomeMarkdown);
    tuiApp.Render();

    // 2. Fetch Available Agents
    var agentsJson = await client.GetStringAsync("/api/hpd-agent/agents");
    using var doc = JsonDocument.Parse(agentsJson);
    var agentsArray = doc.RootElement.EnumerateArray().ToArray();
    
    if (agentsArray.Length == 0)
    {
        var errorMarkdown = new MarkdownBlock("## ❌ Error\nNo agents found on the backend.", null);
        tuiApp.SetRoot(errorMarkdown);
        tuiApp.Render();
        await Task.Delay(2000);
        return;
    }

    // Extract agent names and IDs for display
    var agents = agentsArray.Select(a => new { 
        Id = a.GetProperty("id").GetString() ?? "", 
        Name = a.GetProperty("name").GetString() ?? "" 
    }).ToArray();

    // 3. Use TUI SelectPromptFlow to choose an agent
    var agentPrompt = PromptFlow.Select(
        "Select an AI Agent to chat with:",
        agents,
        a => $"{a.Name} ({a.Id})"
    );
    
    var agentResult = await agentPrompt.RunAsync(tuiApp);
    if (!agentResult.IsSubmitted)
    {
        return; // User cancelled
    }
    
    var selectedAgent = agentResult.Value!;

    // 4. Create a Chat Session
    var statusMarkdown = new MarkdownBlock($"## Creating session with **{selectedAgent.Name}**...\n", null);
    tuiApp.SetRoot(statusMarkdown);
    tuiApp.Render();
    
    var sessionReq = new { };
    var sessionRes = await client.PostAsJsonAsync(
        $"/api/hpd-agent/sessions", 
        sessionReq
    );
    sessionRes.EnsureSuccessStatusCode();
    
    var sessionJson = await sessionRes.Content.ReadAsStringAsync();
    using var sessionDoc = JsonDocument.Parse(sessionJson);
    var sessionId = sessionDoc.RootElement.GetProperty("id").GetString() ?? "";
    
    // 5. The REPL Chat Loop
    var conversationMarkdown = new StringBuilder();
    conversationMarkdown.AppendLine($"# Chatting with **{selectedAgent.Name}**");
    conversationMarkdown.AppendLine($"*Session: {sessionId}*");
    conversationMarkdown.AppendLine("---\n");

    while (true)
    {
        // Get User Input using the TUI text prompt
        var inputPrompt = PromptFlow.Text("You:");
        var inputResult = await inputPrompt.RunAsync(tuiApp);
        
        if (!inputResult.IsSubmitted || string.IsNullOrWhiteSpace(inputResult.Value))
        {
            continue;
        }
        
        var userInput = inputResult.Value!;
        if (userInput.Trim().ToLower() == "/exit") 
            break;

        // Add user message to conversation
        conversationMarkdown.AppendLine($"**You:** {userInput}\n");

        // Prepare the Streaming Request
        var streamReq = new HttpRequestMessage(
            HttpMethod.Post, 
            $"/api/hpd-agent/sessions/{sessionId}/messages/stream"
        );
        streamReq.Content = JsonContent.Create(new { text = userInput });

        // Display streaming response
        var assistantResponseStart = conversationMarkdown.Length;
        conversationMarkdown.Append("**Assistant:** ");

        try
        {
            // Request headers read immediately so we can start streaming the body
            using var response = await client.SendAsync(streamReq, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            // 6. Consume the Server-Sent Events (SSE)
            var line = string.Empty;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) 
                    continue;

                if (line.StartsWith("data: "))
                {
                    var jsonText = line.Substring(6); // Strip "data: " prefix
                    if (jsonText == "[DONE]") 
                        break;  // Standard SSE completion flag

                    try 
                    {
                        // Parse the chunk (adjust property names based on your actual backend)
                        using var chunkDoc = JsonDocument.Parse(jsonText);
                        
                        // Try common property names for streaming chunks
                        string? chunkText = null;
                        if (chunkDoc.RootElement.TryGetProperty("delta", out var deltaElement) && 
                            deltaElement.TryGetProperty("content", out var contentElement))
                        {
                            chunkText = contentElement.GetString();
                        }
                        else if (chunkDoc.RootElement.TryGetProperty("content", out var directContent))
                        {
                            chunkText = directContent.GetString();
                        }
                        else if (chunkDoc.RootElement.TryGetProperty("text", out var textElement))
                        {
                            chunkText = textElement.GetString();
                        }
                        
                        if (!string.IsNullOrEmpty(chunkText))
                        {
                            conversationMarkdown.Append(chunkText);
                            
                            // Update the TUI display with accumulated content
                            var contentBlock = new MarkdownBlock(conversationMarkdown.ToString(), null);
                            tuiApp.SetRoot(contentBlock);
                            tuiApp.Render();
                        }
                    }
                    catch (JsonException) 
                    { 
                        // Handle incomplete/malformed chunks gracefully
                    }
                }
            }
        }
        catch (Exception ex)
        {
            conversationMarkdown.AppendLine($"\n*Error: {ex.Message}*");
        }
        
        conversationMarkdown.AppendLine("\n");
        
        // Update display
        var updatedMarkdown = new MarkdownBlock(conversationMarkdown.ToString(), null);
        tuiApp.SetRoot(updatedMarkdown);
        tuiApp.Render();
    }
    
    // Exit message
    var exitMarkdown = new MarkdownBlock("# 👋 Goodbye!\n\nChat session ended.", null);
    tuiApp.SetRoot(exitMarkdown);
    tuiApp.Render();
}
catch (Exception ex)
{
    var errorMarkdown = new MarkdownBlock($"# ❌ Fatal Error\n\n```\n{ex}\n```", null);
    tuiApp.SetRoot(errorMarkdown);
    tuiApp.Render();
    await Task.Delay(3000);
}
