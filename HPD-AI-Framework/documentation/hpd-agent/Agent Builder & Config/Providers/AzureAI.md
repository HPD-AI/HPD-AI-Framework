# Azure AI Provider

**Provider Key:** `azure-ai`

## Overview

The Azure AI provider enables HPD-Agent to use Microsoft Azure OpenAI models and Azure AI Foundry deployments. Built on Microsoft's modern Azure AI stack, it provides enterprise-grade security, OAuth authentication, and seamless integration with Azure's AI infrastructure.

**Key Features:**
- Azure OpenAI models
- Azure AI Foundry/Projects support
- OAuth/Entra ID authentication (DefaultAzureCredential)
- API key authentication (traditional Azure OpenAI)
- Streaming support for real-time responses
- Function/tool calling capabilities
- Vision support (GPT-4 Vision models)
- Structured JSON output with schema validation
- Deterministic generation with seed support

**For detailed API documentation, see:**
- [**AzureAIProviderConfig API Reference**](#azureaiproviderconfig-api-reference) - Complete property listing

## Quick Start

### Minimal Example (OAuth Authentication)

```csharp
using HPD.Agent;
using HPD.Agent.Providers.AzureAI;

// Authenticate via Azure CLI
// Run: az login

var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-resource.openai.azure.com",
        model: "gpt-4")
    .BuildAsync();

var response = await agent.RunAsync("What is the capital of France?");
Console.WriteLine(response);
```

### Minimal Example (API Key Authentication)

```csharp
var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-resource.openai.azure.com",
        model: "gpt-4",
        apiKey: "your-api-key")
    .BuildAsync();
```

## Installation

```bash
dotnet add package HPD-Agent.Providers.AzureAI
```

**Dependencies:**
- `Azure.AI.Projects` - Azure AI Projects SDK
- `Azure.AI.OpenAI` - Azure OpenAI client library
- `Azure.Identity` - DefaultAzureCredential for OAuth
- `Microsoft.Extensions.AI.OpenAI` - IChatClient integration

## Configuration

### Configuration Patterns

The Azure AI provider supports all three configuration patterns. Choose the one that best fits your needs.

#### 1. Builder Pattern (Fluent API)

Best for: Simple configurations and quick prototyping.

```csharp
var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-resource.openai.azure.com",
        model: "gpt-4",
        configure: opts =>
        {
            opts.MaxTokens = 4096;
            opts.Temperature = 0.7f;
            opts.TopP = 0.9f;
            opts.UseDefaultAzureCredential = true; // OAuth authentication
        })
    .BuildAsync();
```

#### 2. JSON Config File

Best for: A single `agent.json` that fully describes the agent — no code required for configuration.

**`azure-config.json`:**
```json
{
    "name": "AzureAIAgent",
    "systemInstructions": "You are a helpful assistant.",
    "provider": {
        "providerKey": "azure-ai",
        "modelName": "gpt-4o",
        "endpoint": "https://your-project.services.ai.azure.com",
        "providerOptionsJson": "{\"maxTokens\":4096,\"temperature\":0.7,\"topP\":0.95}"
    }
}
```

```csharp
var agent = await AgentConfig.BuildFromFileAsync("azure-config.json");
```

The `providerOptionsJson` value is a JSON string containing Azure AI-specific options. All keys are **camelCase** — see the [ProviderOptionsJson reference](#provideroptionsjson-reference) below for the full list.

#### 3. C# Config Object

Best for: Reusable config shared across multiple builder instances.

```csharp
var config = new AgentConfig
{
    Name = "AzureAIAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "azure-ai",
        ModelName = "gpt-4o",
        Endpoint = "https://your-project.services.ai.azure.com"
    }
};

config.Provider.SetTypedProviderConfig(new AzureAIProviderConfig
{
    MaxTokens = 4096,
    Temperature = 0.7f,
    TopP = 0.95f
});

var agent = await config.BuildAsync();
```

#### 4. Builder + Config Pattern (Recommended)

Best for: Production deployments with reusable configuration and runtime customization.

```csharp
// Define base config once
var config = new AgentConfig
{
    Name = "AzureAIAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "azure-ai",
        ModelName = "gpt-4",
        Endpoint = "https://my-resource.openai.azure.com"
    }
};

var azureOpts = new AzureAIProviderConfig
{
    MaxTokens = 4096,
    Temperature = 0.7f,
    UseDefaultAzureCredential = true
};
config.Provider.SetTypedProviderConfig(azureOpts);

// Reuse with different runtime customizations
var agent1 = await new AgentBuilder(config)
    .WithServiceProvider(services)
    .WithToolkit<MathToolkit>()
    .BuildAsync();

var agent2 = await new AgentBuilder(config)
    .WithServiceProvider(services)
    .WithToolkit<FileToolkit>()
    .BuildAsync();
```

### Provider-Specific Options

The `AzureAIProviderConfig` class provides comprehensive configuration options organized by category:

#### Core Parameters

```csharp
configure: opts =>
{
    // Maximum tokens to generate (default: 4096)
    opts.MaxTokens = 4096;

    // Sampling temperature (0.0-2.0, default: 1.0)
    opts.Temperature = 0.7f;

    // Top-P nucleus sampling (0.0-1.0)
    opts.TopP = 0.9f;

    // Stop sequences
    opts.StopSequences = new List<string> { "STOP", "END" };
}
```

#### Sampling & Penalties

```csharp
configure: opts =>
{
    // Frequency penalty (-2.0 to 2.0)
    // Reduces repetition of tokens based on their frequency
    opts.FrequencyPenalty = 0.5f;

    // Presence penalty (-2.0 to 2.0)
    // Encourages new topics by penalizing any repeated token
    opts.PresencePenalty = 0.5f;
}
```

#### Deterministic Generation

```csharp
configure: opts =>
{
    // Seed for reproducible outputs
    opts.Seed = 12345;

    // Use with temperature = 0 for maximum determinism
    opts.Temperature = 0.0f;
}
```

#### Structured JSON Output

```csharp
configure: opts =>
{
    // Option 1: Loose JSON mode
    opts.ResponseFormat = "json_object";

    // Option 2: Strict schema validation
    opts.ResponseFormat = "json_schema";
    opts.JsonSchemaName = "UserResponse";
    opts.JsonSchema = @"{
        ""type"": ""object"",
        ""properties"": {
            ""name"": { ""type"": ""string"" },
            ""age"": { ""type"": ""number"" }
        },
        ""required"": [""name"", ""age""]
    }";
    opts.JsonSchemaIsStrict = true;
}
```

#### Tool/Function Calling

```csharp
configure: opts =>
{
    // Tool choice behavior: "auto" (default), "none", "required"
    opts.ToolChoice = "auto";
}
```

#### Azure-Specific Configuration

```csharp
configure: opts =>
{
    // Use OAuth/Entra ID authentication (recommended for production)
    opts.UseDefaultAzureCredential = true;

    // Azure AI Project ID (optional - extracted from endpoint if not provided)
    opts.ProjectId = "my-project";
}
```

## Authentication

Azure AI supports two authentication methods with automatic fallback.

### Authentication Priority Order

1. **OAuth/Entra ID** (if `UseDefaultAzureCredential = true` or no API key provided)
2. **API Key** (if provided via config, environment, or appsettings)

### Method 1: OAuth/Entra ID 

OAuth provides enhanced security, automatic token rotation, and enterprise identity integration.

#### Prerequisites

Install Azure CLI:
```bash
# macOS
brew install azure-cli

# Windows
winget install Microsoft.AzureCLI

# Linux
curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash
```

#### Local Development (Azure CLI)

```bash
# Login to Azure
az login

# (Optional) Set default subscription
az account set --subscription "your-subscription-id"
```

```csharp
// Automatically uses Azure CLI credentials
var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-resource.openai.azure.com",
        model: "gpt-4",
        configure: opts => opts.UseDefaultAzureCredential = true)
    .BuildAsync();
```

#### Production (Managed Identity)

When running on Azure infrastructure (App Service, Functions, AKS, etc.):

```csharp
// Automatically uses Managed Identity - no credentials needed
var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-resource.openai.azure.com",
        model: "gpt-4",
        configure: opts => opts.UseDefaultAzureCredential = true)
    .BuildAsync();
```

#### DefaultAzureCredential Chain

`DefaultAzureCredential` tries authentication methods in this order:
1. **Environment variables** - Service principal credentials
2. **Workload Identity** - Kubernetes workload identity
3. **Managed Identity** - Azure-hosted applications
4. **Visual Studio** - Local development (Windows)
5. **VS Code** - Local development
6. **Azure CLI** - Local development (cross-platform)
7. **Azure PowerShell** - Local development
8. **Interactive Browser** - Fallback for local development

### Method 2: API Key Authentication

#### Environment Variables

```bash
export AZURE_AI_ENDPOINT="https://my-resource.openai.azure.com"
export AZURE_AI_API_KEY="your-api-key"
```

```csharp
// Automatically uses environment variables
var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: Environment.GetEnvironmentVariable("AZURE_AI_ENDPOINT"),
        model: "gpt-4")
    .BuildAsync();
```

#### Configuration File (appsettings.json)

```json
{
    "AzureAI": {
        "Endpoint": "https://my-resource.openai.azure.com",
        "ApiKey": "your-api-key"
    }
}
```

```csharp
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .BuildAsync();

var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: config["AzureAI:Endpoint"],
        model: "gpt-4",
        apiKey: config["AzureAI:ApiKey"])
    .BuildAsync();
```

#### Explicit API Key

```csharp
var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-resource.openai.azure.com",
        model: "gpt-4",
        apiKey: "your-api-key")
    .BuildAsync();
```

 **Security Warning:** Never hardcode API keys in source code. Use environment variables, Azure Key Vault, or Managed Identity instead.

## Endpoint Types

The Azure AI provider supports two types of endpoints with automatic detection.

### Azure AI Foundry / Projects

Modern Azure AI platform with centralized model management and OAuth authentication.

**Endpoint Format:**
```
https://<account>.services.ai.azure.com/api/projects/<project-name>
```

**Example:**
```csharp
var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-account.services.ai.azure.com/api/projects/my-project",
        model: "gpt-4",
        configure: opts => opts.UseDefaultAzureCredential = true)
    .BuildAsync();
```

**Features:**
- OAuth/Entra ID authentication required
- Centralized project management
- Access to Azure AI Foundry resources
- Unified endpoint for multiple models
-  API key authentication not supported

### Traditional Azure OpenAI

Classic Azure OpenAI Service endpoints.

**Endpoint Format:**
```
https://<resource-name>.openai.azure.com
```

**Example:**
```csharp
var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-resource.openai.azure.com",
        model: "gpt-4",
        apiKey: "your-api-key")
    .BuildAsync();
```

**Features:**
- API key authentication supported
- OAuth/Entra ID authentication supported
- Direct resource access
- Backward compatibility

## Supported Models

Azure AI provides access to OpenAI models through Azure's infrastructure.

### Common Models

| Model ID | Description | Context Window | Best For |
|----------|-------------|----------------|----------|
| `gpt-4o` | GPT-4 Omni | 128K tokens | Most capable, multimodal |
| `gpt-4o-mini` | GPT-4 Omni Mini | 128K tokens | Fast, cost-effective |
| `gpt-4-turbo` | GPT-4 Turbo | 128K tokens | Advanced reasoning |
| `gpt-4` | GPT-4 | 8K tokens | Original GPT-4 |
| `gpt-4-32k` | GPT-4 32K | 32K tokens | Extended context |
| `gpt-35-turbo` | GPT-3.5 Turbo | 16K tokens | Fast, cost-effective |

### Model Naming

In Azure, you deploy models with custom deployment names:

**Deployment Name vs Model ID:**
```csharp
// Use your deployment name, not the model ID
.WithAzureAI(
    endpoint: "https://my-resource.openai.azure.com",
    model: "my-gpt4-deployment",  // ← Your custom deployment name
    ...)
```

**Finding Deployment Names:**
1. Azure Portal → Azure OpenAI resource → Model deployments
2. Azure AI Foundry → Your project → Deployments

## Advanced Features

### Structured Outputs with JSON Schema

Force the model to generate JSON matching a specific schema.

```csharp
var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-resource.openai.azure.com",
        model: "gpt-4",
        configure: opts =>
        {
            opts.ResponseFormat = "json_schema";
            opts.JsonSchemaName = "ContactInfo";
            opts.JsonSchema = @"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"" },
                    ""email"": { ""type"": ""string"", ""format"": ""email"" },
                    ""phone"": { ""type"": ""string"" }
                },
                ""required"": [""name"", ""email""],
                ""additionalProperties"": false
            }";
            opts.JsonSchemaIsStrict = true;
        })
    .BuildAsync();

var response = await agent.RunAsync("Extract contact info: John Doe, john@example.com");
// Response will be valid JSON matching the schema
```

### Deterministic Generation

Generate reproducible outputs for testing and debugging.

```csharp
var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-resource.openai.azure.com",
        model: "gpt-4",
        configure: opts =>
        {
            opts.Seed = 12345; // Same seed = same output
            opts.Temperature = 0.0f; // Remove randomness
        })
    .BuildAsync();

// Will always generate the same response for the same input
var response1 = await agent.RunAsync("Write a haiku about code");
var response2 = await agent.RunAsync("Write a haiku about code");
// response1 == response2 (with high probability)
```

### Vision Capabilities

Use GPT-4 Vision models to analyze images.

```csharp
var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-resource.openai.azure.com",
        model: "gpt-4o") // Vision-capable model
    .BuildAsync();

var response = await agent.RunAsync(
    "What objects are in this image?",
    imageUrl: "https://example.com/image.jpg");
```

### Function Calling with Tools

```csharp
public class WeatherToolkit
{
    [AIFunction("Get current weather for a location")]
    public string GetWeather(string location)
    {
        return $"Weather in {location}: Sunny, 72°F";
    }
}

var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-resource.openai.azure.com",
        model: "gpt-4",
        configure: opts => opts.ToolChoice = "auto")
    .WithToolkit<WeatherToolkit>()
    .BuildAsync();

var response = await agent.RunAsync("What's the weather in Paris?");
```

## Error Handling

The Azure AI provider includes intelligent error classification and automatic retry logic.

### Error Categories

| Category | HTTP Status | Retry Behavior | Examples |
|----------|-------------|----------------|----------|
| **AuthError** | 401, 403 |  No retry | Invalid credentials, insufficient permissions |
| **RateLimitRetryable** | 429 | Exponential backoff | Quota exceeded, throttling |
| **ClientError** | 400, 404 |  No retry | Invalid request, deployment not found |
| **Transient** | 503 | Retry | Service unavailable, timeout |
| **ServerError** | 500-599 | Retry | Internal server error |

### Common Exceptions

#### DeploymentNotFound (404)
```
The API deployment for this resource does not exist.
```
**Solution:**
- Verify deployment name matches exactly in Azure Portal
- Wait 2-5 minutes if just created
- Check deployment is in correct resource/project

#### AccessDeniedException (401/403)
```
Authentication failed or insufficient permissions.
```
**Solution:**
- For OAuth: Run `az login` or verify Managed Identity
- For API key: Verify key is correct and not expired
- Check Azure RBAC permissions include `Cognitive Services OpenAI User` role

#### RateLimitException (429)
```
Rate limit exceeded - requests throttled.
```
**Solution:** Automatically retried with exponential backoff. If persistent:
- Request quota increase in Azure Portal
- Use multiple deployments for load distribution
- Implement request rate limiting

#### InvalidRequestException (400)
```
Validation error in request parameters.
```
**Solution:**
- Check parameter ranges (temperature, top_p, etc.)
- Verify JSON schema format
- Ensure model supports requested features

## Examples

### Example 1: Basic Chat

```csharp
using HPD.Agent;
using HPD.Agent.Providers.AzureAI;

var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-resource.openai.azure.com",
        model: "gpt-4",
        configure: opts => opts.UseDefaultAzureCredential = true)
    .BuildAsync();

var response = await agent.RunAsync("Explain quantum computing simply.");
Console.WriteLine(response);
```

### Example 2: Streaming Responses

```csharp
var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-resource.openai.azure.com",
        model: "gpt-4")
    .BuildAsync();

await foreach (var chunk in agent.RunAsync("Write a story about AI."))
{
    Console.Write(chunk);
}
```

### Example 3: Function Calling

```csharp
public class CalculatorToolkit
{
    [AIFunction("Add two numbers")]
    public double Add(double a, double b) => a + b;

    [AIFunction("Multiply two numbers")]
    public double Multiply(double a, double b) => a * b;
}

var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-resource.openai.azure.com",
        model: "gpt-4",
        configure: opts => opts.ToolChoice = "auto")
    .WithToolkit<CalculatorToolkit>()
    .BuildAsync();

var response = await agent.RunAsync("What is 25 * 4 + 10?");
```

### Example 4: Structured Output

```csharp
var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-resource.openai.azure.com",
        model: "gpt-4",
        configure: opts =>
        {
            opts.ResponseFormat = "json_schema";
            opts.JsonSchemaName = "MovieReview";
            opts.JsonSchema = @"{
                ""type"": ""object"",
                ""properties"": {
                    ""title"": { ""type"": ""string"" },
                    ""rating"": { ""type"": ""number"", ""minimum"": 0, ""maximum"": 10 },
                    ""summary"": { ""type"": ""string"" },
                    ""recommendation"": { ""type"": ""boolean"" }
                },
                ""required"": [""title"", ""rating"", ""summary"", ""recommendation""]
            }";
            opts.JsonSchemaIsStrict = true;
        })
    .BuildAsync();

var response = await agent.RunAsync("Review the movie Inception.");
// Returns structured JSON matching the schema
```

### Example 5: Multi-Region Deployment

```csharp
var config = new AgentConfig
{
    Provider = new ProviderConfig
    {
        ProviderKey = "azure-ai",
        ModelName = "gpt-4"
    }
};

// Primary region
var primaryAgent = new AgentBuilder(config)
    .WithAzureAI(
        endpoint: "https://eastus-resource.openai.azure.com",
        model: "gpt-4")
    .BuildAsync();

// Failover region
var failoverAgent = new AgentBuilder(config)
    .WithAzureAI(
        endpoint: "https://westus-resource.openai.azure.com",
        model: "gpt-4")
    .BuildAsync();

try
{
    var response = await primaryAgent.RunAsync(message);
}
catch
{
    var response = await failoverAgent.RunAsync(message);
}
```

### Example 6: Azure AI Foundry with Projects

```csharp
var agent = await new AgentBuilder()
    .WithAzureAI(
        endpoint: "https://my-account.services.ai.azure.com/api/projects/my-project",
        model: "gpt-4",
        configure: opts =>
        {
            opts.UseDefaultAzureCredential = true;
            opts.MaxTokens = 4096;
            opts.Temperature = 0.7f;
        })
    .BuildAsync();

var response = await agent.RunAsync("Analyze this data...");
```

## Troubleshooting

### "Endpoint is required for Azure AI"

**Problem:** Missing endpoint configuration.

**Solution:**
```csharp
// Option 1: Explicit endpoint
.WithAzureAI(endpoint: "https://my-resource.openai.azure.com", ...)

// Option 2: Environment variable
Environment.SetEnvironmentVariable("AZURE_AI_ENDPOINT", "https://...");

// Option 3: appsettings.json
{ "AzureAI": { "Endpoint": "https://..." } }
```

### "DefaultAzureCredential failed to retrieve a token"

**Problem:** No authentication method available.

**Solution:** Install Azure CLI and login:
```bash
brew install azure-cli  # or: winget install Microsoft.AzureCLI
az login
```

### "Azure AI Foundry/Projects endpoints require OAuth"

**Problem:** Trying to use API key with Azure AI Foundry endpoint.

**Solution:** Azure AI Foundry only supports OAuth:
```csharp
configure: opts => opts.UseDefaultAzureCredential = true
// OR remove the API key and it will automatically use OAuth
```

### "HTTP 404 (DeploymentNotFound)"

**Problem:** Deployment name doesn't exist or not ready.

**Solution:**
1. Verify deployment name in Azure Portal (case-sensitive!)
2. Wait 2-5 minutes if just created
3. Check you're using the correct endpoint for the deployment

### "Temperature must be between 0 and 2"

**Problem:** Invalid temperature value.

**Solution:**
```csharp
opts.Temperature = 0.7f  // Valid (0.0-2.0)
// NOT: opts.Temperature = 3.0f  //  Invalid
```

### "AzureCliCredential authentication failed: Azure CLI not installed"

**Problem:** Azure CLI not installed for OAuth authentication.

**Solution:** Install Azure CLI:
```bash
# macOS
brew install azure-cli

# Windows
winget install Microsoft.AzureCLI

# Linux
curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash
```

Then login:
```bash
az login
```

## ProviderOptionsJson Reference

When configuring via JSON file, all Azure AI-specific options go inside the `providerOptionsJson` string. The keys are **camelCase** and map 1:1 to `AzureAIProviderConfig` properties.

A complete example:
```json
{
    "name": "MyAgent",
    "provider": {
        "providerKey": "azure-ai",
        "modelName": "gpt-4o",
        "endpoint": "https://your-project.services.ai.azure.com",
        "providerOptionsJson": "{\"maxTokens\":4096,\"temperature\":0.7,\"topP\":0.95,\"seed\":42,\"toolChoice\":\"auto\"}"
    }
}
```

| JSON key | Type | Values / Range | Description |
|---|---|---|---|
| `maxTokens` | int | ≥ 1 | Max tokens to generate |
| `temperature` | float | 0.0–2.0 | Sampling randomness |
| `topP` | float | 0.0–1.0 | Nucleus sampling threshold |
| `frequencyPenalty` | float | -2.0–2.0 | Reduce token repetition |
| `presencePenalty` | float | -2.0–2.0 | Encourage topic diversity |
| `stopSequences` | string[] | — | Stop generation sequences |
| `seed` | long | any | Deterministic generation seed |
| `responseFormat` | string | `"text"`, `"json_object"`, `"json_schema"` | Output format |
| `jsonSchemaName` | string | — | Schema name (required for `json_schema`) |
| `jsonSchema` | string | — | JSON schema definition string |
| `jsonSchemaDescription` | string | — | Optional schema description |
| `jsonSchemaIsStrict` | bool | — | Enforce strict schema (default `true`) |
| `toolChoice` | string | `"auto"`, `"none"`, `"required"` | Tool selection behavior |
| `projectId` | string | — | Azure AI Project ID (extracted from endpoint if omitted) |
| `useDefaultAzureCredential` | bool | — | Use Entra ID / DefaultAzureCredential instead of API key |

---

## AzureAIProviderConfig API Reference

### Core Parameters

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `MaxTokens` | `int?` | ≥ 1 | 4096 | Maximum tokens to generate |
| `Temperature` | `float?` | 0.0-2.0 | 1.0 | Sampling temperature |
| `TopP` | `float?` | 0.0-1.0 | 1.0 | Nucleus sampling threshold |
| `FrequencyPenalty` | `float?` | -2.0 to 2.0 | 0.0 | Reduces token repetition |
| `PresencePenalty` | `float?` | -2.0 to 2.0 | 0.0 | Encourages topic diversity |
| `StopSequences` | `List<string>?` | - | - | Stop generation sequences |

### Determinism

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Seed` | `long?` | - | Seed for reproducible outputs |

### Response Format

| Property | Type | Values | Default | Description |
|----------|------|--------|---------|-------------|
| `ResponseFormat` | `string?` | "text", "json_object", "json_schema" | "text" | Output format |
| `JsonSchemaName` | `string?` | - | - | Schema name (required for json_schema) |
| `JsonSchema` | `string?` | - | - | JSON schema definition |
| `JsonSchemaDescription` | `string?` | - | - | Schema description |
| `JsonSchemaIsStrict` | `bool?` | - | `true` | Enforce strict validation |

### Tool/Function Calling

| Property | Type | Values | Default | Description |
|----------|------|--------|---------|-------------|
| `ToolChoice` | `string?` | "auto", "none", "required" | "auto" | Tool selection behavior |

### Azure Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `UseDefaultAzureCredential` | `bool` | `false` | Use OAuth/Entra ID authentication |
| `ProjectId` | `string?` | - | Azure AI Project ID (optional) |

### Advanced Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AdditionalProperties` | `Dictionary<string, object>?` | - | Custom model parameters |

## Additional Resources

- [Azure OpenAI Documentation](https://learn.microsoft.com/en-us/azure/ai-services/openai/)
- [Azure AI Foundry](https://learn.microsoft.com/en-us/azure/ai-studio/)
- [Azure.AI.Projects SDK](https://www.nuget.org/packages/Azure.AI.Projects)
- [DefaultAzureCredential Guide](https://learn.microsoft.com/en-us/dotnet/api/azure.identity.defaultazurecredential)
- [Azure OpenAI Pricing](https://azure.microsoft.com/en-us/pricing/details/cognitive-services/openai-service/)
- [Azure RBAC for OpenAI](https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/role-based-access-control)
