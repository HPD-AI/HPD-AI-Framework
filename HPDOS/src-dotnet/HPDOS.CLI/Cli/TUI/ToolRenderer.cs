using HPD.Agent;
using HPD.Agent.Hosting.Data;
using HPDOS.Shell.Cli.TUI;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace HPDOS.Shell.Cli.TUI;

/// <summary>
/// Encapsulates all tool-specific rendering logic.
/// Manages tool call lifecycle (start → args → result) and outputs via callbacks.
/// </summary>
internal class ToolRenderer
{
    private readonly ToolRenderContext _context;
    private readonly ConcurrentDictionary<string, ToolMessage> _toolComponents = new();
    private readonly ConcurrentDictionary<string, string?> _callIdToToolkit = new();
    private readonly ConcurrentDictionary<string, string?> _callIdToRenderedLine = new();

    // CodingToolkit tools (for fallback detection when ToolkitName is null)
    private static readonly HashSet<string> CodingToolkitTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "ReadFile", "read_file", "ReadManyFiles", "read_many_files",
        "EditFile", "edit_file", "WriteFile", "write_file",
        "ListDirectory", "list_directory", "GlobSearch", "glob_search",
        "Grep", "grep", "DiffFiles", "diff_files",
        "GetFileInfo", "get_file_info", "ExecuteCommand", "execute_command"
    };

    // Tools whose results should be hidden (rendered via dedicated events instead)
    private static readonly HashSet<string> HiddenResultTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "CreatePlanAsync", "create_plan_async", "CreatePlan", "create_plan",
        "UpdatePlanStepAsync", "update_plan_step_async", "UpdatePlanStep", "update_plan_step",
        "CodingToolkit", "MathToolkit"
    };

    public ToolRenderer(ToolRenderContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Clears all cached tool state (called at turn start).
    /// </summary>
    public void Clear()
    {
        _toolComponents.Clear();
        _callIdToToolkit.Clear();
        _callIdToRenderedLine.Clear();
    }

    /// <summary>
    /// Renders a tool call from history (for session replay).
    /// Used by RenderHistoryTail to display completed tool calls from previous turns.
    /// </summary>
    public void RenderHistoryToolCall(string toolName, string? argsJson)
    {
        if (string.IsNullOrEmpty(argsJson))
            argsJson = "{}";

        var displayLine = BuildCodingToolkitDisplayLine(toolName, argsJson);
        // All history is completed — colour gear green
        var doneLine = displayLine.Replace("[dim]⚙", "[green]⚙");
        _context.Session.WriteLine();
        _context.WriteThread(new Markup(doneLine));
    }

    /// <summary>
    /// Processes tool call start event.
    /// </summary>
    public void RenderToolStart(ToolCallStartEvent evt)
    {
        _context.StopSpinner();
        _context.FlushText();

        var toolMessage = new ToolMessage
        {
            Name = evt.Name,
            Status = ToolCallStatus.Executing
        };
        _toolComponents[evt.CallId] = toolMessage;
        _callIdToToolkit[evt.CallId] = evt.ToolkitName;

        // Hide toolkit containers and tools with dedicated event rendering
        if (evt.Name.EndsWith("Toolkit") || HiddenResultTools.Contains(evt.Name))
        {
            _context.StartSpinner($"{evt.Name}...");
            return;
        }

        // CodingToolkit tools: don't show anything on start - we'll show inline with result
        if (IsCodingToolkitTool(evt.Name, evt.ToolkitName))
        {
            _context.StartSpinner($"{evt.Name}...");
            return;
        }

        // Default: show full tool call info for non-CodingToolkit tools
        _context.Session.WriteLine();
        _context.WriteThread(new Markup($"[yellow]⚙ Calling:[/] [bold]{Markup.Escape(evt.Name)}[/]"));
        _context.SetShowMarkerOnNext();
        _context.StartSpinner("Waiting for result...");
    }

    /// <summary>
    /// Processes tool call args event.
    /// </summary>
    public void RenderToolArgs(ToolCallArgsEvent evt)
    {
        if (!_toolComponents.TryGetValue(evt.CallId, out var tool))
            return;

        tool.Args = evt.ArgsJson;
        _callIdToToolkit.TryGetValue(evt.CallId, out var toolkit);

        // For CodingToolkit tools: buffer the display line (will be shown with result)
        if (IsCodingToolkitTool(tool.Name, toolkit))
        {
            var displayLine = BuildCodingToolkitDisplayLine(tool.Name, evt.ArgsJson);
            _callIdToRenderedLine[evt.CallId] = displayLine;
        }
    }

    /// <summary>
    /// Processes tool call result event.
    /// </summary>
    public void RenderToolResult(ToolCallResultEvent evt)
    {
        _context.StopSpinner();
        _context.FlushText();

        if (!_toolComponents.TryGetValue(evt.CallId, out var tool))
            return;

        tool.Result = evt.Result;

        var isError = ResultDetector.IsError(evt.Result);
        tool.Status = isError ? ToolCallStatus.Error : ToolCallStatus.Completed;

        // Get toolkit name (from event or cached from start event)
        var toolkitName = evt.ToolkitName ?? (_callIdToToolkit.TryGetValue(evt.CallId, out var cached) ? cached : null);

        // Hide results for tools with dedicated event rendering
        if (HiddenResultTools.Contains(tool.Name) || tool.Name.EndsWith("Toolkit"))
        {
            _toolComponents.TryRemove(evt.CallId, out _);
            _callIdToToolkit.TryRemove(evt.CallId, out _);
            _callIdToRenderedLine.TryRemove(evt.CallId, out _);
            return;
        }

        // CodingToolkit tools: show inline with colored gear
        if (IsCodingToolkitTool(tool.Name, toolkitName))
        {
            RenderCodingToolkitResult(tool, evt.Result, isError, evt.CallId);
            _context.SetShowMarkerOnNext();
            _toolComponents.TryRemove(evt.CallId, out _);
            _callIdToToolkit.TryRemove(evt.CallId, out _);
            _callIdToRenderedLine.TryRemove(evt.CallId, out _);
            return;
        }

        // Default: show full result for non-CodingToolkit tools
        _context.WriteThread(tool.Render());
        if (!isError)
        {
            RenderResultByType(evt.Result);
        }

        _context.SetShowMarkerOnNext();

        _toolComponents.TryRemove(evt.CallId, out _);
        _callIdToToolkit.TryRemove(evt.CallId, out _);
    }

    /// <summary>
    /// Detects if a tool belongs to CodingToolkit (by name or explicit ToolkitName)
    /// </summary>
    private static bool IsCodingToolkitTool(string toolName, string? toolkitName)
    {
        if (toolkitName == "CodingToolkit") return true;
        if (CodingToolkitTools.Contains(toolName)) return true;
        return false;
    }

    /// <summary>
    /// Renders a CodingToolkit tool result with inline display and optional diff.
    /// </summary>
    private void RenderCodingToolkitResult(ToolMessage tool, string result, bool isError, string callId)
    {
        // Get buffered display line and colorize gear based on result
        _callIdToRenderedLine.TryRemove(callId, out var displayLine);
        displayLine ??= $"⚙ {Markup.Escape(tool.Name)}";

        // Replace dim gear with colored gear based on success/failure
        var coloredLine = isError
            ? displayLine.Replace("[dim]⚙", "[red]⚙")
            : displayLine.Replace("[dim]⚙", "[green]⚙");

        _context.Session.WriteLine();
        _context.WriteThread(new Markup(coloredLine));

        if (isError)
        {
            _context.WriteThread(new Markup($"[red dim]  {Markup.Escape(TruncateResult(result, 100))}[/]"));
            return;
        }

        // Show diff for write operations
        var isWriteOp = tool.Name is "EditFile" or "WriteFile" or "edit_file" or "write_file";

        if (isWriteOp)
        {
            // Try to extract old/new content from args for EditFile
            if ((tool.Name is "EditFile" or "edit_file") && TryExtractEditFileDiff(tool.Args, out var oldContent, out var newContent))
            {
                DisplayEditFileDiff(newContent is null ? oldContent : newContent, oldContent, newContent);
            }
            // Fall back to result-based diff detection
            else if (ResultDetector.Detect(result) == ResultType.Diff)
            {
                DisplayToolDiff(result);
            }
        }
    }

    /// <summary>
    /// Tries to extract oldString and newString from EditFile args JSON.
    /// </summary>
    private static bool TryExtractEditFileDiff(string? argsJson, out string? oldString, out string? newString)
    {
        oldString = null;
        newString = null;

        if (string.IsNullOrEmpty(argsJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("oldString", out var oldProp))
                oldString = oldProp.GetString();

            if (root.TryGetProperty("newString", out var newProp))
                newString = newProp.GetString();

            return oldString != null || newString != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Displays an inline diff using DiffRenderer with old/new content directly.
    /// </summary>
    private void DisplayEditFileDiff(string filename, string? oldContent, string? newContent)
    {
        try
        {
            var diffRenderer = new DiffRenderer
            {
                OldContent = oldContent,
                NewContent = newContent,
                Filename = filename,
                MaxLines = 50
            };

            _context.WriteThread(diffRenderer.Render());
            _context.Session.WriteLine();
        }
        catch (Exception ex)
        {
            _context.WriteThread(new Markup($"[dim]Note: Could not render diff: {Markup.Escape(ex.Message)}[/]"));
        }
    }

    /// <summary>
    /// Builds the display line for a CodingToolkit tool call (returned as markup string)
    /// </summary>
    private static string BuildCodingToolkitDisplayLine(string toolName, string argsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            var root = doc.RootElement;

            switch (toolName)
            {
                case "ReadFile" or "read_file":
                    var path = root.TryGetProperty("path", out var p) ? p.GetString() :
                               root.TryGetProperty("filePath", out var fp) ? fp.GetString() : null;
                    var startLine = root.TryGetProperty("startLine", out var sl) ? sl.GetInt32() : (int?)null;
                    var endLine = root.TryGetProperty("endLine", out var el) ? el.GetInt32() : (int?)null;
                    if (path != null)
                    {
                        var lineInfo = (startLine.HasValue && endLine.HasValue)
                            ? $" [dim](lines {startLine}-{endLine})[/]"
                            : startLine.HasValue ? $" [dim](from line {startLine})[/]" : "";
                        return $"[dim]⚙ ReadFile:[/] [blue]{Markup.Escape(path)}[/]{lineInfo}";
                    }
                    break;

                case "ReadManyFiles" or "read_many_files":
                    if (root.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Array)
                        return $"[dim]⚙ ReadManyFiles:[/] [blue]{paths.GetArrayLength()} files[/]";
                    break;

                case "EditFile" or "edit_file":
                case "WriteFile" or "write_file":
                    var editPath = root.TryGetProperty("path", out var ep) ? ep.GetString() :
                                   root.TryGetProperty("filePath", out var efp) ? efp.GetString() : null;
                    if (editPath != null)
                        return $"[dim]⚙ {Markup.Escape(toolName)}:[/] [blue]{Markup.Escape(editPath)}[/]";
                    break;

                case "ListDirectory" or "list_directory":
                    var dirPath = root.TryGetProperty("directoryPath", out var dp) ? dp.GetString() :
                                  root.TryGetProperty("path", out var dp2) ? dp2.GetString() : null;
                    var displayDir = string.IsNullOrWhiteSpace(dirPath) ? "." : dirPath;
                    return $"[dim]⚙ ListDirectory:[/] [blue]{Markup.Escape(displayDir)}[/]";

                case "GlobSearch" or "glob_search":
                    var pattern = root.TryGetProperty("pattern", out var pat) ? pat.GetString() : null;
                    if (pattern != null)
                        return $"[dim]⚙ GlobSearch:[/] [blue]{Markup.Escape(pattern)}[/]";
                    break;

                case "Grep" or "grep":
                    var query = root.TryGetProperty("pattern", out var q) ? q.GetString() :
                                root.TryGetProperty("query", out var qry) ? qry.GetString() : null;
                    if (query != null)
                        return $"[dim]⚙ Grep:[/] [blue]{Markup.Escape(query)}[/]";
                    break;

                case "ExecuteCommand" or "execute_command":
                    var cmd = root.TryGetProperty("command", out var c) ? c.GetString() : null;
                    if (cmd != null)
                    {
                        var displayCmd = cmd.Length > 60 ? cmd[..60] + "..." : cmd;
                        return $"[dim]⚙ ExecuteCommand:[/] [yellow]{Markup.Escape(displayCmd)}[/]";
                    }
                    break;
            }
        }
        catch { /* JSON parse failed */ }

        return $"[dim]⚙ {Markup.Escape(toolName)}[/]";
    }

    /// <summary>
    /// Smart content-based rendering. Detects result type and renders accordingly.
    /// </summary>
    private void RenderResultByType(string result)
    {
        var resultType = ResultDetector.Detect(result);

        switch (resultType)
        {
            case ResultType.Diff:
                DisplayToolDiff(result);
                break;
            case ResultType.Json:
                // Future: DisplayJson(result);
                break;
            case ResultType.Table:
                // Future: DisplayTable(result);
                break;
            // Plain text already shown in tool.Render()
        }
    }

    /// <summary>
    /// Displays a diff parsed from unified diff format in the result string.
    /// </summary>
    private void DisplayToolDiff(string result)
    {
        try
        {
            // The result might contain diff information
            // Look for diff markers like +++ and --- (unified diff format)
            if (result.Contains("+++") && result.Contains("---"))
            {
                var lines = result.Split('\n');
                var diffContent = new StringBuilder();
                bool inDiff = false;
                string? fileName = null;

                foreach (var line in lines)
                {
                    // Extract filename from --- line
                    if (line.StartsWith("---") && fileName == null)
                    {
                        var parts = line.Split('\t');
                        if (parts.Length > 0)
                        {
                            fileName = parts[0].Substring(4).Trim(); // Remove "--- "
                        }
                        inDiff = true;
                    }

                    if (inDiff)
                        diffContent.AppendLine(line);
                }

                if (diffContent.Length > 0)
                {
                    // Use DiffRenderer component for rich diff display
                    var diffRenderer = new DiffRenderer
                    {
                        DiffContent = diffContent.ToString(),
                        Filename = fileName,
                        MaxLines = 50
                    };

                    _context.WriteThread(diffRenderer.Render());
                    _context.Session.WriteLine();
                }
            }
        }
        catch (Exception ex)
        {
            _context.WriteThread(new Markup($"[dim]Note: Could not parse diff: {Markup.Escape(ex.Message)}[/]"));
        }
    }

    private static string TruncateResult(string result, int maxLength)
    {
        if (result.Length <= maxLength) return result;
        return result[..maxLength] + "...";
    }
}

/// <summary>
/// Context passed to ToolRenderer with callbacks for output and lifecycle operations.
/// </summary>
internal record ToolRenderContext(
    IConsoleSession Session,
    Action<IRenderable> WriteThread,
    Action FlushText,
    Action<string> StartSpinner,
    Action StopSpinner,
    Action SetShowMarkerOnNext);
