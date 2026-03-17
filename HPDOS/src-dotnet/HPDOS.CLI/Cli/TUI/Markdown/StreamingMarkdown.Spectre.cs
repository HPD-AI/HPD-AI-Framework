using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Spectre.Console;
using Spectre.Console.Rendering;
using HPDOS.Shell.Cli.TUI.Markdown;

namespace HPDOS.Shell.Cli.TUI.Markdown;

// ============================================================================
// SYNTAX HIGHLIGHTER - Keyword-based syntax highlighting for common languages
// ============================================================================

/// <summary>
/// Keyword-based syntax highlighter for common programming languages.
/// Outputs Spectre.Console markup strings like [blue]keyword[/].
///
/// Based on Codex CLI's approach but simplified for .NET without tree-sitter.
/// Uses keyword matching and regex patterns for common token types.
/// </summary>
public class SpectreSyntaxHighlighter : ISyntaxHighlighter
{
    // Language keyword definitions
    private static readonly Dictionary<string, LanguageDefinition> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["csharp"] = new LanguageDefinition
        {
            Keywords = new HashSet<string> { "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "var", "virtual", "void", "volatile", "while", "async", "await", "record", "init", "required", "file", "global", "scoped", "nameof", "when", "where", "yield" },
            Types = new HashSet<string> { "string", "int", "bool", "double", "float", "decimal", "long", "short", "byte", "char", "object", "void", "var", "dynamic", "Task", "List", "Dictionary", "IEnumerable", "Action", "Func" },
            CommentPrefixes = new[] { "//", "/*" },
        },
        ["cs"] = null!, // Alias - set below
        ["c#"] = null!, // Alias - set below

        ["javascript"] = new LanguageDefinition
        {
            Keywords = new HashSet<string> { "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else", "export", "extends", "false", "finally", "for", "from", "function", "get", "if", "import", "in", "instanceof", "let", "new", "null", "of", "return", "set", "static", "super", "switch", "this", "throw", "true", "try", "typeof", "undefined", "var", "void", "while", "with", "yield" },
            Types = new HashSet<string> { "Array", "Boolean", "Date", "Error", "Function", "JSON", "Map", "Math", "Number", "Object", "Promise", "RegExp", "Set", "String", "Symbol", "WeakMap", "WeakSet" },
            CommentPrefixes = new[] { "//", "/*" },
        },
        ["js"] = null!, // Alias
        ["typescript"] = null!, // Similar to JS
        ["ts"] = null!, // Alias

        ["python"] = new LanguageDefinition
        {
            Keywords = new HashSet<string> { "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif", "else", "except", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try", "while", "with", "yield" },
            Types = new HashSet<string> { "int", "str", "float", "bool", "list", "dict", "set", "tuple", "bytes", "type", "object", "None" },
            CommentPrefixes = new[] { "#" },
        },
        ["py"] = null!, // Alias

        ["rust"] = new LanguageDefinition
        {
            Keywords = new HashSet<string> { "as", "async", "await", "break", "const", "continue", "crate", "dyn", "else", "enum", "extern", "false", "fn", "for", "if", "impl", "in", "let", "loop", "match", "mod", "move", "mut", "pub", "ref", "return", "self", "Self", "static", "struct", "super", "trait", "true", "type", "unsafe", "use", "where", "while" },
            Types = new HashSet<string> { "i8", "i16", "i32", "i64", "i128", "isize", "u8", "u16", "u32", "u64", "u128", "usize", "f32", "f64", "bool", "char", "str", "String", "Vec", "Option", "Result", "Box", "Rc", "Arc" },
            CommentPrefixes = new[] { "//", "/*" },
        },
        ["rs"] = null!, // Alias

        ["go"] = new LanguageDefinition
        {
            Keywords = new HashSet<string> { "break", "case", "chan", "const", "continue", "default", "defer", "else", "fallthrough", "for", "func", "go", "goto", "if", "import", "interface", "map", "package", "range", "return", "select", "struct", "switch", "type", "var" },
            Types = new HashSet<string> { "bool", "byte", "complex64", "complex128", "error", "float32", "float64", "int", "int8", "int16", "int32", "int64", "rune", "string", "uint", "uint8", "uint16", "uint32", "uint64", "uintptr" },
            CommentPrefixes = new[] { "//", "/*" },
        },

        ["java"] = new LanguageDefinition
        {
            Keywords = new HashSet<string> { "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class", "const", "continue", "default", "do", "double", "else", "enum", "extends", "false", "final", "finally", "float", "for", "goto", "if", "implements", "import", "instanceof", "int", "interface", "long", "native", "new", "null", "package", "private", "protected", "public", "return", "short", "static", "strictfp", "super", "switch", "synchronized", "this", "throw", "throws", "transient", "true", "try", "void", "volatile", "while", "var", "record", "sealed", "permits", "yield" },
            Types = new HashSet<string> { "String", "Integer", "Long", "Double", "Float", "Boolean", "Character", "Byte", "Short", "Object", "Class", "List", "Map", "Set", "ArrayList", "HashMap", "HashSet" },
            CommentPrefixes = new[] { "//", "/*" },
        },

        ["bash"] = new LanguageDefinition
        {
            Keywords = new HashSet<string> { "if", "then", "else", "elif", "fi", "case", "esac", "for", "while", "until", "do", "done", "in", "function", "select", "time", "coproc", "return", "exit", "break", "continue", "declare", "typeset", "local", "export", "readonly", "unset", "shift", "trap", "eval", "exec", "source" },
            Types = new HashSet<string>(),
            CommentPrefixes = new[] { "#" },
        },
        ["sh"] = null!, // Alias
        ["shell"] = null!, // Alias
        ["zsh"] = null!, // Alias

        ["sql"] = new LanguageDefinition
        {
            Keywords = new HashSet<string> { "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "CREATE", "DROP", "ALTER", "TABLE", "INDEX", "VIEW", "DATABASE", "SCHEMA", "INTO", "VALUES", "SET", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "ON", "AND", "OR", "NOT", "NULL", "IS", "IN", "BETWEEN", "LIKE", "ORDER", "BY", "GROUP", "HAVING", "LIMIT", "OFFSET", "UNION", "ALL", "DISTINCT", "AS", "CASE", "WHEN", "THEN", "ELSE", "END", "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "CONSTRAINT", "DEFAULT", "CHECK", "UNIQUE", "CASCADE", "TRUNCATE", "BEGIN", "COMMIT", "ROLLBACK", "TRANSACTION", "GRANT", "REVOKE" },
            Types = new HashSet<string> { "INT", "INTEGER", "BIGINT", "SMALLINT", "TINYINT", "FLOAT", "DOUBLE", "DECIMAL", "NUMERIC", "CHAR", "VARCHAR", "TEXT", "DATE", "TIME", "DATETIME", "TIMESTAMP", "BOOLEAN", "BOOL", "BLOB", "JSON" },
            CommentPrefixes = new[] { "--", "/*" },
            CaseInsensitive = true,
        },

        ["json"] = new LanguageDefinition
        {
            Keywords = new HashSet<string> { "true", "false", "null" },
            Types = new HashSet<string>(),
            CommentPrefixes = Array.Empty<string>(),
        },

        ["yaml"] = new LanguageDefinition
        {
            Keywords = new HashSet<string> { "true", "false", "null", "yes", "no", "on", "off" },
            Types = new HashSet<string>(),
            CommentPrefixes = new[] { "#" },
        },
        ["yml"] = null!, // Alias

        ["xml"] = new LanguageDefinition
        {
            Keywords = new HashSet<string>(),
            Types = new HashSet<string>(),
            CommentPrefixes = Array.Empty<string>(),
            IsXml = true,
        },
        ["html"] = null!, // Similar to XML
        ["svg"] = null!, // Alias

        ["markdown"] = new LanguageDefinition
        {
            Keywords = new HashSet<string>(),
            Types = new HashSet<string>(),
            CommentPrefixes = Array.Empty<string>(),
        },
        ["md"] = null!, // Alias
    };

    static SpectreSyntaxHighlighter()
    {
        // Set up aliases
        Languages["cs"] = Languages["csharp"];
        Languages["c#"] = Languages["csharp"];
        Languages["js"] = Languages["javascript"];
        Languages["typescript"] = Languages["javascript"];
        Languages["ts"] = Languages["javascript"];
        Languages["py"] = Languages["python"];
        Languages["rs"] = Languages["rust"];
        Languages["sh"] = Languages["bash"];
        Languages["shell"] = Languages["bash"];
        Languages["zsh"] = Languages["bash"];
        Languages["yml"] = Languages["yaml"];
        Languages["html"] = Languages["xml"];
        Languages["svg"] = Languages["xml"];
        Languages["md"] = Languages["markdown"];
    }

    /// <inheritdoc />
    public bool IsLanguageSupported(string? language)
    {
        return !string.IsNullOrEmpty(language) && Languages.ContainsKey(language) && Languages[language] != null;
    }

    /// <inheritdoc />
    public string Highlight(string code, string? language)
    {
        if (string.IsNullOrEmpty(code))
            return "";

        if (string.IsNullOrEmpty(language) || !Languages.TryGetValue(language, out var def) || def == null)
        {
            // No highlighting - just escape for Spectre
            return Markup.Escape(code);
        }

        return HighlightWithDefinition(code, def);
    }

    private static string HighlightWithDefinition(string code, LanguageDefinition def)
    {
        var result = new System.Text.StringBuilder();
        var lines = code.Split('\n');

        for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var line = lines[lineIdx];
            var highlightedLine = HighlightLine(line, def);
            result.Append(highlightedLine);

            if (lineIdx < lines.Length - 1)
                result.Append('\n');
        }

        return result.ToString();
    }

