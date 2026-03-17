# Google AI (Gemini) Provider

**Provider Key:** `google-ai`

## Overview

The Google AI provider enables HPD-Agent to use Google's Gemini models through the Google AI API. Gemini offers state-of-the-art capabilities in reasoning, coding, multimodal understanding, and long-context processing with support for text, images, audio, and video.

**Key Features:**
- Latest Gemini models
- Streaming support for real-time responses
- Function/tool calling capabilities
- Vision support (images, videos)
- Audio understanding
- Structured JSON output with schema validation
- Thinking mode (Gemini 3+ models)
- Image generation (select models)
- Safety settings and content filtering
- Prompt caching for reduced latency

**For detailed API documentation, see:**
- [**GoogleAIProviderConfig API Reference**](#googleaiproviderconfig-api-reference) - Complete property listing

## Quick Start

### Minimal Example

```csharp
using HPD.Agent;
using HPD.Agent.Providers.GoogleAI;

// Set API key via environment variable
Environment.SetEnvironmentVariable("GOOGLE_AI_API_KEY", "your-api-key");

var agent = await new AgentBuilder()
    .WithGoogleAI(model: "gemini-2.0-flash")
    .BuildAsync();

var response = await agent.RunAsync("What is the capital of France?");
Console.WriteLine(response);
```

## Installation

```bash
dotnet add package HPD-Agent.Providers.GoogleAI
```

**Dependencies:**
- `Google_GenerativeAI` - Google Generative AI SDK
- `Google_GenerativeAI.Microsoft` - Microsoft.Extensions.AI integration
- `Microsoft.Extensions.AI` - AI abstractions

## Configuration

### Configuration Patterns

The Google AI provider supports all three configuration patterns. Choose the one that best fits your needs.

#### 1. Builder Pattern (Fluent API)

Best for: Simple configurations and quick prototyping.

```csharp
var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-2.0-flash",
        configure: opts =>
        {
            opts.MaxOutputTokens = 8192;
            opts.Temperature = 0.7;
            opts.TopP = 0.95;
            opts.TopK = 40;
        })
    .BuildAsync();
```

#### 2. JSON Config File

Best for: A single `agent.json` that fully describes the agent — no code required for configuration.

**`gemini-config.json`:**
```json
{
    "name": "GeminiAgent",
    "systemInstructions": "You are a helpful assistant.",
    "provider": {
        "providerKey": "google-ai",
        "modelName": "gemini-2.0-flash",
        "apiKey": "your-api-key",
        "providerOptionsJson": "{\"maxOutputTokens\":8192,\"temperature\":0.7,\"topP\":0.95}"
    }
}
```

```csharp
var agent = await AgentConfig.BuildFromFileAsync("gemini-config.json");
```

The `providerOptionsJson` value is a JSON string containing Google AI-specific options. All keys are **camelCase** — see the [ProviderOptionsJson reference](#provideroptionsjson-reference) below for the full list.

#### 3. C# Config Object

Best for: Reusable config shared across multiple builder instances.

```csharp
var config = new AgentConfig
{
    Name = "GeminiAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "google-ai",
        ModelName = "gemini-2.0-flash",
        ApiKey = "your-api-key"
    }
};

config.Provider.SetTypedProviderConfig(new GoogleAIProviderConfig
{
    MaxOutputTokens = 8192,
    Temperature = 0.7,
    TopP = 0.95
});

var agent = await config.BuildAsync();
```

#### 4. Builder + Config Pattern (Recommended)

Best for: Production deployments with reusable configuration and runtime customization.

```csharp
// Define base config once
var config = new AgentConfig
{
    Name = "GeminiAgent",
    Provider = new ProviderConfig
    {
        ProviderKey = "google-ai",
        ModelName = "gemini-2.0-flash",
        ApiKey = "your-api-key"
    }
};

var googleOpts = new GoogleAIProviderConfig
{
    MaxOutputTokens = 8192,
    Temperature = 0.7
};
config.Provider.SetTypedProviderConfig(googleOpts);

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

The `GoogleAIProviderConfig` class provides comprehensive configuration options organized by category:

#### Core Parameters

```csharp
configure: opts =>
{
    // Maximum output tokens (default: model-specific)
    opts.MaxOutputTokens = 8192;
}
```

#### Sampling Parameters

```csharp
configure: opts =>
{
    // Sampling temperature (0.0-2.0, default: model-specific)
    opts.Temperature = 0.7;

    // Top-P nucleus sampling (0.0-1.0)
    opts.TopP = 0.95;

    // Top-K sampling (positive integer)
    opts.TopK = 40;

    // Presence penalty (discourages token reuse)
    opts.PresencePenalty = 0.0;

    // Frequency penalty (penalizes based on usage frequency)
    opts.FrequencyPenalty = 0.0;

    // Stop sequences (up to 5)
    opts.StopSequences = new List<string> { "STOP", "END" };
}
```

#### Determinism

```csharp
configure: opts =>
{
    // Seed for deterministic generation
    opts.Seed = 42;
}
```

#### Response Format

```csharp
configure: opts =>
{
    // Response MIME type: "text/plain", "application/json", "text/x.enum"
    opts.ResponseMimeType = "application/json";

    // JSON schema for structured output (requires application/json)
    opts.ResponseSchema = @"{
        ""type"": ""object"",
        ""properties"": {
            ""name"": { ""type"": ""string"" },
            ""age"": { ""type"": ""number"" }
        },
        ""required"": [""name"", ""age""]
    }";

    // Alternative: JSON schema (mutually exclusive with ResponseSchema)
    // opts.ResponseJsonSchema = "...";

    // Response modalities (e.g., ["TEXT", "IMAGE"])
    opts.ResponseModalities = new List<string> { "TEXT" };

    // Candidate count (currently only 1 is supported)
    opts.CandidateCount = 1;
}
```

#### Advanced Features

```csharp
configure: opts =>
{
    // Export logprobs in response
    opts.ResponseLogprobs = true;

    // Number of top logprobs (requires ResponseLogprobs = true)
    opts.Logprobs = 5;

    // Enhanced civic answers (if available)
    opts.EnableEnhancedCivicAnswers = true;

    // Affective dialog (emotion detection and adaptation)
    opts.EnableAffectiveDialog = true;
}
```

#### Thinking Configuration (Gemini 3+ Models)

```csharp
configure: opts =>
{
    // Include thoughts in response
    opts.IncludeThoughts = true;

    // Thinking budget in tokens
    opts.ThinkingBudget = 5000;

    // Thinking level: "LOW" or "HIGH"
    opts.ThinkingLevel = "HIGH"; // Deeper reasoning for complex tasks
}
```

#### Media & Image Configuration

```csharp
configure: opts =>
{
    // Media resolution: "MEDIA_RESOLUTION_LOW", "MEDIA_RESOLUTION_MEDIUM", "MEDIA_RESOLUTION_HIGH"
    opts.MediaResolution = "MEDIA_RESOLUTION_HIGH";

    // Include audio timestamp
    opts.AudioTimestamp = true;

    // Image generation (for image generation models)
    opts.ImageAspectRatio = "16:9"; // "1:1", "16:9", "9:16", "4:3", "3:4"
    opts.ImageSize = "2K"; // "1K", "2K", "4K"
    opts.ImageOutputMimeType = "image/png"; // "image/png", "image/jpeg"
    opts.ImageCompressionQuality = 75; // 0-100 (for JPEG only)
}
```

#### Routing Configuration

```csharp
configure: opts =>
{
    // Automated routing preference
    opts.ModelRoutingPreference = "PRIORITIZE_QUALITY"; // "PRIORITIZE_QUALITY", "BALANCED", "PRIORITIZE_COST"

    // Manual routing (specific model)
    opts.ManualRoutingModelName = "gemini-1.5-pro-001";
}
```

#### Safety Settings

```csharp
configure: opts =>
{
    opts.SafetySettings = new List<SafetySettingConfig>
    {
        new()
        {
            Category = "HARM_CATEGORY_HARASSMENT",
            Threshold = "BLOCK_MEDIUM_AND_ABOVE",
            Method = "SEVERITY" // Optional: "SEVERITY" or "PROBABILITY"
        },
        new()
        {
            Category = "HARM_CATEGORY_HATE_SPEECH",
            Threshold = "BLOCK_ONLY_HIGH"
        },
        new()
        {
            Category = "HARM_CATEGORY_SEXUALLY_EXPLICIT",
            Threshold = "BLOCK_MEDIUM_AND_ABOVE"
        },
        new()
        {
            Category = "HARM_CATEGORY_DANGEROUS_CONTENT",
            Threshold = "BLOCK_MEDIUM_AND_ABOVE"
        }
    };
}
```

**Harm Categories:**
- `HARM_CATEGORY_HARASSMENT` - Harassment content
- `HARM_CATEGORY_HATE_SPEECH` - Hate speech and content
- `HARM_CATEGORY_SEXUALLY_EXPLICIT` - Sexually explicit content
- `HARM_CATEGORY_DANGEROUS_CONTENT` - Dangerous content
- `HARM_CATEGORY_CIVIC_INTEGRITY` - Content that may harm civic integrity

**Harm Block Thresholds:**
- `BLOCK_NONE` - Allow all content
- `BLOCK_ONLY_HIGH` - Block only high-probability harmful content
- `BLOCK_MEDIUM_AND_ABOVE` - Block medium and high-probability harmful content
- `BLOCK_LOW_AND_ABOVE` - Block low, medium, and high-probability harmful content
- `OFF` - Turn off safety filter

#### Function Calling Configuration

```csharp
configure: opts =>
{
    // Function calling mode: "AUTO", "ANY", "NONE"
    opts.FunctionCallingMode = "AUTO";

    // Allowed function names (only when mode is "ANY")
    opts.AllowedFunctionNames = new List<string> { "get_weather", "search" };
}
```

## Authentication

Google AI uses API keys for authentication. The provider supports multiple ways to configure your API key.

### Authentication Priority Order

1. **Explicit API key** in `WithGoogleAI()` method
2. **Environment variables**: `GOOGLE_AI_API_KEY` or `GEMINI_API_KEY`
3. **Configuration file**: `appsettings.json` under `"googleAI:ApiKey"`

### Method 1: Environment Variables (Recommended for Development)

```bash
export GOOGLE_AI_API_KEY="your-api-key"
# OR
export GEMINI_API_KEY="your-api-key"
```

```csharp
// Automatically uses environment variable
var agent = await new AgentBuilder()
    .WithGoogleAI(model: "gemini-2.0-flash")
    .BuildAsync();
```

### Method 2: Explicit API Key

```csharp
var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-2.0-flash")
    .BuildAsync();
```

**Security Warning:** Never hardcode API keys in source code. Use environment variables or secure configuration management instead.

### Method 3: Configuration File

**appsettings.json:**
```json
{
  "GoogleAI": {
    "ApiKey": "your-api-key"
  }
}
```

```csharp
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .BuildAsync();

// API key automatically resolved from configuration
var agent = await new AgentBuilder()
    .WithGoogleAI(model: "gemini-2.0-flash")
    .BuildAsync();
```

### Getting an API Key

1. Go to [Google AI Studio](https://aistudio.google.com/)
2. Click **Get API key** in the navigation
3. Create a new API key or use an existing one
4. Copy your API key

**Free Tier:** Google AI offers a generous free tier with rate limits. See [Google AI Pricing](https://ai.google.dev/pricing) for details.

## Supported Models

Google AI provides access to the Gemini model family. For the complete and up-to-date list of available models, see:

**[Google AI Model Documentation](https://ai.google.dev/gemini-api/docs/models/gemini)**

### Gemini 2.0 (Latest)

| Model | Description | Context Window | Best For |
|-------|-------------|----------------|----------|
| `gemini-2.0-flash` | Fast, versatile performance | 1M tokens | Production applications, real-time chat |
| `gemini-2.0-flash-exp` | Experimental flash model | 1M tokens | Testing new features |

### Gemini 1.5

| Model | Description | Context Window | Best For |
|-------|-------------|----------------|----------|
| `gemini-1.5-pro` | Advanced reasoning and coding | 2M tokens | Complex tasks, long-context understanding |
| `gemini-1.5-flash` | Fast, cost-effective | 1M tokens | High-volume applications |
| `gemini-1.5-flash-8b` | Compact, efficient | 1M tokens | Resource-constrained environments |

### Gemini 1.0

| Model | Description | Context Window | Best For |
|-------|-------------|----------------|----------|
| `gemini-1.0-pro` | Balanced performance | 32K tokens | General-purpose applications |

### Image Generation Models (Preview)

| Model | Description | Output |
|-------|-------------|--------|
| `gemini-2.5-flash-image` | Fast image generation | PNG/JPEG |
| `gemini-3-pro-image-preview` | High-quality image generation | PNG/JPEG |

### Thinking Models (Preview)

| Model | Description | Special Features |
|-------|-------------|------------------|
| `gemini-3-flash` | Thinking-enabled model | Extended reasoning capabilities |

**Note:** Model availability and capabilities may vary by region. Check the [official documentation](https://ai.google.dev/gemini-api/docs/models/gemini) for the latest information.

## Advanced Features

### Structured JSON Output

Generate responses in structured JSON format with schema validation:

```csharp
var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-2.0-flash",
        configure: opts =>
        {
            opts.ResponseMimeType = "application/json";
            opts.ResponseSchema = @"{
                ""type"": ""object"",
                ""properties"": {
                    ""title"": { ""type"": ""string"" },
                    ""summary"": { ""type"": ""string"" },
                    ""keywords"": {
                        ""type"": ""array"",
                        ""items"": { ""type"": ""string"" }
                    }
                },
                ""required"": [""title"", ""summary""]
            }";
        })
    .BuildAsync();

