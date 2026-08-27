using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Agent.ModelsDev;

public static class ModelsDevValuationJson
{
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(ModelsDevJsonContext.Default.Options)
        {
            TypeInfoResolver = ModelsDevJsonContext.Default.WithAddedModifier(ConfigurePolymorphism)
        };
        return options;
    }

    private static void ConfigurePolymorphism(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type == typeof(ProviderUsageValuationProvenance))
        {
            typeInfo.PolymorphismOptions!.DerivedTypes.Add(
                new JsonDerivedType(typeof(ModelsDevValuationProvenance), "models_dev"));
        }
        else if (typeInfo.Type == typeof(ProviderUsageValuationDetails))
        {
            typeInfo.PolymorphismOptions!.DerivedTypes.Add(
                new JsonDerivedType(typeof(ModelsDevValuationDetails), "models_dev"));
        }
        else if (typeInfo.Type == typeof(ProviderValuationComponentProvenance))
        {
            typeInfo.PolymorphismOptions!.DerivedTypes.Add(
                new JsonDerivedType(typeof(ModelsDevRateSelection), "models_dev"));
        }
    }
}
