namespace HPD.Base.Tests.Volatile.TestDoubles;

internal static class VolatileTestData
{
    public static CollectionDefinition Collection(string id = "items") => new()
    {
        Id = id,
        Name = id,
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve,
        Operations = new CollectionOperationMatrix
        {
            List = true,
            Get = true,
            Create = true,
            Patch = true,
            Replace = true,
            Delete = true,
            Upsert = true
        }
    };

    public static OperationContext Operation(BaseOperationKind operation, string collectionId = "items") => new()
    {
        Operation = operation,
        CollectionId = collectionId,
        Now = DateTimeOffset.UnixEpoch
    };

    public static PrincipalContext Principal => new()
    {
        AuthenticationState = PrincipalAuthenticationState.Anonymous
    };

    public static RecordPayload Payload(params (string Name, string Value)[] fields)
    {
        var json = "{" + string.Join(",", fields.Select(field => $"\"{field.Name}\":\"{field.Value}\"")) + "}";
        using var document = JsonDocument.Parse(json);
        return new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = document.RootElement.Clone()
        };
    }

    public static RecordPayload Patch(string name, string value)
    {
        using var document = JsonDocument.Parse($"{{\"{name}\":\"{value}\"}}");
        return new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = new Dictionary<string, JsonElement>
            {
                [name] = document.RootElement.GetProperty(name).Clone()
            }
        };
    }
}
