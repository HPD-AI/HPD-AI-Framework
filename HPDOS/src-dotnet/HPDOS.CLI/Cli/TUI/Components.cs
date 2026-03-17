using HPD.Agent.Hosting.Data;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text.RegularExpressions;
using HPDOS.Shell.Cli.TUI.Commands;

namespace HPDOS.Shell.Cli.TUI;

// ============================================================================
// MESSAGE COMPONENTS
// ============================================================================

/// <summary>
/// User message bubble.
/// </summary>
public class UserMessage : UIComponent
{
    public string Content { get; set; } = "";
    
    public override IRenderable Render()
    {
        return new Panel(new Markup(Markup.Escape(Content)))
            .Header("[bold cyan]You[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Padding(1, 0).CapToTerminal();
    }
}

/// <summary>
/// Error message display.
/// </summary>
public class ErrorMessage : UIComponent
{
    public string Message { get; set; } = "";
    public string? Details { get; set; }
    
    public override IRenderable Render()
    {
        var rows = new List<IRenderable>
        {
            new Markup($"[red bold]Error:[/] [red]{Markup.Escape(Message)}[/]")
        };
        
        if (!string.IsNullOrEmpty(Details))
        {
            rows.Add(new Text(""));
            rows.Add(new Markup($"[dim]{Markup.Escape(Details)}[/]"));
        }
        
        return new Panel(new Rows(rows))
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Red)
            .Padding(1, 0).CapToTerminal();
    }
}

// ============================================================================
// LAYOUT COMPONENTS
// ============================================================================

/// <summary>
/// App header with branding and version.
/// </summary>
public class AppHeader : UIComponent
{
    public string Title { get; set; } = "HPD Agent";
    public string Version { get; set; } = "1.0.0";
    public string? Model { get; set; }
    
    public override IRenderable Render()
    {
        var figlet = new FigletText(Title)
            .LeftJustified()
            .Color(Color.Cyan1);
            
        var info = new Markup(
            $"[dim]v{Version}[/]" + 
            (Model != null ? $" [dim]• {Markup.Escape(Model)}[/]" : "")
        );
        
        return new Rows(figlet, info, new Text(""));
    }
}

/// <summary>
/// Session stats display (tokens, time, etc.).
/// Mirrors Gemini's StatsDisplay.tsx
/// </summary>
public class StatsDisplay : UIComponent
{
    public int TotalTokens { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public TimeSpan TotalTime { get; set; }
    public int ToolCalls { get; set; }
    
    public override IRenderable Render()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("[dim]Metric[/]")
            .AddColumn("[dim]Value[/]");
            
        table.AddRow("Total Tokens", TotalTokens.ToString("N0"));
        table.AddRow("Prompt Tokens", PromptTokens.ToString("N0"));
        table.AddRow("Completion Tokens", CompletionTokens.ToString("N0"));
        table.AddRow("Duration", $"{TotalTime.TotalSeconds:F2}s");
        table.AddRow("Tool Calls", ToolCalls.ToString());
        
        return table;
    }
}

/// <summary>
/// Help panel with available commands.
/// Now dynamically populated from CommandRegistry.
/// </summary>
public class HelpPanel : UIComponent
{
    public List<SlashCommand> Commands { get; set; } = new();
    
