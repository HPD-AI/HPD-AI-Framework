# Azure AI Inference Provider - Deprecation Notice

**Date**: January 9, 2026
**Status**: **DEPRECATED** (Maintained for backward compatibility)

## Summary

The `HPD-Agent.Providers.AzureAIInference` provider has been marked as **obsolete** and will be deprecated in a future version.

## Reason for Deprecation

Microsoft is transitioning from `Azure.AI.Inference` to a new architecture based on `Azure.AI.Projects`:

### Old Stack (This Provider - Being Phased Out)
```xml
<PackageReference Include="Azure.AI.Inference" Version="1.0.0-beta.5" />
<PackageReference Include="Microsoft.Extensions.AI.AzureAIInference" Version="9.8.0-preview.1.25412.6" />
```

**Issues:**
- API key authentication only
- Does NOT support Azure AI Foundry endpoints (`*.services.ai.azure.com/api/projects/*`)
- Not compatible with OAuth/Entra ID authentication
- Being superseded by Microsoft's official stack

### New Stack (Microsoft's Current Approach)
```xml
<PackageReference Include="Azure.AI.Projects" />
<PackageReference Include="Azure.AI.Projects.OpenAI" />
<PackageReference Include="Microsoft.Extensions.AI" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" />
<PackageReference Include="OpenAI" />
```

**Benefits:**
-  Supports OAuth/Entra ID authentication
-  Works with Azure AI Foundry/Projects endpoints
-  Modern AIProjectClient architecture
-  Actively maintained by Microsoft
-  Full integration with OpenAI SDK

## What This Means for Users

### If You're Using Azure AI Foundry
**Your endpoint looks like:** `https://*.services.ai.azure.com/api/projects/*`

**This provider will NOT work** - Azure AI Foundry requires OAuth authentication.

 **Use the Azure OpenAI provider instead:**
```csharp
var agent = await new AgentBuilder()
    .WithAzureOpenAI(
        endpoint: "https://your-resource.services.ai.azure.com",
        model: "gpt-4",
        apiKey: "your-key") // Or use DefaultAzureCredential for OAuth
    .Build();
```

### If You're Using Traditional Azure AI Inference Endpoints
**Your endpoint looks like:** `https://*.inference.ai.azure.com`

 **This provider still works** but is deprecated.

**Migration recommended:** Plan to migrate to Azure OpenAI provider or Azure AI Projects-based solution.

## Timeline

- **Current**: Provider marked as `[Obsolete]` with compiler warnings
- **Future**: Provider will be moved to a separate "legacy" package
- **End of Life**: To be announced - significant advance notice will be given

## Files Modified

1. **AzureAIInferenceProvider.cs** - Added `[Obsolete]` attribute with deprecation message
2. **AgentBuilderExtensions.cs** - Added `[Obsolete]` attribute to `WithAzureAIInference()`
3. **README.md** - Added prominent deprecation notice at top with migration guide
4. **build_reference.sh** - Added Azure.AI.Projects SDK to reference repositories

## Compiler Warnings

When using this provider, you'll see:
```
warning CS0618: 'AzureAIInferenceProvider' is obsolete: 'This provider uses Azure.AI.Inference which is being superseded by Azure.AI.Projects. Use Azure OpenAI provider for Azure AI Foundry endpoints. This will be deprecated in a future version.'
```

This is **intentional** and serves as a reminder to plan migration.

## References

### Deprecated Packages
- [Azure.AI.Inference](https://www.nuget.org/packages/Azure.AI.Inference) - v1.0.0-beta.5
- [Microsoft.Extensions.AI.AzureAIInference](https://www.nuget.org/packages/Microsoft.Extensions.AI.AzureAIInference) - v9.8.0-preview.1.25412.6

### Modern Stack
- [Azure.AI.Projects](https://www.nuget.org/packages/Azure.AI.Projects)
- [Azure.AI.Projects.OpenAI](https://www.nuget.org/packages/Azure.AI.Projects.OpenAI)
- [Microsoft.Extensions.AI.OpenAI](https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI)

### Reference Implementations
- [Microsoft Agents Framework - Azure AI](https://github.com/microsoft/agents/tree/main/dotnet/src/Microsoft.Agents.AI.AzureAI)
- [Azure SDK for .NET - AI Projects](https://github.com/Azure/azure-sdk-for-net/tree/main/sdk/ai/Azure.AI.Projects)

## Questions?

For questions or concerns about this deprecation:
1. Review the [README.md](./README.md) migration guide
2. Check the Azure OpenAI provider documentation
3. Open an issue in the HPD-Agent repository

---

**Last Updated**: January 9, 2026