var response = await agent.RunAsync("Summarize this article: ...");
// Response will be valid JSON matching the schema
```

**Benefits:**
-  Guaranteed valid JSON output
-  Type safety with schema validation
-  Structured data extraction

### Thinking Mode (Gemini 3+ Models)

Enable extended reasoning capabilities for complex problems:

```csharp
var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-3-flash",
        configure: opts =>
        {
            opts.IncludeThoughts = true; // Include reasoning process
            opts.ThinkingBudget = 5000; // Token budget for thinking
            opts.ThinkingLevel = "HIGH"; // Deep reasoning
        })
    .BuildAsync();

var response = await agent.RunAsync("Solve this complex math problem: ...");
// Response includes the model's reasoning process
```

**Use Cases:**
- Mathematical problem-solving
- Code debugging and optimization
- Strategic planning
- Complex reasoning tasks

### Multimodal Understanding

Process images, audio, and video alongside text:

```csharp
var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-2.0-flash",
        configure: opts =>
        {
            opts.MediaResolution = "MEDIA_RESOLUTION_HIGH";
            opts.AudioTimestamp = true;
        })
    .BuildAsync();

// Process image
var response = await agent.RunAsync(new ChatMessage
{
    Role = "user",
    Content = "What's in this image?",
    Attachments = new[]
    {
        new ImageAttachment("path/to/image.jpg")
    }
});
```

**Supported Media:**
- 📷 Images (JPEG, PNG, WebP, etc.)
- 🎵 Audio (MP3, WAV, etc.)
- 🎥 Video (MP4, MOV, etc.)
- 📄 Documents (PDF - text extraction)

### Safety Settings

Configure content filtering for your use case:

```csharp
var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-2.0-flash",
        configure: opts =>
        {
            opts.SafetySettings = new List<SafetySettingConfig>
            {
                // Strict harassment filtering
                new()
                {
                    Category = "HARM_CATEGORY_HARASSMENT",
                    Threshold = "BLOCK_LOW_AND_ABOVE"
                },
                // Moderate hate speech filtering
                new()
                {
                    Category = "HARM_CATEGORY_HATE_SPEECH",
                    Threshold = "BLOCK_MEDIUM_AND_ABOVE"
                },
                // Minimal sexually explicit filtering
                new()
                {
                    Category = "HARM_CATEGORY_SEXUALLY_EXPLICIT",
                    Threshold = "BLOCK_ONLY_HIGH"
                }
            };
        })
    .BuildAsync();