    private static string HighlightLine(string line, LanguageDefinition def)
    {
        var result = new System.Text.StringBuilder();
        int i = 0;

        while (i < line.Length)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(line[i]))
            {
                result.Append(line[i]);
                i++;
                continue;
            }

            // Check for comments
            bool matchedComment = false;
            foreach (var prefix in def.CommentPrefixes)
            {
                if (line.Substring(i).StartsWith(prefix))
                {
                    // Rest of line is a comment
                    var comment = line.Substring(i);
                    result.Append($"[dim]{Markup.Escape(comment)}[/]");
                    i = line.Length;
                    matchedComment = true;
                    break;
                }
            }
            if (matchedComment) continue;

            // Check for XML tags
            if (def.IsXml && line[i] == '<')
            {
                int end = line.IndexOf('>', i);
                if (end > i)
                {
                    end++;
                    var tag = line.Substring(i, end - i);
                    result.Append($"[blue]{Markup.Escape(tag)}[/]");
                    i = end;
                    continue;
                }
            }

            // Check for strings
            bool matchedString = false;
            foreach (var strStart in new[] { "\"\"\"", "'''", "@\"", "$\"", "\"", "'", "`" })
            {
                if (line.Substring(i).StartsWith(strStart))
                {
                    var endDelim = strStart switch
                    {
                        "@\"" => "\"",
                        "$\"" => "\"",
                        "\"\"\"" => "\"\"\"",
                        "'''" => "'''",
                        _ => strStart
                    };

                    int end = FindStringEnd(line, i + strStart.Length, endDelim);
                    var str = line.Substring(i, end - i);
                    result.Append($"[yellow]{Markup.Escape(str)}[/]");
                    i = end;
                    matchedString = true;
                    break;
                }
            }
            if (matchedString) continue;