    public override IRenderable Render()
    {
        var rows = new List<IRenderable>();
        
        // Basics section
        rows.Add(new Markup("[bold cyan]Basics:[/]"));
        rows.Add(new Markup("  Type your message or use commands to interact with the agent"));
        rows.Add(new Markup("  Press [bold]Tab[/] for command completion"));
        rows.Add(new Text(""));
        
        // Commands section
        rows.Add(new Markup("[bold cyan]Available Commands:[/]"));
        
        if (Commands.Count == 0)
        {
            rows.Add(new Markup("[dim]  No commands available[/]"));
        }
        else
        {
            foreach (var cmd in Commands.OrderBy(c => c.Name))
            {
                var aliases = cmd.AltNames.Count > 0 
                    ? $" [{Theme.Text.Muted}](aliases: {string.Join(", ", cmd.AltNames)})[/]" 
                    : "";
                    
                var category = cmd.Category != "Built-in"
                    ? $" [{Theme.Text.Muted}][{cmd.Category}][/]"
                    : "";
                
                rows.Add(new Markup(
                    $"  [bold cyan]/{Markup.Escape(cmd.Name)}[/]{aliases}{category}"
                ));
                
                if (!string.IsNullOrEmpty(cmd.Description))
                {
                    rows.Add(new Markup($"    [{Theme.Text.Secondary}]{Markup.Escape(cmd.Description)}[/]"));
                }
                
                rows.Add(new Text(""));
            }
        }
        
        // Keyboard shortcuts section
        rows.Add(new Markup("[bold cyan]Keyboard Shortcuts:[/]"));
        rows.Add(new Markup("  [bold]Ctrl+C[/] - Cancel current operation or exit"));
        rows.Add(new Markup("  [bold]Ctrl+L[/] - Clear screen"));
        rows.Add(new Markup("  [bold]↑/↓[/] - Navigate command suggestions"));
        rows.Add(new Markup("  [bold]Enter[/] - Execute command or send message"));
        rows.Add(new Markup("  [bold]Tab[/] - Complete command"));
        
        return new Panel(new Rows(rows))
            .Header("[cyan]HPD Agent Help[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Padding(1, 0).CapToTerminal();
    }
}

/// <summary>
/// Context summary display showing current session information.
/// Inspired by Gemini's ContextSummaryDisplay.
/// </summary>
public class ContextSummaryDisplay : UIComponent
{
    public int FileCount { get; set; }
    public int ToolCount { get; set; }
    public string? WorkingDirectory { get; set; }
    
    public override IRenderable Render()
    {
        var parts = new List<string>();
        
        if (FileCount > 0)
        {
            parts.Add($"[{Theme.Text.Accent}]{FileCount} file{(FileCount > 1 ? "s" : "")}[/]");
        }
        
        if (ToolCount > 0)
        {
            parts.Add($"[{Theme.Text.Accent}]{ToolCount} tool{(ToolCount > 1 ? "s" : "")}[/]");
        }
        
        if (!string.IsNullOrEmpty(WorkingDirectory))
        {
            parts.Add($"[{Theme.Text.Muted}]{Markup.Escape(WorkingDirectory)}[/]");
        }
        
        if (parts.Count == 0)
        {
            return new Markup($"[{Theme.Text.Muted}]Ready[/]");
        }
        
        return new Markup(string.Join(" [dim]•[/] ", parts));
    }
}

// ============================================================================
// SESSION COMPONENTS
// ============================================================================

/// <summary>
/// Renders the context block shown after switching to a session:
/// a session header Rule, branch table, and (if forks exist) a branch tree.
/// </summary>
public class SessionContextDisplay : UIComponent
{
    public required SessionDto Session { get; set; }
    public required BranchDto[] Branches { get; set; }
    public BranchDto? ActiveBranch { get; set; }

    public override IRenderable Render()
    {
        var rows = new List<IRenderable>
        {
            new Rule($"[{Theme.Markup(Theme.Text.Accent)}]Session [bold]{Session.Id[..8]}[/]…[/]  " +
                     $"[dim]created {UIHelpers.RelativeTime(Session.CreatedAt)}[/]")
                .LeftJustified()
                .RuleStyle(new Style(Theme.Text.Accent)),
            new Text("")
        };

        if (Branches.Length == 0)
        {
            rows.Add(new Markup("[dim]  No branches found.[/]"));
            return new Rows(rows);
        }

        rows.Add(BuildBranchTable());

        if (Branches.Any(b => b.ForkedFrom != null))
        {
            rows.Add(new Text(""));
            rows.Add(BuildBranchTree());
        }

        return new Rows(rows);
    }