```

**Safety Categories:**
- Harassment
- Hate speech
- Sexually explicit content
- Dangerous content
- Civic integrity

### Image Generation (Preview Models)

Generate images with Gemini image generation models:

```csharp
var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-2.5-flash-image",
        configure: opts =>
        {
            opts.ImageAspectRatio = "16:9"; // Landscape
            opts.ImageSize = "2K"; // High resolution
            opts.ImageOutputMimeType = "image/png";
            opts.ImageCompressionQuality = 90;
        })
    .BuildAsync();

var response = await agent.RunAsync("Generate an image of a sunset over mountains");
```

**Aspect Ratios:**
- `1:1` - Square
- `16:9` - Landscape
- `9:16` - Portrait
- `4:3`, `3:4` - Classic ratios

### Function/Tool Calling

Enable the model to call functions/tools:

```csharp
public class WeatherToolkit
{
    [Function("Get current weather for a location")]
    public string GetWeather(
        [Parameter("City name")] string location,
        [Parameter("Temperature unit (celsius/fahrenheit)")] string unit = "celsius")
    {
        return $"The weather in {location} is sunny, 72°F";
    }
}

var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-2.0-flash",
        configure: opts =>
        {
            opts.FunctionCallingMode = "AUTO"; // Model decides when to call
        })
    .WithToolkit<WeatherToolkit>()
    .BuildAsync();