            // Check for numbers
            if (char.IsDigit(line[i]) || (line[i] == '-' && i + 1 < line.Length && char.IsDigit(line[i + 1])))
            {
                int end = i + 1;
                while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '.' || line[end] == 'e' || line[end] == 'E' || line[end] == 'f' || line[end] == 'F' || line[end] == 'd' || line[end] == 'D' || line[end] == 'l' || line[end] == 'L' || line[end] == 'm' || line[end] == 'M' || line[end] == 'x' || line[end] == 'X' || (line[end] >= 'a' && line[end] <= 'f') || (line[end] >= 'A' && line[end] <= 'F')))
                    end++;
                var num = line.Substring(i, end - i);
                result.Append($"[green]{Markup.Escape(num)}[/]");
                i = end;
                continue;
            }

            // Check for identifiers/keywords
            if (char.IsLetter(line[i]) || line[i] == '_')
            {
                int end = i + 1;
                while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
                    end++;
                var word = line.Substring(i, end - i);

                var keywordMatch = def.CaseInsensitive
                    ? def.Keywords.Any(k => k.Equals(word, StringComparison.OrdinalIgnoreCase))
                    : def.Keywords.Contains(word);

                var typeMatch = def.CaseInsensitive
                    ? def.Types.Any(t => t.Equals(word, StringComparison.OrdinalIgnoreCase))
                    : def.Types.Contains(word);

                if (keywordMatch)
                {
                    result.Append($"[blue]{Markup.Escape(word)}[/]");
                }
                else if (typeMatch)
                {
                    result.Append($"[cyan]{Markup.Escape(word)}[/]");
                }
                else
                {
                    result.Append(Markup.Escape(word));
                }
                i = end;
                continue;
            }

            // Operators and punctuation - dim
            if ("+-*/%=<>!&|^~?:;,.()[]{}".Contains(line[i]))
            {
                result.Append($"[dim]{Markup.Escape(line[i].ToString())}[/]");
                i++;
                continue;
            }

            // Default - just escape
            result.Append(Markup.Escape(line[i].ToString()));
            i++;
        }

        return result.ToString();
    }

    private static int FindStringEnd(string line, int start, string delimiter)
    {
        int i = start;
        while (i < line.Length)
        {
            if (line.Substring(i).StartsWith(delimiter))
            {
                return i + delimiter.Length;
            }
            if (line[i] == '\\' && i + 1 < line.Length)
            {
                i += 2; // Skip escaped character
                continue;
            }
            i++;
        }
        return line.Length; // Unclosed string - return end of line
    }

    private class LanguageDefinition
    {
        public HashSet<string> Keywords { get; init; } = new();
        public HashSet<string> Types { get; init; } = new();
        public string[] CommentPrefixes { get; init; } = Array.Empty<string>();
        public bool CaseInsensitive { get; init; } = false;
        public bool IsXml { get; init; } = false;
    }
}

