using System.Globalization;
using System.Text;
using System.Xml;

namespace HPDOS.ToolHarnesses.Middleware;

public sealed class LanguageServerDiagnosticFormatter
{
    public string FormatMutationDiagnostics(
        string path,
        string source,
        IReadOnlyList<LanguageServerDiagnosticSet> diagnosticSets,
        LanguageServerFeedbackOptions options)
    {
        if (!options.Enabled || diagnosticSets.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        using var writer = CreateXmlWriter(builder);

        writer.WriteStartElement("language_server_diagnostics");
        writer.WriteAttributeString("path", path);
        writer.WriteAttributeString("source", source);
        writer.WriteAttributeString("status", "available");

        var emitted = WriteDiagnosticSets(writer, diagnosticSets, options, options.MaxFeedbackCharacters);
        if (emitted.Truncated)
        {
            writer.WriteStartElement("truncated");
            writer.WriteString("Additional diagnostics were omitted because feedback limits were reached.");
            writer.WriteEndElement();
        }

        if (emitted.Count > 0)
        {
            writer.WriteStartElement("repair_hint");
            writer.WriteString("The edit succeeded, but language server diagnostics reported errors in this file.");
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.Flush();

        return builder.ToString();
    }

    public string FormatIterationFeedback(
        IReadOnlyList<LanguageServerDiagnosticSet> diagnosticSets,
        LanguageServerFeedbackOptions options)
    {
        if (!options.Enabled || diagnosticSets.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        using var writer = CreateXmlWriter(builder);

        writer.WriteStartElement("language_server_feedback");
        var emitted = WriteDiagnosticSets(writer, diagnosticSets, options, options.MaxFeedbackCharacters);
        if (emitted.Truncated)
        {
            writer.WriteStartElement("truncated");
            writer.WriteString("Additional diagnostics were omitted because feedback limits were reached.");
            writer.WriteEndElement();
        }

        if (emitted.Count > 0)
        {
            writer.WriteStartElement("hint");
            writer.WriteString("Fix the reported diagnostics before continuing unrelated edits.");
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.Flush();

        return builder.ToString();
    }

    public string FormatUnavailable(string path, string message)
    {
        var builder = new StringBuilder();
        using var writer = CreateXmlWriter(builder);

        writer.WriteStartElement("language_server_diagnostics");
        writer.WriteAttributeString("path", path);
        writer.WriteAttributeString("status", "unavailable");
        writer.WriteStartElement("message");
        writer.WriteString(message);
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.Flush();

        return builder.ToString();
    }

    private static (int Count, bool Truncated) WriteDiagnosticSets(
        XmlWriter writer,
        IReadOnlyList<LanguageServerDiagnosticSet> diagnosticSets,
        LanguageServerFeedbackOptions options,
        int maxCharacters)
    {
        var emitted = 0;
        var currentCharacters = 0;

        foreach (var set in diagnosticSets)
        {
            var selected = SelectDiagnostics(set.Diagnostics, options).ToArray();
            if (selected.Length == 0)
                continue;

            writer.WriteStartElement("diagnostics");
            writer.WriteAttributeString("path", set.Path);
            writer.WriteAttributeString("server", set.ServerId);
            writer.WriteAttributeString("source", FormatSource(set.Source));
            writer.WriteAttributeString("errors", selected.Count(d => d.Severity == LanguageServerDiagnosticSeverity.Error).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("warnings", selected.Count(d => d.Severity == LanguageServerDiagnosticSeverity.Warning).ToString(CultureInfo.InvariantCulture));

            foreach (var diagnostic in selected)
            {
                currentCharacters += diagnostic.Message.Length + (diagnostic.Code?.Length ?? 0) + 64;
                if (currentCharacters > maxCharacters)
                {
                    writer.WriteEndElement();
                    return (emitted, true);
                }

                writer.WriteStartElement("diagnostic");
                writer.WriteAttributeString("severity", FormatSeverity(diagnostic.Severity));
                writer.WriteAttributeString("line", (diagnostic.Line + 1).ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("character", (diagnostic.Character + 1).ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrWhiteSpace(diagnostic.Code))
                    writer.WriteAttributeString("code", diagnostic.Code);
                writer.WriteString(diagnostic.Message);
                writer.WriteEndElement();
                emitted++;
            }

            writer.WriteEndElement();
        }

        return (emitted, false);
    }

    private static IEnumerable<LanguageServerDiagnostic> SelectDiagnostics(
        IReadOnlyList<LanguageServerDiagnostic> diagnostics,
        LanguageServerFeedbackOptions options)
    {
        var errors = 0;
        var warnings = 0;

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity == LanguageServerDiagnosticSeverity.Error)
            {
                if (!options.ShowErrors || errors >= options.MaxErrorsPerFile)
                    continue;

                errors++;
                yield return diagnostic;
                continue;
            }

            if (diagnostic.Severity == LanguageServerDiagnosticSeverity.Warning)
            {
                if (!options.ShowWarnings || warnings >= options.MaxWarningsPerFile)
                    continue;

                warnings++;
                yield return diagnostic;
                continue;
            }

            if (options.ShowInformation)
                yield return diagnostic;
        }
    }

    private static XmlWriter CreateXmlWriter(StringBuilder builder)
        => XmlWriter.Create(builder, new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            ConformanceLevel = ConformanceLevel.Fragment,
            Indent = false
        });

    private static string FormatSeverity(LanguageServerDiagnosticSeverity severity)
        => severity switch
        {
            LanguageServerDiagnosticSeverity.Error => "error",
            LanguageServerDiagnosticSeverity.Warning => "warning",
            LanguageServerDiagnosticSeverity.Information => "information",
            LanguageServerDiagnosticSeverity.Hint => "hint",
            _ => "unknown"
        };

    private static string FormatSource(LanguageServerDiagnosticSource source)
        => source switch
        {
            LanguageServerDiagnosticSource.Publish => "publish",
            LanguageServerDiagnosticSource.DocumentPull => "document_pull",
            LanguageServerDiagnosticSource.WorkspacePull => "workspace_pull",
            _ => "unknown"
        };
}
