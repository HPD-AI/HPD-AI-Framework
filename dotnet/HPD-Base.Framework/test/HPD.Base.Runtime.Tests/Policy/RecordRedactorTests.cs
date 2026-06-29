using System.Text.Json;
using HPD.Base.Policy;
using HPD.Base.Records;
using HPD.Base.Runtime.DependencyInjection;
using HPD.Base.Runtime.Policy;
using HPD.Base.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Tests.Policy;

public sealed class RecordRedactorTests
{
    [Fact]
    public void PublicRedactionRemovesHiddenSystemAndWriteOnlyFields()
    {
        using var provider = Provider();
        var redactor = provider.GetRequiredService<IBaseRecordRedactor>();

        var record = redactor.RedactRecord(
            Record(),
            Collection(),
            Allow(),
            VisibilityLevel.Public);

        Assert.Equal(["title"], record.Payload.Fields!.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.True(record.Policy!.Redacted);
        Assert.Contains("secret", record.Policy.OmittedFields!);
        Assert.Contains("systemId", record.Policy.OmittedFields!);
        Assert.Contains("password", record.Policy.OmittedFields!);
    }

    [Fact]
    public void ReadMaskIncludeOnlyLimitsReturnedFields()
    {
        using var provider = Provider();
        var redactor = provider.GetRequiredService<IBaseRecordRedactor>();

        var record = redactor.RedactRecord(
            Record(),
            Collection(),
            Allow(new FieldMask { Mode = FieldMaskMode.IncludeOnly, Include = ["title"] }),
            VisibilityLevel.Admin);

        Assert.Equal(["title"], record.Payload.Fields!.Keys.ToArray());
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddHPDBaseRuntime();
        return services.BuildServiceProvider();
    }

    private static BasePolicyEvaluation Allow(FieldMask? mask = null) => new()
    {
        Decision = new PolicyDecision
        {
            Effect = PolicyEffect.Allow,
            Outcome = PolicyOutcome.Allowed
        },
        EffectiveReadMask = mask
    };

    private static CollectionDefinition Collection() => new()
    {
        Id = "items",
        Name = "items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve,
        Fields =
        [
            new FieldDefinition { Id = "title", Name = "title", Type = BaseFieldTypes.String },
            new FieldDefinition { Id = "secret", Name = "secret", Type = BaseFieldTypes.String, Hidden = true },
            new FieldDefinition { Id = "systemId", Name = "systemId", Type = BaseFieldTypes.String, System = true },
            new FieldDefinition
            {
                Id = "password",
                Name = "password",
                Type = BaseFieldTypes.String,
                Visibility = new FieldVisibilityAnnotation { WriteOnly = true }
            }
        ]
    };

    private static RecordEnvelope Record() => new()
    {
        CollectionId = "items",
        Id = new RecordId("rec_1"),
        Payload = new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = new Dictionary<string, JsonElement>
            {
                ["title"] = Json("hello"),
                ["secret"] = Json("shh"),
                ["systemId"] = Json("sys"),
                ["password"] = Json("pw")
            }
        },
        Metadata = new RecordMetadata()
    };

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse($"\"{value}\"");
        return document.RootElement.Clone();
    }
}