// ============================================================================
// MARKDOWN RENDERER - Renders markdown AST to Spectre.Console renderables
// ============================================================================

/// <summary>
/// Renders Markdown AST to Spectre.Console renderables.
/// Implements IMarkdownRenderer for integration with StreamCollector.
///
/// Supports the same elements as Codex CLI and Gemini CLI:
/// - Headers (H1-H6) with style variations
/// - Fenced and indented code blocks with syntax highlighting
/// - Bold, italic, strikethrough, inline code
/// - Ordered and unordered lists with nesting
/// - Blockquotes with nesting
/// - Horizontal rules
/// - Links and auto-links
/// - Tables (pipe-delimited)
/// </summary>
public class SpectreMarkdownRenderer : IMarkdownRenderer<IRenderable>
{
    private readonly MarkdownPipeline _pipeline;
    private readonly ISyntaxHighlighter? _syntaxHighlighter;

    /// <summary>
    /// Creates a new SpectreMarkdownRenderer with optional syntax highlighting.
    /// </summary>
    /// <param name="syntaxHighlighter">Optional syntax highlighter for code blocks</param>
    public SpectreMarkdownRenderer(ISyntaxHighlighter? syntaxHighlighter = null)
    {
        _syntaxHighlighter = syntaxHighlighter ?? new SpectreSyntaxHighlighter();

        // Configure Markdig with extensions we need
        _pipeline = new MarkdownPipelineBuilder()
            .UseEmphasisExtras()      // ~~strikethrough~~
            .UseAutoLinks()           // Auto-detect URLs
            .UseTaskLists()           // - [ ] checkboxes
            .UsePipeTables()          // | pipe | tables |
            .Build();
    }

