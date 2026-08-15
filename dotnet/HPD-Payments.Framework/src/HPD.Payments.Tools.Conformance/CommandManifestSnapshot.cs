using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Payments.Tools.Conformance;

/// <summary>Loads exact command definitions and derives stable receipt bindings.</summary>
internal sealed class CommandManifestSnapshot
{
    private readonly IReadOnlyDictionary<string, ProofCommandDefinition> _commands;
    internal string SchemaVersion { get; }
    internal int Revision { get; }
    internal string ProductRoot { get; }
    internal string ManifestDigest { get; }
    internal IReadOnlyDictionary<string, ProofCommandDefinition> Commands => _commands;

    private CommandManifestSnapshot(string schemaVersion, int revision, string productRoot, string manifestDigest,
        IReadOnlyDictionary<string, ProofCommandDefinition> commands) =>
        (SchemaVersion, Revision, ProductRoot, ManifestDigest, _commands) =
            (schemaVersion, revision, productRoot, manifestDigest, commands);

    internal static CommandManifestSnapshot Load(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 2 or > 4_194_304) throw new ArgumentOutOfRangeException(nameof(bytes));
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            { MaxDepth = 16, CommentHandling = JsonCommentHandling.Disallow });
        var root = document.RootElement;
        var schema = RequiredString(root, "schemaVersion");
        if (schema != "hpd.payments.commands.v1") throw new InvalidDataException("Command manifest schema is unsupported.");
        var revision = root.GetProperty("revision").GetInt32();
        if (revision < 1) throw new InvalidDataException("Command manifest revision is invalid.");
        var productRoot = RequiredString(root, "productRoot");
        var manifestDigest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes.Span));
        var commands = new Dictionary<string, ProofCommandDefinition>(StringComparer.Ordinal);
        foreach (var item in root.GetProperty("commands").EnumerateArray())
        {
            var definition = new ProofCommandDefinition(RequiredString(item, "id"), item.GetProperty("enabled").GetBoolean(),
                RequiredString(item, "cwd"), RequiredStrings(item, "argv"), RequiredStrings(item, "prerequisites"),
                item.GetProperty("timeoutSeconds").GetInt32(), RequiredStrings(item, "outputs"), RequiredStrings(item, "cleanup"),
                item.GetProperty("acceptedExitCodes").EnumerateArray().Select(static x => x.GetInt32()).ToArray(),
                RequiredString(item, "proofClass"), schema, revision, productRoot, manifestDigest);
            if (definition.TimeoutSeconds is < 1 or > 86_400 || definition.Argv.Count == 0 ||
                definition.AcceptedExitCodes.Count == 0 ||
                definition.Prerequisites.Distinct(StringComparer.Ordinal).Count() != definition.Prerequisites.Count ||
                !commands.TryAdd(definition.Id, definition))
                throw new InvalidDataException("Command definition is invalid or duplicated.");
            ValidatePaths(definition);
        }
        if (commands.Count == 0) throw new InvalidDataException("Command manifest is empty.");
        ValidatePrerequisites(commands);
        return new(schema, revision, productRoot, manifestDigest, commands);
    }

    internal ProofCommandDefinition RequireEnabled(string id)
    {
        if (!_commands.TryGetValue(id, out var command)) throw new KeyNotFoundException(id);
        if (!command.Enabled) throw new InvalidOperationException($"Command {id} is not admitted.");
        return command;
    }

    internal void RequireProductRoot(string expectedRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRoot);
        var expected = Path.GetFullPath(expectedRoot).TrimEnd(Path.DirectorySeparatorChar);
        var declared = Path.GetFullPath(ProductRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (!StringComparer.Ordinal.Equals(expected, declared) || !Directory.Exists(declared) ||
            new DirectoryInfo(declared).LinkTarget is not null)
            throw new InvalidDataException("Command manifest product root is mismatched, absent, or linked.");
    }

    private static string RequiredString(JsonElement element, string property)
    {
        var value = element.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16_384)
            throw new InvalidDataException($"Command property {property} is absent or over-bound.");
        return value;
    }

    private static string[] RequiredStrings(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(static value =>
            value.GetString() is { Length: > 0 and <= 16_384 } text ? text :
                throw new InvalidDataException("Command array contains an invalid string.")).ToArray();

    private static void ValidatePaths(ProofCommandDefinition definition)
    {
        if (Path.IsPathRooted(definition.WorkingDirectory) || Escapes(definition.WorkingDirectory))
            throw new InvalidDataException("Command working directory must remain relative to the product root.");
        if (!Path.IsPathRooted(definition.Argv[0]) && Escapes(definition.Argv[0]))
            throw new InvalidDataException("A relative command executable must remain under the product root.");
        foreach (var pattern in definition.Outputs.Concat(definition.Cleanup))
        {
            if (Path.IsPathRooted(pattern) || Escapes(pattern))
                throw new InvalidDataException("Command output and cleanup paths must remain under the product root.");
        }

        static bool Escapes(string value) => value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(static segment => segment == "..");
    }

    private static void ValidatePrerequisites(IReadOnlyDictionary<string, ProofCommandDefinition> commands)
    {
        var state = new Dictionary<string, byte>(StringComparer.Ordinal);
        foreach (var id in commands.Keys) Visit(id);
        return;

        void Visit(string id)
        {
            if (state.TryGetValue(id, out var seen))
            {
                if (seen == 1) throw new InvalidDataException("Command prerequisite graph contains a cycle.");
                return;
            }
            state[id] = 1;
            foreach (var prerequisite in commands[id].Prerequisites)
            {
                if (commands.ContainsKey(prerequisite)) Visit(prerequisite);
            }
            state[id] = 2;
        }
    }
}

internal sealed record ProofCommandDefinition(string Id, bool Enabled, string WorkingDirectory, IReadOnlyList<string> Argv,
    IReadOnlyList<string> Prerequisites, int TimeoutSeconds, IReadOnlyList<string> Outputs, IReadOnlyList<string> Cleanup,
    IReadOnlyList<int> AcceptedExitCodes, string ProofClass, string SchemaVersion, int ManifestRevision, string ProductRoot,
    string ManifestDigest)
{
    internal string Binding => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        ProofCanonical.Join(SchemaVersion, ManifestRevision.ToString(System.Globalization.CultureInfo.InvariantCulture), ManifestDigest,
            ProductRoot, Id, Enabled ? "true" : "false", WorkingDirectory, ProofCanonical.Join(Argv.ToArray()),
            ProofCanonical.Join(Prerequisites.ToArray()), TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ProofCanonical.Join(Outputs.ToArray()), ProofCanonical.Join(Cleanup.ToArray()),
            ProofCanonical.Join(AcceptedExitCodes.Select(static x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray()),
            ProofClass))));
}