var response = await agent.RunAsync("What's the weather in San Francisco?");
// Model automatically calls GetWeather function
```

**Function Calling Modes:**
- `AUTO` - Model decides when to call functions (default)
- `ANY` - Model must call one of the allowed functions
- `NONE` - Disable function calling

## Error Handling

The Google AI provider includes intelligent error classification and automatic retry logic.

### Error Categories

| Category | HTTP Status | Retry Behavior | Examples |
|----------|-------------|----------------|----------|
| **AuthError** | 401, 403 | No retry | Invalid API key, insufficient permissions |
| **RateLimitRetryable** | 429 |  Exponential backoff | QUOTA_EXCEEDED, RESOURCE_EXHAUSTED |
| **ClientError** | 400, 404, 413 | No retry | Invalid parameters, model not found, file too large |
| **Transient** | 503 |  Retry | Temporary unavailability, network issues |
| **ServerError** | 500-599 |  Retry | Internal server errors |

### Common Exceptions

#### API_KEY_INVALID (401)
```
Invalid API key
```
**Solution:** Verify your API key is correct and active

#### QUOTA_EXCEEDED (429)
```
Rate limit exceeded - automatic exponential backoff retry
```
**Solution:** Reduce request rate or wait for quota refresh

#### RESOURCE_EXHAUSTED (429)
```
Quota or resource exhausted
```
**Solution:** Check your quota limits or upgrade plan

#### INVALID_ARGUMENT (400)
```
Invalid request parameters
```
**Solution:** Check parameter types, ranges, and required fields

#### SAFETY (400)
```
Content blocked by safety filter
```
**Solution:** Adjust safety settings or modify content

#### BLOCKED (400)
```
Content blocked due to policy violation
```
**Solution:** Review content guidelines

#### FILE_TOO_LARGE (413)
```
Uploaded file exceeds size limit
```
**Solution:** Reduce file size or split into smaller chunks

## Examples

### Example 1: Basic Chat

```csharp
using HPD.Agent;
using HPD.Agent.Providers.GoogleAI;