    /// <summary>
    /// Render markdown string to a Spectre IRenderable.
    /// </summary>
    public IRenderable Render(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return new Text("");

        try
        {
            var document = Markdig.Markdown.Parse(markdown, _pipeline);
            var renderables = new List<IRenderable>();

            foreach (var block in document)
            {
                var rendered = RenderBlock(block);
                if (rendered != null)
                    renderables.Add(rendered);
            }

            return renderables.Count == 0
                ? new Text(markdown)
                : new Rows(renderables);
        }
        catch
        {
            // Fallback to plain text if parsing fails
            return new Text(markdown);
        }
    }

    private IRenderable? RenderBlock(Block block)
    {
        return block switch
        {
            HeadingBlock heading => RenderHeading(heading),
            ParagraphBlock paragraph => RenderParagraph(paragraph),
            FencedCodeBlock fencedCode => RenderFencedCodeBlock(fencedCode),
            CodeBlock code => RenderCodeBlock(code),
            ListBlock list => RenderList(list),
            QuoteBlock quote => RenderQuote(quote),
            ThematicBreakBlock => RenderThematicBreak(),
            HtmlBlock html => RenderHtmlBlock(html),
            Markdig.Extensions.Tables.Table table => RenderTable(table),
            _ => RenderUnknownBlock(block)
        };
    }

    private IRenderable RenderTable(Markdig.Extensions.Tables.Table table)
    {
        var spectreTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);

        // First row is always the header in GFM tables
        bool firstRow = true;

        foreach (var row in table.OfType<Markdig.Extensions.Tables.TableRow>())
        {
            var cells = row.OfType<Markdig.Extensions.Tables.TableCell>().ToList();

            if (firstRow)
            {
                // Add columns from the header row
                foreach (var cell in cells)
                {
                    var headerText = RenderTableCellContent(cell);
                    spectreTable.AddColumn(new TableColumn(new Markup($"[bold]{headerText}[/]")));
                }
                firstRow = false;
            }
            else
            {
                var cellRenderables = cells.Select(c => new Markup(RenderTableCellContent(c)) as IRenderable).ToArray();
                spectreTable.AddRow(cellRenderables);
            }
        }

