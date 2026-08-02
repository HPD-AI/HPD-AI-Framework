using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Gateway.Abstractions;

namespace HPD.Gateway.Abstractions.Serialization;

public static class GatewayJson
{
    public const int MaximumDocumentBytes = 4 * 1024 * 1024;
    public const int MaximumDepth = 64;
    public const int MaximumTokens = 500_000;
    public const int MaximumPropertiesPerObject = 256;
    public const int MaximumItemsPerArray = 10_000;
    public const int MaximumStringUtf8Bytes = 16 * 1024;

    public static JsonSerializerOptions StrictOptions { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(GatewayJsonSerializerContext.Default.Options)
        {
            TypeInfoResolver = GatewayJsonSerializerContext.Default,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = MaximumDepth
        };
        options.MakeReadOnly();
        return options;
    }
}

public sealed record GatewayConfigurationParseResult
{
    public GatewayConfiguration? Configuration { get; init; }

    public required ImmutableArray<GatewayValidationError> Errors { get; init; }

    public bool IsParsed => Configuration is not null && Errors.IsEmpty;
}

public sealed record GatewayPortableDocumentResult
{
    public GatewayConfiguration? Configuration { get; init; }

    public GatewayCanonicalDocument? CanonicalDocument { get; init; }

    public required ImmutableArray<GatewayValidationError> Errors { get; init; }

    public bool IsStructurallyValid => Configuration is not null && CanonicalDocument is not null && Errors.IsEmpty;
}

public static class GatewayPortableDocumentReader
{
    public static GatewayPortableDocumentResult Read(ReadOnlySpan<byte> utf8Json)
    {
        var parsed = GatewayConfigurationParser.Parse(utf8Json);
        if (!parsed.IsParsed)
        {
            return new GatewayPortableDocumentResult { Errors = parsed.Errors };
        }

        var canonical = GatewayConfigurationCanonicalizer.TryCanonicalize(parsed.Configuration);
        return canonical.IsCanonicalized
            ? new GatewayPortableDocumentResult
            {
                Configuration = parsed.Configuration,
                CanonicalDocument = canonical.Document,
                Errors = []
            }
            : new GatewayPortableDocumentResult { Errors = canonical.Errors };
    }
}

public static class GatewayConfigurationParser
{
    public static GatewayConfigurationParseResult Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty || utf8Json.Length > GatewayJson.MaximumDocumentBytes)
        {
            return Failure(GatewayValidationErrorCode.BoundExceeded, "$", "Configuration document is empty or exceeds its byte bound.");
        }

        try
        {
            ValidateLexicalBounds(utf8Json);
            var configuration = JsonSerializer.Deserialize(utf8Json, GatewayJsonSerializerContext.Default.GatewayConfiguration);
            if (configuration is null)
            {
                return Failure(GatewayValidationErrorCode.MissingRequiredValue, "$", "Configuration document produced no value.");
            }

            return new GatewayConfigurationParseResult
            {
                Configuration = configuration,
                Errors = []
            };
        }
        catch (JsonException)
        {
            return Failure(GatewayValidationErrorCode.InvalidValue, "$", "Configuration JSON is malformed, unknown, or outside the supported wire contract.");
        }
        catch (NotSupportedException)
        {
            return Failure(GatewayValidationErrorCode.InvalidValue, "$", "Configuration contains an unsupported wire type.");
        }
    }

    private static void ValidateLexicalBounds(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = GatewayJson.MaximumDepth
        });

        Span<bool> arrayFrames = stackalloc bool[GatewayJson.MaximumDepth + 1];
        Span<int> memberCounts = stackalloc int[GatewayJson.MaximumDepth + 1];
        var frameDepth = 0;
        var tokens = 0;

        while (reader.Read())
        {
            if (++tokens > GatewayJson.MaximumTokens)
            {
                throw new JsonException("Token bound exceeded.");
            }

            if (frameDepth > 0 && arrayFrames[frameDepth - 1] && reader.TokenType != JsonTokenType.EndArray)
            {
                if (++memberCounts[frameDepth - 1] > GatewayJson.MaximumItemsPerArray)
                {
                    throw new JsonException("Array item bound exceeded.");
                }
            }

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                if (frameDepth == 0 || ++memberCounts[frameDepth - 1] > GatewayJson.MaximumPropertiesPerObject)
                {
                    throw new JsonException("Object property bound exceeded.");
                }
            }

            var valueLength = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
            if ((reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName) &&
                valueLength > GatewayJson.MaximumStringUtf8Bytes)
            {
                throw new JsonException("String byte bound exceeded.");
            }

            switch (reader.TokenType)
            {
                case JsonTokenType.StartArray:
                    arrayFrames[frameDepth] = true;
                    memberCounts[frameDepth] = 0;
                    frameDepth++;
                    break;
                case JsonTokenType.StartObject:
                    arrayFrames[frameDepth] = false;
                    memberCounts[frameDepth] = 0;
                    frameDepth++;
                    break;
                case JsonTokenType.EndArray:
                case JsonTokenType.EndObject:
                    frameDepth--;
                    break;
            }
        }
    }

    private static GatewayConfigurationParseResult Failure(GatewayValidationErrorCode code, string path, string message) =>
        new()
        {
            Errors = [new GatewayValidationError(code, path, message)]
        };
}
