using HPD.Agent;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Bedrock;

/// <summary>
/// Extension methods for AgentBuilder to configure AWS Bedrock as the AI provider.
/// </summary>
public static class AgentBuilderExtensions
{
    /// <summary>
    /// Configures the agent to use AWS Bedrock as the AI provider.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="model">The Bedrock model ID (e.g., "anthropic.claude-3-5-sonnet-20241022-v2:0", "meta.llama3-70b-instruct-v1:0")</param>
    /// <param name="region">AWS region where Bedrock is hosted (e.g., "us-east-1", "us-west-2")</param>
    /// <param name="configure">Optional action to configure additional Bedrock-specific options</param>
    /// <param name="clientFactory">Optional factory to wrap the chat client with middleware (logging, caching, etc.)</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// <para>
    /// Region Resolution (in priority order):
    /// 1. Explicit region parameter
    /// 2. BedrockProviderConfig.Region (via configure action)
    /// 3. Environment variable: AWS_REGION or AWS_DEFAULT_REGION
    /// 4. AWS credentials file (~/.aws/config)
    /// </para>
    /// <para>
    /// Credential Resolution (AWS Default Credential Chain):
    /// 1. Environment variables: AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_SESSION_TOKEN
    /// 2. AWS credentials file (~/.aws/credentials)
    /// 3. IAM role (for EC2, ECS, Lambda, etc.)
    /// 4. Explicit credentials via configure action
    /// </para>
    /// <para>
    /// This method creates a <see cref="BedrockProviderConfig"/> that is:
    /// - Stored in <c>ProviderConfig.ProviderOptionsJson</c> for FFI/JSON serialization
    /// - Applied during <c>BedrockProvider.CreateChatClient()</c> via the registered deserializer
    /// </para>
    /// <para>
    /// For FFI/JSON configuration, you can use the same config structure directly:
    /// <code>
    /// {
    ///   "Provider": {
    ///     "ProviderKey": "bedrock",
    ///     "ModelName": "anthropic.claude-3-5-sonnet-20241022-v2:0",
    ///     "ProviderOptionsJson": "{\"region\":\"us-east-1\",\"maxTokens\":4096,\"temperature\":0.7}"
    ///   }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Option 1: With explicit region and configuration
    /// var agent = await new AgentBuilder()
    ///     .WithBedrock(
    ///         model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
    ///         region: "us-east-1",
    ///         configure: opts =>
    ///         {
    ///             opts.MaxTokens = 4096;
    ///             opts.Temperature = 0.7f;
    ///         })
    ///     .Build();
    ///
    /// // Option 2: With AWS credentials
    /// var agent = await new AgentBuilder()
    ///     .WithBedrock(
    ///         model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
    ///         region: "us-east-1",
    ///         configure: opts =>
    ///         {
    ///             opts.AccessKeyId = "YOUR_ACCESS_KEY";
    ///             opts.SecretAccessKey = "YOUR_SECRET_KEY";
    ///             opts.MaxTokens = 4096;
    ///         })
    ///     .Build();
    ///
    /// // Option 3: With AWS profile
    /// var agent = await new AgentBuilder()
    ///     .WithBedrock(
    ///         model: "meta.llama3-70b-instruct-v1:0",
    ///         region: "us-west-2",
    ///         configure: opts =>
    ///         {
    ///             opts.ProfileName = "my-aws-profile";
    ///             opts.Temperature = 0.8f;
    ///             opts.TopP = 0.9f;
    ///         })
    ///     .Build();
    ///
    /// // Option 4: With guardrails
    /// var agent = await new AgentBuilder()
    ///     .WithBedrock(
    ///         model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
    ///         region: "us-east-1",
    ///         configure: opts =>
    ///         {
    ///             opts.GuardrailIdentifier = "my-guardrail-id";
    ///             opts.GuardrailVersion = "1";
    ///             opts.GuardrailTrace = "enabled";
    ///         })
    ///     .Build();
    ///
    /// // Option 5: With stop sequences
    /// var agent = await new AgentBuilder()
    ///     .WithBedrock(
    ///         model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
    ///         region: "us-east-1",
    ///         configure: opts =>
    ///         {
    ///             opts.StopSequences = new List&lt;string&gt; { "\n\n", "END", "---" };
    ///             opts.MaxTokens = 8192;
    ///         })
    ///     .Build();
    ///
    /// // Option 6: With middleware via ClientFactory
    /// var agent = await new AgentBuilder()
    ///     .WithBedrock(
    ///         model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
    ///         region: "us-east-1",
    ///         configure: opts => opts.MaxTokens = 4096,
    ///         clientFactory: client => new LoggingChatClient(client, logger))
    ///     .Build();
    ///
    /// // Option 7: Auto-resolve from environment variables
    /// // Set AWS_REGION, AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY
    /// var agent = await new AgentBuilder()
    ///     .WithBedrock(model: "anthropic.claude-3-5-sonnet-20241022-v2:0")
    ///     .Build();
    ///
    /// // Option 8: With prompt caching (Claude 3.5+)
    /// var agent = await new AgentBuilder()
    ///     .WithBedrock(
    ///         model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
    ///         region: "us-east-1",
    ///         configure: opts =>
    ///         {
    ///             opts.EnablePromptCaching = true;
    ///             opts.MaxTokens = 4096;
    ///         })
    ///     .Build();
    ///
    /// // Option 9: With custom endpoint (VPC endpoint)
    /// var agent = await new AgentBuilder()
    ///     .WithBedrock(
    ///         model: "anthropic.claude-3-5-sonnet-20241022-v2:0",
    ///         region: "us-east-1",
    ///         configure: opts =>
    ///         {
    ///             opts.ServiceUrl = "https://vpce-xxx.bedrock-runtime.us-east-1.vpce.amazonaws.com";
    ///         })
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithBedrock(
        this AgentBuilder builder,
        string model,
        string? region = null,
        Action<BedrockProviderConfig>? configure = null,
        Func<IChatClient, IChatClient>? clientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model ID is required for AWS Bedrock provider.", nameof(model));

        // Create provider config
        var providerConfig = new BedrockProviderConfig();

        // Set region if provided
        if (!string.IsNullOrWhiteSpace(region))
        {
            providerConfig.Region = region;
        }

        // Allow user to configure additional options
        configure?.Invoke(providerConfig);

        // Validate configuration
        ValidateProviderConfig(providerConfig, model, configure);

        // Build provider config
        builder.Config.Provider = new ProviderConfig
        {
            ProviderKey = "bedrock",
            ModelName = model
        };

        // Store the typed config
        builder.Config.Provider.SetTypedProviderConfig(providerConfig);

        // Store the client factory if provided
        if (clientFactory != null)
        {
            // Store in AdditionalProperties for the provider to retrieve during CreateChatClient
            builder.Config.Provider.AdditionalProperties ??= new Dictionary<string, object>();
            builder.Config.Provider.AdditionalProperties["ClientFactory"] = clientFactory;
        }

        return builder;
    }

