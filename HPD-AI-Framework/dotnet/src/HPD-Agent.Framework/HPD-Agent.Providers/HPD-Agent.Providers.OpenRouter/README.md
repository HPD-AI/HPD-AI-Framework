# HPD-Agent.Providers.OpenRouter

This package provides an integration with [OpenRouter](https://openrouter.ai/), a service that provides access to a wide variety of large language models.

## Configuration

To use the OpenRouter provider, configure it in your `AgentConfig`. You must provide an API key.

### C# Configuration

```csharp
var config = new AgentConfig
{
    Provider = new ProviderConfig
    {
        ProviderKey = "openrouter",
        ModelName = "google/gemini-flash-1.5", // Specify any model available on OpenRouter
        ApiKey = "YOUR_OPENROUTER_API_KEY",
        AdditionalProperties = new()
        {
            ["HttpReferer"] = "https://your-app.com", // Optional: Your app's URL for OpenRouter analytics
            ["AppName"] = "My Awesome Agent" // Optional: Your app's name for OpenRouter analytics
        }
    }
};
```

### JSON Configuration (`appsettings.json`)

```json
{
  "Agent": {
    "Provider": {
      "ProviderKey": "openrouter",
      "ModelName": "google/gemini-flash-1.5",
      "ApiKey": "YOUR_OPENROUTER_API_KEY",
      "AdditionalProperties": {
        "HttpReferer": "https://your-app.com",
        "AppName": "My Awesome Agent"
      }
    }
  }
}
```

### Configuration Options

The following properties can be set via the `AdditionalProperties` dictionary:

| Key           | Type   | Description                                                                                                |
|---------------|--------|------------------------------------------------------------------------------------------------------------|
| `HttpReferer` | string | Optional. Your application's URL, which is sent to OpenRouter for analytics and to identify your app. Defaults to the HPD-Agent GitHub repo if not set. |
| `AppName`     | string | Optional. Your application's name, also used for OpenRouter analytics. Defaults to "HPD-Agent" if not set. |
