# HPD-Agent.Providers.Bedrock

This package provides an integration with Amazon Bedrock.

## Configuration

To use the Bedrock provider, configure it in your `AgentConfig`. You can do this either programmatically in C# or through a JSON configuration file.

### C# Configuration

```csharp
var config = new AgentConfig
{
    Provider = new ProviderConfig
    {
        ProviderKey = "bedrock",
        ModelName = "anthropic.claude-3-sonnet-20240229-v1:0", // Or any other Bedrock model
        AdditionalProperties = new()
        {
            ["Region"] = "us-east-1",
            ["AccessKeyId"] = "YOUR_AWS_ACCESS_KEY_ID", // Optional: uses environment variables if omitted
            ["SecretAccessKey"] = "YOUR_AWS_SECRET_ACCESS_KEY" // Optional: uses environment variables if omitted
        }
    }
};
```

### JSON Configuration (`appsettings.json`)

```json
{
  "Agent": {
    "Provider": {
      "ProviderKey": "bedrock",
      "ModelName": "anthropic.claude-3-sonnet-20240229-v1:0",
      "AdditionalProperties": {
        "Region": "us-east-1",
        "AccessKeyId": "YOUR_AWS_ACCESS_KEY_ID",
        "SecretAccessKey": "YOUR_AWS_SECRET_ACCESS_KEY"
      }
    }
  }
}
```

### Configuration Options

The following properties can be set via the `AdditionalProperties` dictionary:

| Key               | Type   | Description                                                                                                |
|-------------------|--------|------------------------------------------------------------------------------------------------------------|
| `Region`          | string | **Required.** The AWS Region where the Bedrock service is hosted (e.g., "us-east-1"). Can also be set via the `AWS_REGION` environment variable. |
| `AccessKeyId`     | string | Optional. Your AWS Access Key ID. If not provided, the SDK will use the default credential chain. Can also be set via `AWS_ACCESS_KEY_ID`. |
| `SecretAccessKey` | string | Optional. Your AWS Secret Access Key. If not provided, the SDK will use the default credential chain. Can also be set via `AWS_SECRET_ACCESS_KEY`. |
