namespace HPD.Agent.Bots;

/// <summary>
/// Marks a partial class as a platform format converter.
/// This internal pre-1.0 marker is retained for planned source generation. Current
/// platform converters implement their formatting rules by hand.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class PlatformFormatConverterAttribute : Attribute { }

/// <summary>Format rule: markdown <c>**text**</c> → platform bold.</summary>
/// <param name="format">Format string where <c>{0}</c> is the inner content. Example: <c>"*{0}*"</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class BoldAttribute(string format) : Attribute { public string Format => format; }

/// <summary>Format rule: markdown <c>_text_</c> or <c>*text*</c> → platform italic.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class ItalicAttribute(string format) : Attribute { public string Format => format; }

/// <summary>Format rule: markdown <c>~~text~~</c> → platform strikethrough.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class StrikeAttribute(string format) : Attribute { public string Format => format; }

/// <summary>
/// Format rule: markdown <c>[text](url)</c> → platform link.
/// <c>{0}</c> = link text, <c>{1}</c> = URL.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class LinkAttribute(string format) : Attribute { public string Format => format; }

/// <summary>Format rule: markdown <c>`code`</c> → platform inline code.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class CodeAttribute(string format) : Attribute { public string Format => format; }

/// <summary>Format rule: markdown fenced code block → platform code block.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class CodeBlockAttribute(string format) : Attribute { public string Format => format; }

/// <summary>Format rule: markdown <c>&gt; text</c> → platform blockquote.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class BlockquoteAttribute(string format) : Attribute { public string Format => format; }

/// <summary>Format rule: unordered list item. <c>{0}</c> = item content.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class ListItemAttribute(string format) : Attribute { public string Format => format; }

/// <summary>Format rule: ordered list item. <c>{0}</c> = item content, <c>{n}</c> = 1-based index.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class OrderedListItemAttribute(string format) : Attribute { public string Format => format; }
