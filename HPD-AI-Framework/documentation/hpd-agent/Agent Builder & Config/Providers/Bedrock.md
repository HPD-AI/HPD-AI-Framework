# AWS Bedrock Provider

**Provider Key:** `bedrock`

## Overview

The AWS Bedrock provider enables HPD-Agent to use Amazon Bedrock's foundation models, including Claude, Llama, Mistral, and many others. Bedrock provides access to multiple model families through a single, unified API with enterprise-grade security and compliance.

**Key Features:**
-  Multiple model families (Claude, Llama, Mistral, Titan, Cohere, AI21, and more)
-  Streaming support for real-time responses
-  Function/tool calling capabilities
-  Vision support (Claude 3+ models)
-  AWS Guardrails integration
-  Prompt caching (Claude 3.5+ models)
-  IAM-based authentication
-  VPC endpoint support
-  FIPS compliance options

**For detailed API documentation, see:**
- [**BedrockProviderConfig API Reference**](#bedrockproviderconfig-api-reference) - Complete property listing

## Quick Start

### Minimal Example

```csharp
using HPD.Agent;
using HPD.Agent.Providers.Bedrock;

// Set AWS credentials via environment variables
Environment.SetEnvironmentVariable("AWS_REGION", "us-east-1");
Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "your-access-key");
Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "your-secret-key");

var agent = await new AgentBuilder()
    .WithBedrock(
        model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
        region: "us-east-1")
    .BuildAsync();

var response = await agent.RunAsync("What is the capital of France?");
Console.WriteLine(response);
```

## Installation

```bash
dotnet add package HPD-Agent.Providers.Bedrock
```

**Dependencies:**
- `AWSSDK.BedrockRuntime` - AWS Bedrock Runtime SDK
- `AWSSDK.Core` - AWS SDK core functionality
- `AWSSDK.Extensions.Bedrock.MEAI` - Microsoft.Extensions.AI integration
- `Microsoft.Extensions.AI` - AI abstractions

## Configuration

### Configuration Patterns

The AWS Bedrock provider supports all three configuration patterns. Choose the one that best fits your needs.

#### 1. Builder Pattern (Fluent API)

Best for: Simple configurations and quick prototyping.

```csharp
var agent = await new AgentBuilder()
    .WithBedrock(
        model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
        region: "us-east-1",
        configure: opts =>
        {
            opts.MaxTokens = 4096;
            opts.Temperature = 0.7f;
            opts.TopP = 0.9f;
        })
    .BuildAsync();
```

#### 2. JSON Config File

Best for: A single `agent.json` that fully describes the agent — no code required for configuration.

**`bedrock-config.json`:**
```json
{
    "name": "BedrockAgent",
    "systemInstructions": "You are a helpful assistant.",
    "provider": {
        "providerKey": "bedrock",
        "modelName": "anthropic.claude-3-5-sonnet-20241022-v2:0",
        "providerOptionsJson": "{\"region\":\"us-east-1\",\"maxTokens\":4096,\"temperature\":0.7}"
    }
}
```

```csharp
var agent = await AgentConfig.BuildFromFileAsync("bedrock-config.json");
```

The `providerOptionsJson` value is a JSON string containing Bedrock-specific options. All keys are **camelCase** — see the [ProviderOptionsJson reference](#provideroptionsjson-reference) below for the full list.

#### 3. C# Config Object

Best for: Reusable config shared across multiple builder instances.

```csharp
var config = new AgentConfig
{
    Name = "BedrockAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "bedrock",
        ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
    }
};

config.Provider.SetTypedProviderConfig(new BedrockProviderConfig
{
    Region = "us-east-1",
    MaxTokens = 4096,
    Temperature = 0.7f
});

var agent = await config.BuildAsync();
```

#### 4. Builder + Config Pattern (Recommended)

Best for: Production deployments with reusable configuration and runtime customization.

```csharp
// Define base config once
var config = new AgentConfig
{
    Name = "BedrockAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "bedrock",
        ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
    }
};

var bedrockOpts = new BedrockProviderConfig
{
    Region = "us-east-1",
    MaxTokens = 4096,
    Temperature = 0.7f
};
config.Provider.SetTypedProviderConfig(bedrockOpts);

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

The `BedrockProviderConfig` class provides comprehensive configuration options organized by category:

#### Core Parameters

```csharp
configure: opts =>
{
    // Maximum tokens to generate (default: model-specific)
    opts.MaxTokens = 4096;

    // Sampling temperature (0.0-1.0, default: model-specific)
    opts.Temperature = 0.7f;

    // Top-P nucleus sampling (0.0-1.0)
    opts.TopP = 0.9f;

    // Stop sequences (max 2500)
    opts.StopSequences = new List<string> { "STOP", "END" };
}
```

#### AWS Credentials & Region

```csharp
configure: opts =>
{
    // AWS Region (required) - can also use environment variables
    opts.Region = "us-east-1";

    // Explicit credentials (optional - uses AWS credential chain if not provided)
    opts.AccessKeyId = "AKIAIOSFODNN7EXAMPLE";
    opts.SecretAccessKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";

    // Session token for temporary credentials (STS/AssumeRole)
    opts.SessionToken = "temporary-session-token";

    // Or use AWS profile from ~/.aws/credentials
    opts.ProfileName = "my-aws-profile";
}
```

#### Tool/Function Calling

```csharp
configure: opts =>
{
    // Tool choice behavior: "auto" (default), "any", "tool"
    opts.ToolChoice = "auto";

    // Force specific tool (requires ToolChoice = "tool")
    opts.ToolChoiceName = "get_weather";
}
```

#### Guardrails

```csharp
configure: opts =>
{
    // Guardrail ID or ARN
    opts.GuardrailIdentifier = "guardrail-id";

    // Guardrail version (required with identifier)
    opts.GuardrailVersion = "1";

    // Trace guardrail evaluation: "enabled" or "disabled"
    opts.GuardrailTrace = "enabled";
}
```

#### Advanced Options

```csharp
configure: opts =>
{
    // Request timeout in milliseconds
    opts.RequestTimeoutMs = 120000; // 2 minutes

    // Maximum retry attempts
    opts.MaxRetryAttempts = 3;

    // Use FIPS-compliant endpoints (US only)
    opts.UseFipsEndpoint = true;

    // Custom VPC endpoint URL
    opts.ServiceUrl = "https://vpce-xxx.bedrock-runtime.us-east-1.vpce.amazonaws.com";

    // Enable prompt caching (Claude 3.5+ only)
    opts.EnablePromptCaching = true;

    // Additional model-specific request fields
    opts.AdditionalModelRequestFields = new Dictionary<string, object>
    {
        ["customField"] = "value"
    };
}
```

## Authentication

AWS Bedrock uses AWS IAM for authentication. The provider supports multiple authentication methods with priority ordering.

### Authentication Priority Order

1. **Explicit credentials** in `BedrockProviderConfig`
2. **AWS profile** from `~/.aws/credentials`
3. **AWS Default Credential Chain**:
   - Environment variables (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_SESSION_TOKEN`)
   - AWS credentials file (`~/.aws/credentials`)
   - IAM role (for EC2, ECS, Lambda, etc.)

### Method 1: Environment Variables (Recommended for Development)

```bash
export AWS_REGION="us-east-1"
export AWS_ACCESS_KEY_ID="your-access-key"
export AWS_SECRET_ACCESS_KEY="your-secret-key"
```

```csharp
// Automatically uses environment variables
var agent = await new AgentBuilder()
    .WithBedrock(model: "anthropic.claude-3-5-sonnet-20241022-v2:0")
    .BuildAsync();
```

### Method 2: AWS Profile (Recommended for Local Development)

**~/.aws/credentials:**
```ini
[default]
aws_access_key_id = your-access-key
aws_secret_access_key = your-secret-key
region = us-east-1

[production]
aws_access_key_id = prod-access-key
aws_secret_access_key = prod-secret-key
region = us-west-2
```

```csharp
var agent = await new AgentBuilder()
    .WithBedrock(
        model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
        configure: opts => opts.ProfileName = "production")
    .BuildAsync();
```

### Method 3: Explicit Credentials (Use with Caution)

```csharp
var agent = await new AgentBuilder()
    .WithBedrock(
        model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
        region: "us-east-1",
        configure: opts =>
        {
            opts.AccessKeyId = "AKIAIOSFODNN7EXAMPLE";
            opts.SecretAccessKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";
        })
    .BuildAsync();
```

 **Security Warning:** Never hardcode credentials in source code. Use environment variables, AWS profiles, or IAM roles instead.

### Method 4: Temporary Credentials (STS/AssumeRole)

```csharp
var agent = await new AgentBuilder()
    .WithBedrock(
        model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
        region: "us-east-1",
        configure: opts =>
        {
            opts.AccessKeyId = "ASIATEMP...";
            opts.SecretAccessKey = "secret...";
            opts.SessionToken = "temporary-session-token";
        })
    .BuildAsync();
```

### Method 5: IAM Role (Recommended for Production)

When running on AWS infrastructure (EC2, ECS, Lambda, etc.), use IAM roles - no credentials needed:

```csharp
// Automatically uses attached IAM role
var agent = await new AgentBuilder()
    .WithBedrock(
        model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
        region: "us-east-1")
    .BuildAsync();
```

## Supported Models

AWS Bedrock provides access to multiple foundation model families. For the complete and up-to-date list of available models, see:

**[AWS Bedrock Model IDs Documentation](https://docs.aws.amazon.com/bedrock/latest/userguide/model-ids.html)**

### Common Model Families

- **Anthropic Claude** - Advanced reasoning and coding (Claude 3.5 Sonnet, Claude 3 Opus, Haiku)
- **Meta Llama** - Open-source models (Llama 3.2, 3.1, 2)
- **Mistral AI** - Efficient multilingual models (Mistral Large, Mixtral)
- **Amazon Titan** - AWS-native models for text and embeddings
- **Cohere** - Enterprise search and generation (Command, Command R+)
- **AI21 Labs** - Jurassic models for text generation

### Model ID Format

Bedrock model IDs follow this pattern:
```
provider.model-name-version
```

**Examples:**
- `anthropic.claude-3-5-sonnet-20241022-v2:0`
- `meta.llama3-70b-instruct-v1:0`
- `mistral.mistral-large-2402-v1:0`

## Advanced Features

### Guardrails

AWS Bedrock Guardrails help you implement safeguards for your generative AI applications.

```csharp
var agent = await new AgentBuilder()
    .WithBedrock(
        model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
        region: "us-east-1",
        configure: opts =>
        {
            // Configure guardrail
            opts.GuardrailIdentifier = "my-guardrail-id";
            opts.GuardrailVersion = "1";
            opts.GuardrailTrace = "enabled"; // Include evaluation details
        })
    .BuildAsync();
```

**Guardrail Features:**
- Content filtering (harmful content, PII, profanity)
- Denied topics enforcement
- Word and phrase filters
- Sensitive information redaction

**Resources:**
- [Bedrock Guardrails Documentation](https://docs.aws.amazon.com/bedrock/latest/userguide/guardrails.html)

### Prompt Caching

Prompt caching reduces latency and costs for Claude 3.5+ models by caching frequently used context.

```csharp
var agent = await new AgentBuilder()
    .WithBedrock(
        model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
        region: "us-east-1",
        configure: opts =>
        {
            opts.EnablePromptCaching = true;
        })
    .BuildAsync();
```

**Benefits:**
-  Reduced latency for repeated prompts
-  Lower costs for cached tokens
-  Ideal for long system prompts or documents

**Supported Models:** Claude 3.5 Sonnet and later

### VPC Endpoints

Connect to Bedrock through private VPC endpoints for enhanced security.

```csharp
var agent = await new AgentBuilder()
    .WithBedrock(
        model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
        region: "us-east-1",
        configure: opts =>
        {
            // Use VPC endpoint instead of public endpoint
            opts.ServiceUrl = "https://vpce-xxx.bedrock-runtime.us-east-1.vpce.amazonaws.com";
        })
    .BuildAsync();
```

**Resources:**
- [VPC Endpoints for Bedrock](https://docs.aws.amazon.com/bedrock/latest/userguide/vpc-interface-endpoints.html)

### FIPS Compliance

Use FIPS 140-2 validated cryptographic modules (US regions only).

```csharp
var agent = await new AgentBuilder()
    .WithBedrock(
        model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
        region: "us-east-1",
        configure: opts =>
        {
            opts.UseFipsEndpoint = true;
        })
    .BuildAsync();
```

## Error Handling

The Bedrock provider includes intelligent error classification and automatic retry logic.

### Error Categories

| Category | HTTP Status | Retry Behavior | Examples |
|----------|-------------|----------------|----------|
| **AuthError** | 401, 403 |  No retry | Invalid credentials, insufficient permissions |
| **RateLimitRetryable** | 429 |  Exponential backoff | ThrottlingException, quota exceeded |
| **ClientError** | 400, 404 |  No retry | ValidationException, model not found |
| **Transient** | 503 |  Retry | ServiceUnavailable, ModelNotReady |
| **ServerError** | 500-599 |  Retry | InternalServerException |

### Automatic Retry Configuration

```csharp
var agent = await new AgentBuilder()
    .WithBedrock(
        model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
        region: "us-east-1",
        configure: opts =>
        {
            // Configure retry behavior
            opts.MaxRetryAttempts = 3;
            opts.RequestTimeoutMs = 120000; // 2 minutes
        })
    .BuildAsync();
```

### Common Exceptions

#### ThrottlingException (429)
```
Rate limit exceeded - automatic exponential backoff retry
```
**Solution:** Reduce request rate or request quota increase

#### AccessDeniedException (403)
```
Access denied - insufficient IAM permissions
```
**Solution:** Verify IAM policy includes `bedrock:InvokeModel` permission

#### ValidationException (400)
```
Invalid request parameters
```
**Solution:** Check model ID format, parameter ranges, and input validation

#### ModelNotReadyException (503)
```
Model is still loading or temporarily unavailable
```
**Solution:** Automatically retried with backoff

#### ResourceNotFoundException (404)
```
Model not found or not enabled
```
**Solution:** Verify model ID and ensure model is enabled in AWS console

## Examples

### Example 1: Basic Chat with Claude

```csharp
using HPD.Agent;
using HPD.Agent.Providers.Bedrock;

var agent = await new AgentBuilder()
    .WithBedrock(
        model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
        region: "us-east-1")
    .BuildAsync();

var response = await agent.RunAsync("Explain quantum computing in simple terms.");
Console.WriteLine(response);
```

### Example 2: Function Calling with Tools

```csharp
public class WeatherToolkit
{
    [Function("Get current weather for a location")]
    public string GetWeather(string location)
    {
        return $"The weather in {location} is sunny, 72°F";
    }
}

var agent = await new AgentBuilder()
    .WithBedrock(
        model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
        region: "us-east-1",
        configure: opts => opts.ToolChoice = "auto")
    .WithToolkit<WeatherToolkit>()
    .BuildAsync();

var response = await agent.RunAsync("What's the weather in Seattle?");
```

### Example 3: Streaming Responses

```csharp
var agent = await new AgentBuilder()
    .WithBedrock(
        model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
        region: "us-east-1")
    .BuildAsync();

await foreach (var chunk in agent.RunAsync("Write a short story about AI."))
{
    Console.Write(chunk);
}
```

### Example 4: Multi-Region Deployment

```csharp
// Load base config
var config = new AgentConfig
{
    Name = "BedrockAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "bedrock",
        ModelName = "anthropic.claude-3-5-sonnet-20241022-v2:0"
    }
};

// Deploy to multiple regions
var usEastAgent = new AgentBuilder(config)
    .WithBedrock(
        model: config.Provider.ModelName,
        region: "us-east-1")
    .BuildAsync();

var usWestAgent = new AgentBuilder(config)
    .WithBedrock(
        model: config.Provider.ModelName,
        region: "us-west-2")
    .BuildAsync();
```

### Example 5: Guardrails with Content Filtering

```csharp
var agent = await new AgentBuilder()
    .WithBedrock(
        model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
        region: "us-east-1",
        configure: opts =>
        {
            opts.GuardrailIdentifier = "content-filter-guardrail";
            opts.GuardrailVersion = "1";
            opts.GuardrailTrace = "enabled";
        })
    .BuildAsync();

try
{
    var response = await agent.RunAsync("User input here");
    Console.WriteLine(response);
}
catch (Exception ex) when (ex.Message.Contains("guardrail"))
{
    Console.WriteLine("Content blocked by guardrail");
}
```

## Troubleshooting

### "AWS Region is required"

**Problem:** Missing AWS region configuration.

**Solution:**
```csharp
// Option 1: Explicit region
.WithBedrock(model: "...", region: "us-east-1")

// Option 2: Environment variable
Environment.SetEnvironmentVariable("AWS_REGION", "us-east-1");

// Option 3: Config object
configure: opts => opts.Region = "us-east-1"
```

### "AccessDeniedException"

**Problem:** IAM permissions insufficient.

**Solution:** Ensure IAM policy includes required permissions:
```json
{
    "Version": "2012-10-17",
    "Statement": [
        {
            "Effect": "Allow",
            "Action": [
                "bedrock:InvokeModel",
                "bedrock:InvokeModelWithResponseStream"
            ],
            "Resource": "arn:aws:bedrock:*::foundation-model/*"
        }
    ]
}
```

### "ValidationException: Model not found"

**Problem:** Invalid model ID or model not enabled.

**Solution:**
1. Verify model ID format: `provider.model-name-version`
2. Enable model in AWS Console: Bedrock → Model access
3. Check model availability in your region

### "ThrottlingException"

**Problem:** Rate limit exceeded.

**Solution:** The provider automatically retries with exponential backoff. If persistent:
1. Request quota increase in AWS Service Quotas
2. Implement request rate limiting
3. Use multiple regions for load distribution

### "Temperature must be between 0 and 1"

**Problem:** Invalid temperature value for Bedrock.

**Solution:** Unlike some providers, Bedrock uses 0.0-1.0 range:
```csharp
configure: opts => opts.Temperature = 0.7f  //  Valid (0.0-1.0)
// NOT: opts.Temperature = 1.5f  //  Invalid for Bedrock
```

### Connection timeout errors

**Problem:** Requests timing out.

**Solution:** Increase timeout for large responses:
```csharp
configure: opts =>
{
    opts.RequestTimeoutMs = 180000; // 3 minutes
    opts.MaxTokens = 8192; // Or reduce max tokens
}
```

## ProviderOptionsJson Reference

When configuring via JSON file, all Bedrock-specific options go inside the `providerOptionsJson` string. The keys are **camelCase** and map 1:1 to `BedrockProviderConfig` properties.

A complete example:
```json
{
    "name": "MyAgent",
    "provider": {
        "providerKey": "bedrock",
        "modelName": "anthropic.claude-3-5-sonnet-20241022-v2:0",
        "providerOptionsJson": "{\"region\":\"us-east-1\",\"maxTokens\":4096,\"temperature\":0.7,\"topP\":0.9,\"toolChoice\":\"auto\"}"
    }
}
```

| JSON key | Type | Values / Range | Description |
|---|---|---|---|
| `maxTokens` | int | ≥ 1 | Max tokens to generate |
| `temperature` | float | 0.0–1.0 | Sampling randomness |
| `topP` | float | 0.0–1.0 | Nucleus sampling threshold |
| `stopSequences` | string[] | max 2500 | Stop generation sequences |
| `region` | string | — | AWS region (e.g. `"us-east-1"`) — required |
| `accessKeyId` | string | — | AWS access key ID (uses credential chain if omitted) |
| `secretAccessKey` | string | — | AWS secret access key (uses credential chain if omitted) |
| `sessionToken` | string | — | AWS session token (for temporary credentials) |
| `profileName` | string | — | AWS credentials profile (default: `"default"`) |
| `toolChoice` | string | `"auto"`, `"any"`, `"tool"` | Tool selection behavior |
| `toolChoiceName` | string | — | Specific tool name (when `toolChoice` is `"tool"`) |
| `enablePromptCaching` | bool | — | Enable prompt caching (Claude 3.5+ on Bedrock) |
| `guardrailIdentifier` | string | — | Guardrail ID or ARN |
| `guardrailVersion` | string | — | Guardrail version or `"DRAFT"` |
| `guardrailTrace` | string | `"enabled"`, `"disabled"` | Include guardrail assessment in response |
| `requestTimeoutMs` | int | ≥ 1 | Request timeout in milliseconds |
| `maxRetryAttempts` | int | ≥ 0 | Max retry attempts (default: SDK default) |
| `useFipsEndpoint` | bool | — | Use FIPS-compliant endpoints (US gov/compliance) |
| `serviceUrl` | string | — | Custom endpoint URL (VPC endpoints, local testing) |
| `additionalModelRequestFields` | object | — | Extra model-specific parameters |

---

## BedrockProviderConfig API Reference

### Core Parameters

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `MaxTokens` | `int?` | ≥ 1 | Model-specific | Maximum tokens to generate |
| `Temperature` | `float?` | 0.0-1.0 | Model-specific | Sampling temperature |
| `TopP` | `float?` | 0.0-1.0 | - | Nucleus sampling threshold |
| `StopSequences` | `List<string>?` | Max 2500 | - | Stop generation sequences |

### AWS Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Region` | `string?` | Required | AWS region (e.g., "us-east-1") |
| `AccessKeyId` | `string?` | - | AWS access key ID |
| `SecretAccessKey` | `string?` | - | AWS secret access key |
| `SessionToken` | `string?` | - | Temporary session token (STS) |
| `ProfileName` | `string?` | - | AWS profile from ~/.aws/credentials |

### Tool/Function Calling

| Property | Type | Values | Default | Description |
|----------|------|--------|---------|-------------|
| `ToolChoice` | `string?` | "auto", "any", "tool" | - | Tool selection behavior |
| `ToolChoiceName` | `string?` | - | - | Specific tool name (requires ToolChoice="tool") |

### Guardrails

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `GuardrailIdentifier` | `string?` | - | Guardrail ID or ARN |
| `GuardrailVersion` | `string?` | - | Guardrail version (required with identifier) |
| `GuardrailTrace` | `string?` | "disabled" | "enabled" or "disabled" |

### Advanced Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `RequestTimeoutMs` | `int?` | 100000 | Request timeout in milliseconds |
| `MaxRetryAttempts` | `int?` | 3 | Maximum retry attempts |
| `UseFipsEndpoint` | `bool` | `false` | Use FIPS-compliant endpoints |
| `ServiceUrl` | `string?` | - | Custom endpoint (e.g., VPC endpoint) |
| `EnablePromptCaching` | `bool` | `false` | Enable prompt caching (Claude 3.5+) |
| `AdditionalModelRequestFields` | `Dictionary<string, object>?` | - | Model-specific parameters |

## Additional Resources

- [AWS Bedrock Documentation](https://docs.aws.amazon.com/bedrock/)
- [Bedrock Model IDs](https://docs.aws.amazon.com/bedrock/latest/userguide/model-ids.html)
- [Bedrock Pricing](https://aws.amazon.com/bedrock/pricing/)
- [IAM Permissions for Bedrock](https://docs.aws.amazon.com/bedrock/latest/userguide/security-iam.html)
- [Bedrock Guardrails](https://docs.aws.amazon.com/bedrock/latest/userguide/guardrails.html)
- [VPC Endpoints for Bedrock](https://docs.aws.amazon.com/bedrock/latest/userguide/vpc-interface-endpoints.html)