        return new Rows(new Text(""), spectreTable, new Text(""));
    }

    private static string RenderTableCellContent(Markdig.Extensions.Tables.TableCell cell)
    {
        var parts = new System.Text.StringBuilder();
        foreach (var block in cell)
        {
            if (block is ParagraphBlock para)
                parts.Append(RenderInlines(para.Inline));
        }
        return parts.ToString();
    }

    /// <summary>
    /// Render headers with Codex-style formatting:
    /// H1: Bold + Underlined (white)
    /// H2: Bold (cyan)
    /// H3: Bold + Italic (yellow)
    /// H4: Italic
    /// H5-H6: Dim
    /// </summary>
    private IRenderable RenderHeading(HeadingBlock heading)
    {
        var text = GetInlineText(heading.Inline);

        return heading.Level switch
        {
            1 => new Markup($"\n[bold underline white]{Markup.Escape(text)}[/]\n"),
            2 => new Markup($"\n[bold cyan]{Markup.Escape(text)}[/]\n"),
            3 => new Markup($"\n[bold italic yellow]{Markup.Escape(text)}[/]\n"),
            4 => new Markup($"\n[italic]{Markup.Escape(text)}[/]\n"),
            _ => new Markup($"\n[dim bold]{Markup.Escape(text)}[/]\n")
        };
    }

    private IRenderable RenderParagraph(ParagraphBlock paragraph)
    {
        var content = RenderInlines(paragraph.Inline);
        return new Markup(content + "\n");
    }

    /// <summary>
    /// Render fenced code blocks with syntax highlighting.
    /// </summary>
    private IRenderable RenderFencedCodeBlock(FencedCodeBlock codeBlock)
    {
        var language = codeBlock.Info ?? "";
        var code = codeBlock.Lines.ToString().TrimEnd();

        // Apply syntax highlighting if available
        IRenderable codeContent;
        if (_syntaxHighlighter != null && _syntaxHighlighter.IsLanguageSupported(language))
        {
            var highlighted = _syntaxHighlighter.Highlight(code, language);
            codeContent = new Markup(highlighted);
        }
        else
        {
            codeContent = new Text(code);
        }

        var panel = new Panel(codeContent)
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey);

        if (!string.IsNullOrEmpty(language))
        {
            panel.Header($"[yellow]{Markup.Escape(language)}[/]");
        }

        return new Rows(new Text(""), panel, new Text(""));
    }

    /// <summary>
    /// Render indented code blocks (no language - plain text).
    /// </summary>
    private IRenderable RenderCodeBlock(CodeBlock codeBlock)
    {
        var code = codeBlock.Lines.ToString().TrimEnd();

        return new Panel(new Text(code))
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey);
    }

    /// <summary>
    /// Render lists with proper bullet styles and nesting.
    /// Ordered: 1. 2. 3.
    /// Unordered: * o -
    /// </summary>
    private IRenderable RenderList(ListBlock list, int depth = 0)
    {
        var items = new List<IRenderable>();
        var index = 1;
        var indent = new string(' ', depth * 2);

        // Different bullet styles for nesting depth
        var bullet = list.IsOrdered
            ? null // Will use numbers
            : depth switch
            {
                0 => "*",
                1 => "o",
                _ => "-"
            };

        foreach (var item in list)
        {
            if (item is ListItemBlock listItem)
            {
                var marker = list.IsOrdered ? $"[blue]{index}.[/]" : $"[dim]{bullet}[/]";
                var content = RenderListItemContent(listItem, depth);
                items.Add(new Markup($"{indent}{marker} {content}"));
                index++;
            }
        }

        return new Rows(items);
    }

    private string RenderListItemContent(ListItemBlock item, int depth)
    {
        var parts = new List<string>();

        foreach (var block in item)
        {
            if (block is ParagraphBlock para)
            {
                parts.Add(RenderInlines(para.Inline));
            }
            else if (block is ListBlock nestedList)
            {
                // Handle nested lists - render recursively
                parts.Add("\n" + RenderNestedList(nestedList, depth + 1));
            }
        }

        return string.Join(" ", parts);
    }

    private string RenderNestedList(ListBlock list, int depth)
    {
        var items = new List<string>();
        var index = 1;
        var indent = new string(' ', depth * 2);

        var bullet = list.IsOrdered
            ? null
            : depth switch
            {
                1 => "o",
                2 => "-",
                _ => "."
            };

        foreach (var item in list)
        {
            if (item is ListItemBlock listItem)
            {
                var marker = list.IsOrdered ? $"[blue]{index}.[/]" : $"[dim]{bullet}[/]";
                var content = RenderListItemContent(listItem, depth);
                items.Add($"{indent}{marker} {content}");
                index++;
            }
        }

        return string.Join("\n", items);
    }

    /// <summary>
    /// Render blockquotes with green styling (like Codex).
    /// </summary>
    private IRenderable RenderQuote(QuoteBlock quote)
    {
        var content = new List<string>();

        foreach (var block in quote)
        {
            if (block is ParagraphBlock para)
            {
                content.Add(RenderInlines(para.Inline));
            }
            else if (block is QuoteBlock nestedQuote)
            {
                // Nested quote
                content.Add("> " + RenderQuoteContent(nestedQuote));
            }
        }

        var quoteText = string.Join("\n", content);

        // Green quote with bar prefix (like Codex)
        var lines = quoteText.Split('\n');
        var prefixedLines = lines.Select(l => $"[green]|[/] [italic]{l}[/]");

        return new Rows(prefixedLines.Select(l => new Markup(l)).ToArray());
    }

    private string RenderQuoteContent(QuoteBlock quote)
    {
        var parts = new List<string>();
        foreach (var block in quote)
        {
            if (block is ParagraphBlock para)
            {
                parts.Add(RenderInlines(para.Inline));
            }
        }
        return string.Join("\n", parts);
    }

    /// <summary>
    /// Render horizontal rule as a dim line.
    /// </summary>
    private IRenderable RenderThematicBreak()
    {
        return new Rule().RuleStyle("dim");
    }

    private IRenderable RenderHtmlBlock(HtmlBlock html)
    {
        // Render HTML as plain text (escaped)
        var content = html.Lines.ToString().Trim();
        return new Text(content);
    }

    private IRenderable? RenderUnknownBlock(Block block)
    {
        // Try to extract any text content
        var text = block.ToString();
        if (!string.IsNullOrWhiteSpace(text))
            return new Text(text);

        return null;
    }

    /// <summary>
    /// Render inline elements with Spectre markup.
    /// Supports: bold, italic, strikethrough, code, links, auto-links
    /// </summary>
    private static string RenderInlines(ContainerInline? container)
    {
        if (container == null)
            return "";

        var result = new System.Text.StringBuilder();

        foreach (var inline in container)
        {
            result.Append(RenderInline(inline));
        }

        return result.ToString();
    }

    private static string RenderInline(Inline inline)
    {
        return inline switch
        {
            LiteralInline literal => Markup.Escape(literal.Content.ToString()),
            EmphasisInline emphasis => RenderEmphasis(emphasis),
            CodeInline code => RenderInlineCode(code),
            LinkInline link => RenderLink(link),
            LineBreakInline lineBreak => lineBreak.IsHard ? "\n" : " ",
            HtmlInline html => Markup.Escape(html.Tag),
            AutolinkInline autolink => $"[link={autolink.Url}][cyan]{Markup.Escape(autolink.Url)}[/][/]",
            // Task list checkbox
            Markdig.Extensions.TaskLists.TaskList task => task.Checked ? "[green][x][/] " : "[dim][ ][/] ",
            // For unknown inline types, try to get their text content
            ContainerInline container => RenderInlines(container),
            _ => "" // Don't output class names for unknown types
        };
    }

    /// <summary>
    /// Render emphasis with proper nesting support.
    /// *single* = italic
    /// **double** = bold
    /// ***triple*** = bold + italic
    /// ~~strikethrough~~
    /// </summary>
    private static string RenderEmphasis(EmphasisInline emphasis)
    {
        var content = RenderInlines(emphasis);

        // Check for strikethrough (~~text~~)
        if (emphasis.DelimiterChar == '~')
        {
            return $"[strikethrough]{content}[/]";
        }

        // Regular emphasis: * or _
        return emphasis.DelimiterCount switch
        {
            1 => $"[italic]{content}[/]",
            2 => $"[bold]{content}[/]",
            3 => $"[bold italic]{content}[/]", // Bold + italic
            _ => content
        };
    }

    /// <summary>
    /// Render inline code with cyan background (like Codex).
    /// </summary>
    private static string RenderInlineCode(CodeInline code)
    {
        return $"[cyan on grey23]{Markup.Escape(code.Content)}[/]";
    }

    /// <summary>
    /// Render links with cyan color and underline.
    /// Format: text (url)
    /// </summary>
    private static string RenderLink(LinkInline link)
    {
        var text = RenderInlines(link);
        var url = link.Url ?? "";

        if (link.IsImage)
        {
            return $"[dim][img] {Markup.Escape(text)}[/]";
        }

        if (!string.IsNullOrEmpty(url))
        {
            // Clickable link with cyan color
            return $"[link={url}][cyan underline]{text}[/][/]";
        }

        return text;
    }

    /// <summary>
    /// Get plain text content from inline elements.
    /// </summary>
    private static string GetInlineText(ContainerInline? container)
    {
        if (container == null)
            return "";

        var result = new System.Text.StringBuilder();

        foreach (var inline in container)
        {
            result.Append(GetInlineTextContent(inline));
        }

        return result.ToString();
    }

    private static string GetInlineTextContent(Inline inline)
    {
        return inline switch
        {
            LiteralInline literal => literal.Content.ToString(),
            EmphasisInline emphasis => GetInlineText(emphasis),
            CodeInline code => code.Content,
            LinkInline link => GetInlineText(link),
            LineBreakInline => " ",
            ContainerInline container => GetInlineText(container),
            _ => "" // Don't output class names for unknown types
        };
    }
}
