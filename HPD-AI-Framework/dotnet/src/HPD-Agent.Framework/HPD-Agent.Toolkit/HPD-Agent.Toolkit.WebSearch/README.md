
The WebSearch Toolkit provides intelligent web search capabilities to HPD-Agent with support for multiple search providers, type-safe configuration, and AOT-compatible implementation.

## 🎯 Features

- **Multiple Provider Support**: Tavily AI, Brave Search, and Bing (via Semantic Kernel)
- **Intelligent Function Generation**: Only generates functions that are supported by configured providers
- **Type-Safe Configuration**: Fluent builders with compile-time safety
- **AOT Compatible**: Full native compilation support with source-generated JSON serialization
- **Conditional Functions**: Functions appear/disappear based on provider capabilities
- **Dynamic Descriptions**: Function descriptions adapt to available providers

## 🚀 Quick Start

### Basic Setup with Tavily AI

```csharp
var agent = AgentBuilder.Create()
    .WithInstructions("You are a helpful research assistant.")
    .WithWebSearchProvider(tavily => tavily
        .WithApiKey("your-tavily-api-key")
        .ForResearchMode()) // AI answers + raw content
    .WithWebSearchToolkit()
    .Build();
```

Available AI Functions:
- `WebSearch(query, count?, provider?)` - Search the web with AI answers
- `NewsSearch(query, timeRange?, provider?)` - Search for recent news
- `AnswerSearch(query)` - Get AI-generated answers with sources

### Multi-Provider Setup

```csharp
var agent = AgentBuilder.Create()
    .WithWebSearchProvider(tavily => tavily
        .WithApiKey("tavily-key")
        .ForResearchMode())
    .WithWebSearchProvider(brave => brave
        .WithApiKey("brave-key")
        .ForDeveloperSearch())
    .WithWebSearchProvider(bing => bing
        .WithApiKey("bing-key")
        .ForEnterpriseSearch())
    .WithWebSearchToolkit(defaultProvider: "tavily")
    .Build();
```

Available AI Functions (all providers):
- `WebSearch(query, count?, provider?)` - Provider options: "tavily, brave, bing"
- `NewsSearch(query, timeRange?, provider?)` - News search across providers
- `VideoSearch(query, count?, provider?)` - Video search (Brave recommended)
- `AnswerSearch(query)` - AI answers (Tavily only)
- `ShoppingSearch(query, count?)` - Shopping search (Bing only)

## 📋 Provider Comparison

| Feature | Tavily AI | Brave Search | Bing (SK) |
|---------|-----------|--------------|-----------|
| Web Search |  |  |  |
| News Search |  |  |  |
| Video Search |    |  |  |
| Shopping Search |    |    |  |
| AI Answers |  |    |    |
| Raw Content |  |    |    |
| Privacy Focus |    |  |    |

##  Configuration Examples

### Research Assistant (Tavily AI)

```csharp
.WithWebSearchProvider(tavily => tavily
    .WithApiKey("your-key")
    .WithSearchDepth(TavilySearchDepth.Advanced)
    .IncludeAnswers(true)
    .IncludeRawContent(true)
    .WithTopic(TavilyTopic.General)
    .WithTimeFrame(TavilyTimeFrame.Week)
    .WithChunksPerSource(3))
```

**Generated Functions:**
- WebSearch: "Search the web using tavily with AI answers available"
- NewsSearch: "Search for recent news with AI summaries available"
- AnswerSearch: "Get AI-generated answers with cited sources using Tavily"

### Privacy-Focused Assistant (Brave)

```csharp
.WithWebSearchProvider(brave => brave
    .WithApiKey("your-key")
    .WithSafeSearch(BraveSafeSearch.Strict)
    .WithCountry(null) // No geo-targeting
    .WithLanguage("en")
    .EnableFreshness(false)
    .EnableSpellCheck(false)
    .EnableExtraSnippets(false))
```

**Generated Functions:**
- WebSearch: "Search the web using brave"
- NewsSearch: "Search for recent news"
- VideoSearch: "Search for videos using brave (recommended)"

### Enterprise Assistant (Bing)

```csharp
.WithWebSearchProvider(bing => bing
    .WithApiKey("your-key")
    .WithCustomConfig("your-config-id")
    .WithMarket("en-US")
    .WithSafeSearch(BingSafeSearch.Moderate)
    .EnableResponseFilter(BingResponseFilter.WebPages | BingResponseFilter.News)
    .EnableAnswers(true)
    .EnableShopping(true))
```

**Generated Functions:**
- WebSearch: "Search the web using bing"
- NewsSearch: "Search for recent news"
- VideoSearch: "Search for videos"
- ShoppingSearch: "Search for shopping deals and product prices using Bing Shopping"

