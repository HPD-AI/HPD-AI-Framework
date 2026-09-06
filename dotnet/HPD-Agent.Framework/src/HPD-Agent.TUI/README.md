# HPD-Agent.TUI

Terminal UI shell primitives for HPD agents.

## Install

```bash
dotnet add package HPD-Agent.TUI
```

## Use When

Use this package when you need this HPD Agent capability in an agent application.

## Pre-1.0 API Evolution

HPD-Agent.TUI is still pre-1.0. Until `1.0.0`, releases may refine runtime
interfaces, transcript rendering contracts, and model-selection surfaces as the
terminal experience stabilizes. The current TUI model is session/thread runtime
navigation with transcript cells and renderer registrations.


## Markdown palettes

Markdown colors are independent of the shell's UI theme. Register immutable palettes
when building the application:

```csharp
using HPD.TUI.Core;
using HPD.TUI.Markdown;

var ui = Theme.Default;
var markdown = MarkdownTheme.FromTheme(ui) with
{
    Heading1 = new Style(new Color(190, 140, 250), Color.Default, TextAttributes.Bold),
    Link = new Style(new Color(70, 200, 240), Color.Default, TextAttributes.Underline),
    InlineCode = new Style(new Color(240, 190, 90), Color.Default),
    Strong = new MarkdownInlineStyle { Attributes = TextAttributes.Bold },
    Syntax = CodeSyntaxTheme.FromTheme(ui) with
    {
        Keyword = new Style(new Color(190, 140, 250), Color.Default),
        Comment = new Style(new Color(125, 140, 155), Color.Default, TextAttributes.Italic)
    }
};
var reasoning = markdown with { Body = ui.Border, Heading1 = ui.Border };

builder.UseTheme(ui)
    .UseMarkdownTheme(markdown)
    .UseReasoningMarkdownTheme(reasoning);
```

`MarkdownTheme` supplies separate styles for Body, Heading1–Heading6, Link, InlineCode,
CodeBody (unhighlighted code), CodeBorder, CodeLanguage, QuoteText, QuoteMarker,
ListMarker, TaskChecked, TaskUnchecked, TableHeader, TableBody, TableBorder,
ThematicBreak, Image, and Html. `Syntax` supplies Text, Keyword, String, Number,
Identifier, Function, Type, Operator, Punctuation, and Comment. These are full `Style` values, including
foreground, background, and attributes.

Strong, Emphasis, and Strikethrough use `MarkdownInlineStyle`: null colors inherit
from the enclosing heading/link/text, explicit colors override it, and attributes
combine. Inner inline styling takes precedence. The conservative built-in highlighter
recognizes line comments for its supported C-style, Python, and shell modes, and
multiline block comments for C-style modes. Function declarations and calls use local
token context; types use built-in names, declaration context, and uppercase naming
conventions. Symbolic operators have their own style. Classification is lexical,
not compiler symbol resolution, and can misclassify unconventional names.

Without explicit palettes, responses derive defaults from the UI theme and reasoning
uses muted defaults. An explicit reasoning palette is used unchanged. Custom
transcript renderers read `context.MarkdownTheme`; event handlers and other callers
can use `registry.TranscriptRenderers.Services.ResolveMarkdownTheme(ui, reasoning)`.
`MarkdownMessageFactory.CreateAssistant` and `CreateReasoning` require that resolved
palette directly, including when preparing non-streaming messages.

Preparation, block caches, raw fallback, rendering, and scrollback all use the same
structural palette identity. Callers do not maintain a syntax-theme revision counter.
A `MarkdownView` renders the styles already in its prepared layout independently of
the surrounding UI theme; width and color-system validation remain enforced.

Palettes are fixed for a built registry. This is startup configuration, not a live
recoloring API: previously published native terminal history requires an explicit
new presentation and replay to change its colors.


### Catalog reasoning constraints

`AgentTuiModelCapabilities.SupportedReasoningEfforts` preserves raw catalog levels and `DefaultReasoningEffort` identifies the advertised default. The picker intersects known levels (`none`, `low`, `medium`, `high`, `xhigh`) with the implementation; unknown values remain metadata. Choosing server default leaves reasoning unspecified.

A catalog can supply `AgentTuiModelChoice.ProviderConfig` for model-specific construction constraints. Selection carries that payload into normal provider resolution, which snapshots it through the existing provider serialization contract while retaining the selected connection identity.