    private IRenderable BuildBranchTable()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(Theme.Text.Muted))
            .AddColumn(new TableColumn("[dim]Branch[/]"))
            .AddColumn(new TableColumn("[dim]Messages[/]").RightAligned())
            .AddColumn(new TableColumn("[dim]Last Active[/]"))
            .AddColumn(new TableColumn("[dim]Tags[/]"));

        var sorted = Branches
            .OrderByDescending(b => b.IsOriginal)
            .ThenByDescending(b => b.LastActivity);

        foreach (var branch in sorted)
        {
            var isActive  = branch.Id == ActiveBranch?.Id;
            var prefix    = branch.ForkedFrom != null ? "  └─ " : "";
            var namePart  = isActive
                ? $"[bold cyan]{prefix}{Markup.Escape(branch.Name)}[/] [dim cyan]✦[/]"
                : $"{prefix}{Markup.Escape(branch.Name)}";
            var tags = branch.Tags is { Count: > 0 }
                ? string.Join(" ", branch.Tags.Select(t => $"[dim grey]{Markup.Escape(t)}[/]"))
                : "[dim]—[/]";

            table.AddRow(
                namePart,
                $"[dim]{branch.MessageCount}[/]",
                $"[dim]{UIHelpers.RelativeTime(branch.LastActivity)}[/]",
                tags);
        }

        return table;
    }

    private Tree BuildBranchTree()
    {
        var childrenOf = Branches
            .Where(b => b.ForkedFrom != null)
            .GroupBy(b => b.ForkedFrom!)
            .ToDictionary(g => g.Key, g => g.ToList());

        var tree = new Tree($"[{Theme.Markup(Theme.Text.Muted)}]Branch tree[/]")
        {
            Style = new Style(Theme.Text.Muted),
            Guide = TreeGuide.Line
        };

        foreach (var root in Branches.Where(b => b.ForkedFrom == null))
            AddChildren(tree.AddNode(FormatNode(root)), root.Id, childrenOf);

        return tree;
    }

    private static void AddChildren(TreeNode node, string branchId, Dictionary<string, List<BranchDto>> childrenOf)
    {
        if (!childrenOf.TryGetValue(branchId, out var children)) return;
        foreach (var child in children.OrderBy(c => c.SiblingIndex))
            AddChildren(node.AddNode(FormatNode(child)), child.Id, childrenOf);
    }

    private static string FormatNode(BranchDto b)
    {
        var tags = b.Tags is { Count: > 0 }
            ? $"  [dim grey]{string.Join(", ", b.Tags)}[/]"
            : "";
        return $"[white]{Markup.Escape(b.Name)}[/]  " +
               $"[dim]{b.MessageCount} msg · {UIHelpers.RelativeTime(b.LastActivity)}[/]{tags}";
    }
}

// ============================================================================
// TOOL COMPONENTS
// ============================================================================

/// <summary>
/// Detected content types for smart UI rendering.
/// Add new types here when extending the UI.
/// </summary>
public enum ResultType
{
    /// <summary>Plain text, no special formatting</summary>
    Plain,

    /// <summary>Unified diff format (file changes)</summary>
    Diff,

    /// <summary>Markdown or ASCII table</summary>
    Table,

    /// <summary>Valid JSON object or array</summary>
    Json,

    /// <summary>Error message (starts with "Error:")</summary>
    Error
}

/// <summary>
/// Detects the content type of tool results for smart UI rendering.
///
/// This enables Toolkit-agnostic UI: instead of checking tool names,
/// we detect what the content IS and render accordingly.
///
/// To add a new result type:
/// 1. Add enum value to ResultType
/// 2. Add detection method (IsXxx)
/// 3. Add check in Detect() method
/// 4. Create renderer in Components/
/// 5. Wire in AgentUIRenderer.RenderResultByType()
/// </summary>
public static class ResultDetector
{
    /// <summary>
    /// Returns true if the result is an error message.
    /// </summary>
    public static bool IsError(string result) =>
        result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Detect the content type of a tool result.
    /// </summary>
    public static ResultType Detect(string result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return ResultType.Plain;

        // Error takes priority
        if (IsError(result))
            return ResultType.Error;

        // Check specific formats (order matters - most specific first)
        if (IsDiff(result))
            return ResultType.Diff;

        if (IsJson(result))
            return ResultType.Json;

        if (IsTable(result))
            return ResultType.Table;

        return ResultType.Plain;
    }