var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-2.0-flash")
    .BuildAsync();

var response = await agent.RunAsync("Explain quantum computing in simple terms.");
Console.WriteLine(response);
```

### Example 2: Structured Output

```csharp
var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-2.0-flash",
        configure: opts =>
        {
            opts.ResponseMimeType = "application/json";
            opts.ResponseSchema = @"{
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"" },
                    ""email"": { ""type"": ""string"" },
                    ""age"": { ""type"": ""number"" }
                },
                ""required"": [""name"", ""email""]
            }";
        })
    .BuildAsync();

var response = await agent.RunAsync("Extract the person's details: John Smith, john@example.com, 30 years old");
// Returns valid JSON: {"name":"John Smith","email":"john@example.com","age":30}
```

### Example 3: Function Calling

```csharp
public class DatabaseToolkit
{
    [Function("Query database for user information")]
    public string QueryUser([Parameter("User ID")] int userId)
    {
        return $"User {userId}: John Doe, john@example.com";
    }

    [Function("Update user information in database")]
    public string UpdateUser(
        [Parameter("User ID")] int userId,
        [Parameter("New email")] string email)
    {
        return $"Updated user {userId} email to {email}";
    }
}

var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-2.0-flash",
        configure: opts => opts.FunctionCallingMode = "AUTO")
    .WithToolkit<DatabaseToolkit>()
    .BuildAsync();

var response = await agent.RunAsync("Get information for user ID 123 and update their email to newemail@example.com");
```

### Example 4: Streaming Responses

```csharp
var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-2.0-flash")
    .BuildAsync();

await foreach (var chunk in agent.RunAsync("Write a short story about AI."))
{
    Console.Write(chunk); // Print each chunk as it arrives
}
```

### Example 5: Multimodal - Image Understanding

```csharp
var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-2.0-flash",
        configure: opts => opts.MediaResolution = "MEDIA_RESOLUTION_HIGH")
    .BuildAsync();

var response = await agent.RunAsync(new ChatMessage
{
    Role = "user",
    Content = "Describe this image in detail and identify any text.",
    Attachments = new[]
    {
        new ImageAttachment("path/to/document.jpg")
    }
});
```

### Example 6: Thinking Mode for Complex Problems

```csharp
var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-3-flash",
        configure: opts =>
        {
            opts.IncludeThoughts = true;
            opts.ThinkingLevel = "HIGH";
            opts.ThinkingBudget = 10000;
        })
    .BuildAsync();

