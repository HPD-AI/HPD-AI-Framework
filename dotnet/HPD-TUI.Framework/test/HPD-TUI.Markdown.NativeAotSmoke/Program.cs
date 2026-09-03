using HPD.TUI.Core;
using HPD.TUI.Markdown;

const string source = "# Native AOT\n\n| Value |\n|---|\n| [**linked**](https://example.com) |\n\n```csharp\nvar value = 42;\n```";
var pipeline = MarkdownPipelineFactory.CreateDefault();
var snapshot = new MarkdownDocumentParser().Parse(source, new MarkdownParseOptions { Pipeline = pipeline });
var engine = new MarkdownLayoutEngine();
var options = new MarkdownLayoutOptions(60, MarkdownTheme.FromTheme(Theme.Default));
var rich = engine.Layout(snapshot, options);
var raw = engine.Layout(snapshot, options with { Mode = MarkdownPresentationMode.Raw });

if (rich.Rows.IsEmpty || raw.Rows.IsEmpty || snapshot.Source != source)
    return 1;
if (!rich.Rows.SelectMany(static row => row.Line.Runs).Any(static run => run.Hyperlink is not null))
    return 2;
if (string.Concat(raw.Rows.SelectMany(static row => row.Line.Runs).Select(static run => run.Text))
    .Contains('\u001b', StringComparison.Ordinal))
    return 3;
return 0;