    private static void ValidateProviderConfig(BedrockProviderConfig config, string model, Action<BedrockProviderConfig>? configure)
    {
        // Validate Temperature range
        if (config.Temperature.HasValue && (config.Temperature.Value < 0 || config.Temperature.Value > 1))
        {
            throw new ArgumentException(
                "Temperature must be between 0 and 1 for AWS Bedrock.",
                nameof(configure));
        }

        // Validate TopP range
        if (config.TopP.HasValue && (config.TopP.Value < 0 || config.TopP.Value > 1))
        {
            throw new ArgumentException(
                "TopP must be between 0 and 1 for AWS Bedrock.",
                nameof(configure));
        }

        // Validate MaxTokens minimum
        if (config.MaxTokens.HasValue && config.MaxTokens.Value < 1)
        {
            throw new ArgumentException(
                "MaxTokens must be at least 1.",
                nameof(configure));
        }

        // Validate StopSequences count
        if (config.StopSequences != null && config.StopSequences.Count > 2500)
        {
            throw new ArgumentException(
                "StopSequences cannot exceed 2500 items.",
                nameof(configure));
        }

        // Validate ToolChoice
        if (!string.IsNullOrEmpty(config.ToolChoice))
        {
            var validChoices = new[] { "auto", "any", "tool" };
            if (!Array.Exists(validChoices, c => c == config.ToolChoice))
            {
                throw new ArgumentException(
                    "ToolChoice must be one of: auto, any, tool.",
                    nameof(configure));
            }

            // Validate ToolChoiceName requirement
            if (config.ToolChoice == "tool" && string.IsNullOrEmpty(config.ToolChoiceName))
            {
                throw new ArgumentException(
                    "ToolChoiceName is required when ToolChoice is 'tool'.",
                    nameof(configure));
            }
        }

        // Validate Guardrail configuration
        if (!string.IsNullOrEmpty(config.GuardrailIdentifier) && string.IsNullOrEmpty(config.GuardrailVersion))
        {
            throw new ArgumentException(
                "GuardrailVersion is required when GuardrailIdentifier is specified.",
                nameof(configure));
        }

        // Validate GuardrailTrace
        if (!string.IsNullOrEmpty(config.GuardrailTrace))
        {
            var validTraceOptions = new[] { "enabled", "disabled" };
            if (!Array.Exists(validTraceOptions, t => t == config.GuardrailTrace))
            {
                throw new ArgumentException(
                    "GuardrailTrace must be either 'enabled' or 'disabled'.",
                    nameof(configure));
            }
        }

        // Validate credentials pairing
        if (!string.IsNullOrEmpty(config.AccessKeyId) && string.IsNullOrEmpty(config.SecretAccessKey))
        {
            throw new ArgumentException(
                "SecretAccessKey is required when AccessKeyId is specified.",
                nameof(configure));
        }

        if (!string.IsNullOrEmpty(config.SecretAccessKey) && string.IsNullOrEmpty(config.AccessKeyId))
        {
            throw new ArgumentException(
                "AccessKeyId is required when SecretAccessKey is specified.",
                nameof(configure));
        }
    }
}