var response = await agent.RunAsync(@"
    Solve this optimization problem:
    You have 100 items with different weights and values.
    Find the optimal combination that maximizes value while staying under 50kg total weight.
");
// Response includes detailed reasoning process
```

### Example 7: Safety Settings for User-Generated Content

```csharp
var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-2.0-flash",
        configure: opts =>
        {
            // Strict filtering for user-facing application
            opts.SafetySettings = new List<SafetySettingConfig>
            {
                new() { Category = "HARM_CATEGORY_HARASSMENT", Threshold = "BLOCK_LOW_AND_ABOVE" },
                new() { Category = "HARM_CATEGORY_HATE_SPEECH", Threshold = "BLOCK_LOW_AND_ABOVE" },
                new() { Category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", Threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new() { Category = "HARM_CATEGORY_DANGEROUS_CONTENT", Threshold = "BLOCK_MEDIUM_AND_ABOVE" }
            };
        })
    .BuildAsync();

try
{
    var response = await agent.RunAsync("User input here");
    Console.WriteLine(response);
}
catch (Exception ex) when (ex.Message.Contains("SAFETY"))
{
    Console.WriteLine("Content blocked by safety filter");
}
```

### Example 8: High-Precision JSON Extraction

```csharp
var agent = await new AgentBuilder()
    .WithGoogleAI(
        apiKey: "your-api-key",
        model: "gemini-2.0-flash",
        configure: opts =>
        {
            opts.ResponseMimeType = "application/json";
            opts.Temperature = 0.0; // Deterministic
            opts.ResponseSchema = @"{
                ""type"": ""object"",
                ""properties"": {
                    ""invoiceNumber"": { ""type"": ""string"" },
                    ""date"": { ""type"": ""string"", ""format"": ""date"" },
                    ""total"": { ""type"": ""number"" },
                    ""items"": {
                        ""type"": ""array"",
                        ""items"": {
                            ""type"": ""object"",
                            ""properties"": {
                                ""description"": { ""type"": ""string"" },
                                ""amount"": { ""type"": ""number"" }
                            }
                        }
                    }
                },
                ""required"": [""invoiceNumber"", ""date"", ""total""]
            }";
        })
    .BuildAsync();

var invoiceText = "Invoice #INV-001, Date: 2024-01-15...";
var extracted = await agent.RunAsync($"Extract invoice data: {invoiceText}");
// Returns structured JSON matching schema
```

## Troubleshooting

### "API key is required for Google AI"

**Problem:** Missing Google AI API key.

**Solution:**
```csharp
// Option 1: Explicit API key
.WithGoogleAI(apiKey: "your-api-key", model: "gemini-2.0-flash")

// Option 2: Environment variable
Environment.SetEnvironmentVariable("GOOGLE_AI_API_KEY", "your-api-key");

// Option 3: Alternative environment variable
Environment.SetEnvironmentVariable("GEMINI_API_KEY", "your-api-key");
```

### "API_KEY_INVALID" or "UNAUTHENTICATED"

**Problem:** Invalid or expired API key.

**Solution:**
1. Verify API key is correct
2. Check API key hasn't been revoked
3. Generate new key at [Google AI Studio](https://aistudio.google.com/)

### "QUOTA_EXCEEDED" or "RESOURCE_EXHAUSTED"

**Problem:** Rate limit or quota exceeded.

**Solution:**
1. The provider automatically retries with exponential backoff
2. Reduce request rate
3. Check quota limits at [Google AI Studio](https://aistudio.google.com/)
4. Consider upgrading to paid tier

### "Temperature must be between 0 and 2"

**Problem:** Invalid temperature value for Google AI.

**Solution:** Google AI uses 0.0-2.0 range (unlike some providers that use 0.0-1.0):
```csharp
configure: opts => opts.Temperature = 1.5  //  Valid (0.0-2.0)
// NOT: opts.Temperature = 2.5  // Invalid for Google AI
```

### "When ResponseSchema is set, ResponseMimeType must be 'application/json'"

**Problem:** Incompatible response format settings.

**Solution:** Set ResponseMimeType when using ResponseSchema:
```csharp
configure: opts =>
{
    opts.ResponseMimeType = "application/json"; // Required
    opts.ResponseSchema = "...";
}
```

### "ResponseSchema and ResponseJsonSchema cannot both be set"

**Problem:** Conflicting schema configuration.

**Solution:** Use one or the other, not both:
```csharp
// Option 1: Use ResponseSchema
configure: opts =>
{
    opts.ResponseMimeType = "application/json";
    opts.ResponseSchema = "...";
}

