namespace HPD.Agent.Adapters;

/// <summary>
/// Marks a partial class as a platform format converter.
/// The source generator emits the <c>ConvertNode(MdastNode)</c> switch dispatcher
/// using the formatting rules declared by the sibling attributes on the same class.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PlatformFormatConverterAttribute : Attribute { }

/// <summary>Format rule: markdown <c>**text**</c> → platform bold.</summary>
/// <param name="format">Format string where <c>{0}</c> is the inner content. Example: <c>"*{0}*"</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BoldAttribute(string format) : Attribute { public string Format => format; }

/// <summary>Format rule: markdown <c>_text_</c> or <c>*text*</c> → platform italic.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ItalicAttribute(string format) : Attribute { public string Format => format; }

/// <summary>Format rule: markdown <c>~~text~~</c> → platform strikethrough.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class StrikeAttribute(string format) : Attribute { public string Format => format; }

/// <summary>
/// Format rule: markdown <c>[text](url)</c> → platform link.
/// <c>{0}</c> = link text, <c>{1}</c> = URL.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LinkAttribute(string format) : Attribute { public string Format => format; }

/// <summary>Format rule: markdown <c>`code`</c> → platform inline code.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CodeAttribute(string format) : Attribute { public string Format => format; }

/// <summary>Format rule: markdown fenced code block → platform code block.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CodeBlockAttribute(string format) : Attribute { public string Format => format; }

/// <summary>Format rule: markdown <c>&gt; text</c> → platform blockquote.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BlockquoteAttribute(string format) : Attribute { public string Format => format; }

/// <summary>Format rule: unordered list item. <c>{0}</c> = item content.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ListItemAttribute(string format) : Attribute { public string Format => format; }

/// <summary>Format rule: ordered list item. <c>{0}</c> = item content, <c>{n}</c> = 1-based index.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class OrderedListItemAttribute(string format) : Attribute { public string Format => format; }