## 🏗️ Architecture

### Core Components

```
WebSearch/
├── IWebSearchConnector.cs          # Core search interface
├── Models/
│   └── SearchModels.cs             # SearchResult, AnswerResult models
├── Context/
│   └── WebSearchContext.cs         # IToolkitMetadata implementation
├── Builders/
│   ├── IWebSearchProviderBuilders.cs  # Type-safe builder interfaces
│   ├── TavilyWebSearchBuilder.cs       # Tavily-specific builder
│   ├── BraveWebSearchBuilder.cs        # Brave-specific builder (Phase 2)
│   └── BingWebSearchBuilder.cs         # Bing-specific builder (Phase 2)
├── Connectors/
│   ├── TavilyConnector.cs              # Tavily API implementation
│   ├── TavilyJsonContext.cs            # AOT-compatible JSON context
│   ├── BraveConnector.cs               # Brave API implementation (Phase 2)
│   └── BingConnector.cs                # Bing wrapper (Phase 2)
├── Toolkit/
│   └── WebSearchToolkit.cs              # Main Toolkit with conditional functions
├── Extensions/
│   └── AgentBuilderExtensions.cs       # Fluent configuration API
└── Examples/
    └── WebSearchExamples.cs             # Usage examples
```

### Conditional Function System

The Toolkit uses HPD-Agent's V2 conditional function system with type-safe expressions to generate only relevant AI functions:

```csharp
[ConditionalFunction<WebSearchContext>("HasTavilyProvider || HasBraveProvider || HasBingProvider")]
[Description("Search the web using {metadata.DefaultProvider}")]
public async Task<string> WebSearchAsync(
    [Description("Search query")] string query,
    [Description("Number of results (1-20)")] int count = 10,
    [Description("Provider: {metadata.ConfiguredProviders} (optional, defaults to {metadata.DefaultProvider})")] string? provider = null)
```

**V2 Benefits:**
-  Compile-time validation of context properties  
-  Full IntelliSense support for conditions
-  Type-safe property access

### Provider Detection Logic

```csharp
public class WebSearchContext : IToolkitMetadata
{
    public bool HasProvider(string providerName) =>
        _connectors.ContainsKey(providerName.ToLowerInvariant());
    
    public T GetProperty<T>(string propertyName, T defaultValue = default) => propertyName switch
    {
        "DefaultProvider" => (T)(object)(_defaultProvider ?? "none"),
        "ConfiguredProviders" => (T)(object)string.Join(", ", _connectors.Keys),
        _ => defaultValue
    };
}
```

## 🔄 Fluent Builder Patterns

### Preset Configurations

```csharp
// Research Mode (Tavily)
.ForResearchMode() // Advanced search, AI answers, raw content, 3 chunks

// News Mode (Tavily)
.ForNewsMode() // News topic, week timeframe, AI answers

// Privacy Mode (Brave)
.ForPrivacyFocusedSearch() // Strict safe search, no geo-targeting

// Developer Mode (Brave)
.ForDeveloperSearch() // Web + news, extra snippets, US/English

// Enterprise Mode (Bing)
.ForEnterpriseSearch() // Custom config, shopping, answers enabled
```

### Custom Configurations

```csharp
.WithWebSearchProvider(tavily => tavily
    .WithApiKey("key")
    .WithSearchDepth(TavilySearchDepth.Basic)
    .IncludeAnswers(false)
    .IncludeRawContent(true)
    .WithTopic(TavilyTopic.News)
    .WithTimeFrame(TavilyTimeFrame.Day)
    .WithChunksPerSource(1))
```

## 🎛️ Provider Configuration Options

### Tavily AI Configuration

```csharp
public interface ITavilyWebSearchBuilder
{
    ITavilyWebSearchBuilder WithApiKey(string apiKey);
    ITavilyWebSearchBuilder WithSearchDepth(TavilySearchDepth depth);
    ITavilyWebSearchBuilder IncludeAnswers(bool include);
    ITavilyWebSearchBuilder IncludeRawContent(bool include);
    ITavilyWebSearchBuilder WithTopic(TavilyTopic topic);
    ITavilyWebSearchBuilder WithTimeFrame(TavilyTimeFrame timeFrame);
    ITavilyWebSearchBuilder WithChunksPerSource(int chunks);
    
    // Preset configurations
    ITavilyWebSearchBuilder ForResearchMode();
    ITavilyWebSearchBuilder ForNewsMode();
}
```

### Brave Search Configuration