// Option 2: Use ResponseJsonSchema
configure: opts =>
{
    opts.ResponseMimeType = "application/json";
    opts.ResponseJsonSchema = "...";
}
```

### "SAFETY" or "BLOCKED" errors

**Problem:** Content blocked by safety filters.

**Solution:** Adjust safety settings or modify content:
```csharp
configure: opts =>
{
    opts.SafetySettings = new List<SafetySettingConfig>
    {
        new() { Category = "HARM_CATEGORY_HARASSMENT", Threshold = "BLOCK_ONLY_HIGH" }
    };
}
```

**Note:** Lowering safety thresholds may allow harmful content. Use responsibly.

### "FILE_TOO_LARGE" errors

**Problem:** Uploaded file exceeds size limit.

**Solution:**
1. Reduce file size (compress images/videos)
2. Split large files into smaller chunks
3. Use lower MediaResolution setting

### Model not responding or timing out

**Problem:** Long generation or network issues.

**Solution:** Reduce output length or complexity:
```csharp
configure: opts =>
{
    opts.MaxOutputTokens = 4096; // Reduce max tokens
    opts.Temperature = 0.7; // More focused responses
}
```

## ProviderOptionsJson Reference

When configuring via JSON file, all Google AI-specific options go inside the `providerOptionsJson` string. The keys are **camelCase** and map 1:1 to `GoogleAIProviderConfig` properties.

A complete example:
```json
{
    "name": "MyAgent",
    "provider": {
        "providerKey": "google-ai",
        "modelName": "gemini-2.0-flash",
        "apiKey": "your-api-key",
        "providerOptionsJson": "{\"maxOutputTokens\":8192,\"temperature\":0.7,\"topP\":0.95,\"topK\":40,\"seed\":42,\"responseMimeType\":\"application/json\"}"
    }
}
```

| JSON key | Type | Values / Range | Description |
|---|---|---|---|
| `maxOutputTokens` | int | ≥ 1 | Max tokens to generate |
| `temperature` | double | model-specific | Sampling randomness |
| `topP` | double | 0.0–1.0 | Nucleus sampling threshold |
| `topK` | int | ≥ 1 | Top-K sampling |
| `presencePenalty` | double | — | Penalize token reuse (binary) |
| `frequencyPenalty` | double | — | Penalize token reuse (proportional) |
| `stopSequences` | string[] | max 5 | Stop generation sequences |
| `seed` | int | any | Deterministic generation seed |
| `responseMimeType` | string | `"text/plain"`, `"application/json"`, `"text/x.enum"` | Output MIME type |
| `responseSchema` | string | — | OpenAPI schema as JSON string (requires `application/json`) |
| `responseJsonSchema` | string | — | JSON Schema as JSON string (alternative to `responseSchema`) |
| `responseModalities` | string[] | `"TEXT"`, `"IMAGE"`, `"AUDIO"` | Output modalities |
| `candidateCount` | int | 1 | Number of response candidates (currently must be 1) |
| `responseLogprobs` | bool | — | Include log probabilities in response |
| `logprobs` | int | — | Number of top logprobs per token (requires `responseLogprobs`) |
| `enableEnhancedCivicAnswers` | bool | — | Enhanced civic answer mode |
| `enableAffectiveDialog` | bool | — | Emotion-adaptive responses |
| `includeThoughts` | bool | — | Include thinking in response (Gemini 3+) |
| `thinkingBudget` | int | — | Thinking token budget (Gemini 3+) |
| `thinkingLevel` | string | `"LOW"`, `"HIGH"` | Thinking depth level (Gemini 3+) |
| `mediaResolution` | string | `"MEDIA_RESOLUTION_LOW"`, `"MEDIA_RESOLUTION_MEDIUM"`, `"MEDIA_RESOLUTION_HIGH"` | Input media resolution |
| `audioTimestamp` | bool | — | Include audio timestamps in request |
| `imageAspectRatio` | string | `"1:1"`, `"16:9"`, `"9:16"`, `"4:3"`, `"3:4"` | Image generation aspect ratio |
| `imageSize` | string | `"1K"`, `"2K"`, `"4K"` | Image output resolution |
| `imageOutputMimeType` | string | `"image/png"`, `"image/jpeg"` | Image output format |
| `imageCompressionQuality` | int | 0–100 | JPEG compression quality (default: 75) |
| `modelRoutingPreference` | string | `"PRIORITIZE_QUALITY"`, `"BALANCED"`, `"PRIORITIZE_COST"` | Auto routing preference |
| `manualRoutingModelName` | string | — | Specific model for manual routing |
| `safetySettings` | object[] | — | Safety category/threshold pairs |
| `functionCallingMode` | string | `"AUTO"`, `"ANY"`, `"NONE"` | Function calling behavior |
| `allowedFunctionNames` | string[] | — | Allowed functions (when mode is `"ANY"`) |

---

## GoogleAIProviderConfig API Reference

### Core Parameters

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `MaxOutputTokens` | `int?` | ≥ 1 | Model-specific | Maximum tokens to generate |

### Sampling Parameters

| Property | Type | Range | Default | Description |
|----------|------|-------|---------|-------------|
| `Temperature` | `double?` | 0.0-2.0 | Model-specific | Sampling temperature |
| `TopP` | `double?` | 0.0-1.0 | Model-specific | Nucleus sampling threshold |
| `TopK` | `int?` | > 0 | Model-specific | Top-K sampling |
| `PresencePenalty` | `double?` | - | - | Binary penalty for token reuse |
| `FrequencyPenalty` | `double?` | - | - | Proportional penalty for token reuse |
| `StopSequences` | `List<string>?` | Max 5 | - | Stop generation sequences |

### Determinism

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Seed` | `int?` | - | Seed for deterministic generation |