    /// <summary>
    /// Detect unified diff format.
    /// Matches:
    /// - Standard unified diff (--- / +++ / @@)
    /// - Simple +/- line prefixes
    /// </summary>
    private static bool IsDiff(string text)
    {
        // Standard unified diff markers
        if (text.Contains("---") && text.Contains("+++"))
            return true;

        // Hunk headers
        if (text.Contains("@@ "))
            return true;

        // Multiple +/- prefixed lines (simple diff)
        var lines = text.Split('\n');
        var diffLineCount = lines.Count(l =>
            l.StartsWith("+") || l.StartsWith("-"));

        // At least 3 diff lines to avoid false positives
        return diffLineCount > 2;
    }

    /// <summary>
    /// Detect valid JSON (object or array).
    /// </summary>
    private static bool IsJson(string text)
    {
        var trimmed = text.TrimStart();

        // Quick check for JSON start characters
        if (!trimmed.StartsWith("{") && !trimmed.StartsWith("["))
            return false;

        // Validate by parsing
        try
        {
            System.Text.Json.JsonDocument.Parse(trimmed);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Detect markdown or ASCII tables.
    /// Matches:
    /// - Markdown tables (| col1 | col2 |)
    /// - Box-drawing tables (┌─┬─┐)
    /// </summary>
    private static bool IsTable(string text)
    {
        var lines = text.Split('\n');

        // Markdown table: multiple lines starting with |
        var pipeLines = lines.Count(l =>
            l.Contains('|') && l.Trim().StartsWith("|"));
        if (pipeLines >= 2)
            return true;

        // Box-drawing table
        if (lines.Any(l => l.Contains('┌') || l.Contains('├') || l.Contains('└')))
            return true;

        return false;
    }
}

/// <summary>
/// Displays a single tool call with status, args, and result.
/// Mirrors Gemini's ToolMessage.tsx
/// </summary>
public class ToolMessage : UIComponent
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Args { get; set; }
    public string? Result { get; set; }
    public ToolCallStatus Status { get; set; } = ToolCallStatus.Pending;
    
    public override IRenderable Render()
    {
        var (icon, color) = GetStatusIndicator();
        
        var panel = new Panel(RenderContent())
            .Header($"[{color}]{icon}[/] [{Theme.Tool.Header}]{Markup.Escape(Name)}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(GetBorderColor())
            .Padding(1, 0).CapToTerminal();
            
        return panel;
    }
    
    private IRenderable RenderContent()
    {
        var rows = new List<IRenderable>();

        // Description
        if (!string.IsNullOrEmpty(Description))
        {
            rows.Add(new Markup($"[dim]{Markup.Escape(Description)}[/]"));
            rows.Add(new Text(""));
        }

        // Result
        if (!string.IsNullOrEmpty(Result))
        {
            var resultColor = Status == ToolCallStatus.Error ? Theme.Status.Error : Theme.Status.Success;
            var resultIcon = Status == ToolCallStatus.Error ? "✗" : "✓";

            rows.Add(new Markup($"[{resultColor}]{resultIcon} Result:[/]"));

            // Truncate long results
            if (Result.Length > 1000)
                rows.Add(new Markup(Markup.Escape(Result[..1000]) + "\n[dim]... (truncated)[/]"));
            else
                rows.Add(new Text(Result));
        }

        // Executing indicator
        if (Status == ToolCallStatus.Executing)
        {
            rows.Add(new Markup("[cyan]◐ Executing...[/]"));
        }

        return new Rows(rows);
    }
    
    private (string icon, Color color) GetStatusIndicator() => Status switch
    {
        ToolCallStatus.Pending => ("○", Theme.Status.Pending),
        ToolCallStatus.Executing => ("◐", Theme.Status.Executing),
        ToolCallStatus.Completed => ("●", Theme.Status.Success),
        ToolCallStatus.Error => ("✗", Theme.Status.Error),
        _ => ("○", Theme.Status.Pending)
    };
    
    private Color GetBorderColor() => Status switch
    {
        ToolCallStatus.Pending => Theme.Tool.Border,
        ToolCallStatus.Executing => Theme.Status.Executing,
        ToolCallStatus.Completed => Theme.Status.Success,
        ToolCallStatus.Error => Theme.Status.Error,
        _ => Theme.Tool.Border
    };
}

// ============================================================================
// DIFF RENDERING COMPONENTS
// ============================================================================

/// <summary>
/// Renders unified diff with syntax highlighting using DiffPlex.
/// Supports both unified diff format parsing and side-by-side comparison.
/// </summary>
public class DiffRenderer : UIComponent
{
    public string DiffContent { get; set; } = "";
    public string? OldContent { get; set; }
    public string? NewContent { get; set; }
    public string? Filename { get; set; }
    public int MaxLines { get; set; } = 50;
    public bool ShowSideBySide { get; set; } = false;
    public bool IgnoreWhitespace { get; set; } = true;

    public override IRenderable Render()
    {
        // If we have OldContent and NewContent, use DiffPlex directly
        if (OldContent != null && NewContent != null)
        {
            return ShowSideBySide
                ? RenderSideBySideDiff()
                : RenderInlineDiff();
        }

        // Otherwise parse unified diff format
        return RenderUnifiedDiffFormat();
    }

    private IRenderable RenderInlineDiff()
    {
        // Use SideBySideDiffBuilder instead to get SubPieces for word-level highlighting
        var differ = new DiffPlex.DiffBuilder.SideBySideDiffBuilder(new DiffPlex.Differ());
        var sideBySideDiff = differ.BuildDiffModel(OldContent!, NewContent!, IgnoreWhitespace);

        if (!sideBySideDiff.OldText.HasDifferences && !sideBySideDiff.NewText.HasDifferences)
            return new Text("[dim]No changes[/]");

        var rows = new List<IRenderable>();

        // Header with filename
        if (!string.IsNullOrEmpty(Filename))
        {
            rows.Add(new Markup($"[bold]{Markup.Escape(Filename)}[/]"));
        }

        // Merge old and new lines for inline view
        var maxLines = Math.Max(sideBySideDiff.OldText.Lines.Count, sideBySideDiff.NewText.Lines.Count);
        var additions = 0;
        var deletions = 0;
        var modifications = 0;

        for (int i = 0; i < maxLines; i++)
        {
            var oldLine = i < sideBySideDiff.OldText.Lines.Count ? sideBySideDiff.OldText.Lines[i] : null;
            var newLine = i < sideBySideDiff.NewText.Lines.Count ? sideBySideDiff.NewText.Lines[i] : null;

            if (oldLine?.Type == DiffPlex.DiffBuilder.Model.ChangeType.Deleted) deletions++;
            if (newLine?.Type == DiffPlex.DiffBuilder.Model.ChangeType.Inserted) additions++;
            if (oldLine?.Type == DiffPlex.DiffBuilder.Model.ChangeType.Modified || newLine?.Type == DiffPlex.DiffBuilder.Model.ChangeType.Modified) modifications++;
        }

        rows.Add(new Markup($"[{Theme.Diff.Added}]+{additions}[/] [{Theme.Diff.Removed}]-{deletions}[/] [{Theme.Status.Warning}]~{modifications}[/]"));
        rows.Add(new Text(""));

        // Render diff lines in inline format (old then new)
        int lineCount = 0;
        for (int i = 0; i < maxLines && lineCount < MaxLines; i++)
        {
            var oldLine = i < sideBySideDiff.OldText.Lines.Count ? sideBySideDiff.OldText.Lines[i] : null;
            var newLine = i < sideBySideDiff.NewText.Lines.Count ? sideBySideDiff.NewText.Lines[i] : null;

            // For unchanged lines, show once
            if (oldLine?.Type == DiffPlex.DiffBuilder.Model.ChangeType.Unchanged)
            {
                rows.Add(RenderInlineDiffLine(oldLine, isOldLine: false));
                lineCount++;
                continue;
            }

            // For modified lines, show BOTH old and new versions on separate lines
            if (oldLine?.Type == DiffPlex.DiffBuilder.Model.ChangeType.Modified)
            {
                rows.Add(RenderInlineDiffLine(oldLine, isOldLine: true));
                lineCount++;
            }

            if (newLine?.Type == DiffPlex.DiffBuilder.Model.ChangeType.Modified)
            {
                rows.Add(RenderInlineDiffLine(newLine, isOldLine: false));
                lineCount++;
                continue;
            }

            // For deleted lines
            if (oldLine != null && oldLine.Type == DiffPlex.DiffBuilder.Model.ChangeType.Deleted)
            {
                rows.Add(RenderInlineDiffLine(oldLine, isOldLine: true));
                lineCount++;
            }

            // For inserted lines
            if (newLine != null && newLine.Type == DiffPlex.DiffBuilder.Model.ChangeType.Inserted)
            {
                rows.Add(RenderInlineDiffLine(newLine, isOldLine: false));
                lineCount++;
            }
        }

        // Truncation notice
        if (lineCount >= MaxLines)
        {
            rows.Add(new Markup($"[dim]... more lines truncated[/]"));
        }

        return new Panel(new Rows(rows))
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Tool.Border)
            .Padding(1, 0).CapToTerminal();
    }

    private IRenderable RenderInlineDiffLine(DiffPlex.DiffBuilder.Model.DiffPiece piece, bool isOldLine)
    {
        var (prefix, color) = piece.Type switch
        {
            DiffPlex.DiffBuilder.Model.ChangeType.Inserted => ("+", Theme.Diff.Added),
            DiffPlex.DiffBuilder.Model.ChangeType.Deleted => ("-", Theme.Diff.Removed),
            DiffPlex.DiffBuilder.Model.ChangeType.Modified => ("~", Theme.Status.Warning),
            _ => (" ", Theme.Diff.Context)
        };

        var lineNum = piece.Position?.ToString().PadLeft(4) ?? "    ";

        var content = Markup.Escape(piece.Text ?? "");
        return new Markup($"[dim]{lineNum}[/] [{color}]{prefix}{content}[/]");
    }

    private IRenderable RenderSideBySideDiff()
    {
        var differ = new DiffPlex.DiffBuilder.SideBySideDiffBuilder(new DiffPlex.Differ());
        var diff = differ.BuildDiffModel(OldContent!, NewContent!, IgnoreWhitespace);

        if (!diff.OldText.HasDifferences && !diff.NewText.HasDifferences)
            return new Text("[dim]No changes[/]");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Theme.Tool.Border);

        // Add columns for side-by-side view
        table.AddColumn(new TableColumn("[dim]Old[/]").Width(60));
        table.AddColumn(new TableColumn("[dim]New[/]").Width(60));

        // Header with filename
        if (!string.IsNullOrEmpty(Filename))
        {
            table.Caption = new TableTitle($"[bold]{Markup.Escape(Filename)}[/]");
        }

        // Render lines side by side
        var maxLines = Math.Min(MaxLines, Math.Max(diff.OldText.Lines.Count, diff.NewText.Lines.Count));
        for (int i = 0; i < maxLines; i++)
        {
            var oldLine = i < diff.OldText.Lines.Count ? diff.OldText.Lines[i] : null;
            var newLine = i < diff.NewText.Lines.Count ? diff.NewText.Lines[i] : null;

            table.AddRow(
                RenderSideBySidePiece(oldLine, i + 1, isOldSide: true),
                RenderSideBySidePiece(newLine, i + 1, isOldSide: false)
            );
        }

        // Truncation notice
        var totalLines = Math.Max(diff.OldText.Lines.Count, diff.NewText.Lines.Count);
        if (totalLines > MaxLines)
        {
            table.AddRow(
                new Markup($"[dim]... {totalLines - MaxLines} more lines[/]"),
                new Markup($"[dim]... {totalLines - MaxLines} more lines[/]")
            );
        }

        return table;
    }