```csharp
public interface IBraveWebSearchBuilder
{
    IBraveWebSearchBuilder WithApiKey(string apiKey);
    IBraveWebSearchBuilder WithSafeSearch(BraveSafeSearch safeSearch);
    IBraveWebSearchBuilder WithCountry(string? country);
    IBraveWebSearchBuilder WithLanguage(string language);
    IBraveWebSearchBuilder EnableFreshness(bool enable);
    IBraveWebSearchBuilder EnableSpellCheck(bool enable);
    IBraveWebSearchBuilder EnableExtraSnippets(bool enable);
    
    // Preset configurations
    IBraveWebSearchBuilder ForPrivacyFocusedSearch();
    IBraveWebSearchBuilder ForDeveloperSearch();
}
```

### Bing Search Configuration

```csharp
public interface IBingWebSearchBuilder
{
    IBingWebSearchBuilder WithApiKey(string apiKey);
    IBingWebSearchBuilder WithCustomConfig(string customConfigId);
    IBingWebSearchBuilder WithMarket(string market);
    IBingWebSearchBuilder WithSafeSearch(BingSafeSearch safeSearch);
    IBingWebSearchBuilder EnableResponseFilter(BingResponseFilter filter);
    IBingWebSearchBuilder EnableAnswers(bool enable);
    IBingWebSearchBuilder EnableShopping(bool enable);
    
    // Preset configurations
    IBingWebSearchBuilder ForEnterpriseSearch();
}
```

## 🔌 Integration with HPD-Agent

### Toolkit Registration

The WebSearch Toolkit integrates seamlessly with HPD-Agent's Toolkit system:

```csharp
.WithWebSearchToolkit(defaultProvider: "tavily")
```

This registration:
1. Analyzes configured providers and their capabilities
2. Generates appropriate conditional functions
3. Creates dynamic function descriptions
4. Registers the Toolkit with the agent

### Source Generator Integration

The Toolkit works with HPD-Agent's source generator for:
- Conditional function evaluation
- DSL expression parsing
- Function description generation
- Compile-time validation

## ⚡ Performance & AOT Compatibility

### Native AOT Support

- **Source-Generated JSON**: All JSON serialization uses `JsonSerializerContext`
- **No Reflection**: All operations are AOT-compatible
- **Minimal Runtime**: Optimized for fast startup and low memory

### JSON Context Example

```csharp
[JsonSerializable(typeof(TavilySearchRequest))]
[JsonSerializable(typeof(TavilySearchResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal partial class TavilyJsonContext : JsonSerializerContext
{
}

// Usage
var json = JsonSerializer.Serialize(request, TavilyJsonContext.Default.TavilySearchRequest);
var response = JsonSerializer.Deserialize(json, TavilyJsonContext.Default.TavilySearchResponse);
```

## 🧪 Testing

### Example Usage

```csharp
// Create a research assistant
var agent = WebSearchExamples.CreateResearchAssistant("tavily-api-key");

// The agent will have these AI functions available:
// - WebSearch("quantum computing breakthroughs", 5)
// - NewsSearch("AI developments", "week") 
// - AnswerSearch("What is quantum supremacy?")
```

### Expected Function Behavior

```csharp
// Web search with provider selection
await agent.InvokeAsync("Search for recent developments in quantum computing using Tavily");
// Calls: WebSearch("quantum computing developments", 10, "tavily")

// News search with time filtering
await agent.InvokeAsync("Find news about AI from this week");
// Calls: NewsSearch("AI news", "week")

// AI-powered answers
await agent.InvokeAsync("What are the main benefits of quantum computing?");
// Calls: AnswerSearch("benefits of quantum computing")
```

## 📈 Roadmap

### Phase 1: Core Infrastructure  
- [x] Base interfaces and models
- [x] Tavily connector implementation
- [x] Toolkit with conditional functions
- [x] AgentBuilder extensions
- [x] AOT compatibility

### Phase 2: Provider Expansion 🚧
- [ ] Brave Search connector
- [ ] Bing Search connector wrapper
- [ ] Enhanced source generator support
- [ ] Provider-specific optimizations

### Phase 3: Advanced Features 📋
- [ ] Caching and rate limiting
- [ ] Search result ranking
- [ ] Custom search templates
- [ ] Analytics and monitoring

##  Development

### Building the Project

```bash
cd HPD-Agent
dotnet build
```

### Running Tests

```bash
dotnet test
```

### Adding a New Provider

1. Implement `IWebSearchConnector`
2. Create provider-specific models with `JsonSerializerContext`
3. Add fluent builder interface and implementation
4. Create connector class with AOT-compatible JSON handling
5. Add AgentBuilder extension methods
6. Update conditional function logic

## 📄 License

This WebSearch Toolkit is part of the HPD-Agent project and follows the same licensing terms.