### Response Format

| Property | Type | Values | Default | Description |
|----------|------|--------|---------|-------------|
| `ResponseMimeType` | `string?` | "text/plain", "application/json", "text/x.enum" | "text/plain" | Response format |
| `ResponseSchema` | `string?` | JSON schema | - | OpenAPI schema for structured output |
| `ResponseJsonSchema` | `string?` | JSON schema | - | Alternative JSON schema (mutually exclusive with ResponseSchema) |
| `ResponseModalities` | `List<string>?` | ["TEXT", "IMAGE", etc.] | - | Requested response modalities |
| `CandidateCount` | `int?` | 1 | 1 | Number of response candidates (currently only 1) |

### Advanced Features

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ResponseLogprobs` | `bool?` | `false` | Export logprobs in response |
| `Logprobs` | `int?` | - | Number of top logprobs (requires ResponseLogprobs) |
| `EnableEnhancedCivicAnswers` | `bool?` | `false` | Enhanced civic answer mode |
| `EnableAffectiveDialog` | `bool?` | `false` | Emotion detection and adaptation |

### Thinking Configuration (Gemini 3+)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IncludeThoughts` | `bool?` | `false` | Include reasoning process in response |
| `ThinkingBudget` | `int?` | - | Token budget for thinking |
| `ThinkingLevel` | `string?` | - | "LOW" or "HIGH" reasoning depth |

### Media & Image Configuration

| Property | Type | Values | Default | Description |
|----------|------|--------|---------|-------------|
| `MediaResolution` | `string?` | "MEDIA_RESOLUTION_LOW", "MEDIA_RESOLUTION_MEDIUM", "MEDIA_RESOLUTION_HIGH" | - | Input media resolution |
| `AudioTimestamp` | `bool?` | - | - | Include audio timestamps |
| `ImageAspectRatio` | `string?` | "1:1", "16:9", "9:16", "4:3", "3:4" | - | Generated image aspect ratio |
| `ImageSize` | `string?` | "1K", "2K", "4K" | - | Generated image resolution |
| `ImageOutputMimeType` | `string?` | "image/png", "image/jpeg" | "image/png" | Generated image format |
| `ImageCompressionQuality` | `int?` | 0-100 | 75 | JPEG compression quality |

### Routing Configuration

| Property | Type | Values | Default | Description |
|----------|------|--------|---------|-------------|
| `ModelRoutingPreference` | `string?` | "PRIORITIZE_QUALITY", "BALANCED", "PRIORITIZE_COST" | - | Automated routing preference |
| `ManualRoutingModelName` | `string?` | - | - | Specific model for manual routing |

### Safety Settings

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SafetySettings` | `List<SafetySettingConfig>?` | - | Content filtering configuration |

**SafetySettingConfig Properties:**
- `Category` (string) - Harm category (e.g., "HARM_CATEGORY_HARASSMENT")
- `Threshold` (string) - Blocking threshold (e.g., "BLOCK_MEDIUM_AND_ABOVE")
- `Method` (string) - Block method: "SEVERITY" or "PROBABILITY"

### Function Calling Configuration

| Property | Type | Values | Default | Description |
|----------|------|--------|---------|-------------|
| `FunctionCallingMode` | `string?` | "AUTO", "ANY", "NONE" | "AUTO" | Function calling behavior |
| `AllowedFunctionNames` | `List<string>?` | - | - | Allowed functions (mode = "ANY" only) |

### Additional Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AdditionalProperties` | `Dictionary<string, object>?` | - | Custom model-specific parameters |

## Additional Resources

- [Google AI Documentation](https://ai.google.dev/)
- [Gemini API Documentation](https://ai.google.dev/gemini-api/docs)
- [Google AI Studio](https://aistudio.google.com/) - Get API keys and test models
- [Model Documentation](https://ai.google.dev/gemini-api/docs/models/gemini)
- [Google AI Pricing](https://ai.google.dev/pricing)
- [Safety Settings Guide](https://ai.google.dev/gemini-api/docs/safety-settings)
- [Structured Output Guide](https://ai.google.dev/gemini-api/docs/json-mode)
- [Function Calling Guide](https://ai.google.dev/gemini-api/docs/function-calling)