    private IRenderable RenderUnifiedDiffFormat()
    {
        // Parse unified diff format (legacy support for tools that output unified diffs)
        var lines = DiffContent.Split('\n');
        var rows = new List<IRenderable>();

        // Header with filename
        if (!string.IsNullOrEmpty(Filename))
        {
            rows.Add(new Markup($"[bold]{Markup.Escape(Filename)}[/]"));
        }

        // Count stats
        int additions = 0, deletions = 0;
        foreach (var line in lines)
        {
            if (line.StartsWith("+") && !line.StartsWith("+++")) additions++;
            if (line.StartsWith("-") && !line.StartsWith("---")) deletions++;
        }

        rows.Add(new Markup($"[{Theme.Diff.Added}]+{additions}[/] [{Theme.Diff.Removed}]-{deletions}[/]"));
        rows.Add(new Text(""));

        // Render diff lines
        var displayLines = lines.Take(MaxLines).ToList();
        foreach (var line in displayLines)
        {
            rows.Add(RenderUnifiedDiffLine(line));
        }

        // Truncation notice
        if (lines.Length > MaxLines)
        {
            rows.Add(new Markup($"[dim]... {lines.Length - MaxLines} more lines[/]"));
        }

        return new Panel(new Rows(rows))
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Tool.Border)
            .Padding(1, 0).CapToTerminal();
    }


    private IRenderable RenderSideBySidePiece(DiffPlex.DiffBuilder.Model.DiffPiece? piece, int lineNum, bool isOldSide)
    {
        if (piece == null || piece.Type == DiffPlex.DiffBuilder.Model.ChangeType.Imaginary)
        {
            return new Markup("[dim]   [/]");
        }

        var color = piece.Type switch
        {
            DiffPlex.DiffBuilder.Model.ChangeType.Inserted => Theme.Diff.Added,
            DiffPlex.DiffBuilder.Model.ChangeType.Deleted => Theme.Diff.Removed,
            DiffPlex.DiffBuilder.Model.ChangeType.Modified => Theme.Status.Warning,
            _ => Theme.Diff.Context
        };

        var lineNumStr = piece.Position?.ToString().PadLeft(4) ?? "    ";
        var content = Markup.Escape(piece.Text ?? "");

        return new Markup($"[dim]{lineNumStr}[/] [{color}]{content}[/]");
    }

    private IRenderable RenderUnifiedDiffLine(string line)
    {
        if (line.StartsWith("+++") || line.StartsWith("---"))
        {
            return new Markup($"[{Theme.Text.Muted}]{Markup.Escape(line)}[/]");
        }

        if (line.StartsWith("@@"))
        {
            return new Markup($"[{Theme.Diff.Hunk}]{Markup.Escape(line)}[/]");
        }

        var (prefix, color) = (line.Length > 0 ? line[0] : ' ') switch
        {
            '+' => ("+", Theme.Diff.Added),
            '-' => ("-", Theme.Diff.Removed),
            _ => (" ", Theme.Diff.Context)
        };

        var content = line.Length > 1 ? Markup.Escape(line.Substring(1)) : "";
        return new Markup($"[{color}]{prefix}{content}[/]");
    }
}

