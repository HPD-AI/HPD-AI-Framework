using System.Text.Json;
using Xunit;
using FluentAssertions;
using HPD.Agent.MCP;
using HPD.Agent.Secrets;

namespace HPD.Agent.Tests.MCPServer;

/// <summary>
/// Unit tests for MCPServerConfig.
/// </summary>
public class MCPServerConfigTests
{
    [Fact]
    public void ParentToolHarness_HasJsonIgnore_NotSerialized()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Transport = "stdio",
            Command = "node",
            Arguments = new List<string> { "test.js" },
            ParentToolHarness = "MyToolHarness"
        };

        var json = JsonSerializer.Serialize(config, MCPJsonSerializerContext.Default.MCPServerConfig);

        json.Should().NotContain("ParentToolHarness");
        json.Should().NotContain("MyToolHarness");
    }

    [Fact]
    public void CollapseWithinToolHarness_HasJsonIgnore_NotSerialized()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Transport = "stdio",
            Command = "node",
            Arguments = new List<string> { "test.js" },
            CollapseWithinToolHarness = true
        };

        var json = JsonSerializer.Serialize(config, MCPJsonSerializerContext.Default.MCPServerConfig);

        json.Should().NotContain("CollapseWithinToolHarness");
    }

    [Fact]
    public void StdioJsonDeserialization_UsesTransportBasedSchema()
    {
        var json = @"{
            ""name"": ""filesystem"",
            ""transport"": ""stdio"",
            ""command"": ""npx"",
            ""arguments"": [""@modelcontextprotocol/server-filesystem"", ""/tmp""],
            ""workingDirectory"": ""/workspace"",
            ""inheritEnvironmentVariables"": false,
            ""useDefaultEnvironmentVariables"": true,
            ""environment"": {
                ""FILESYSTEM_ROOT"": ""/tmp"",
                ""REMOVE_ME"": null
            },
            ""environmentSecretKeys"": {
                ""GITHUB_TOKEN"": ""mcp:filesystem:GitHubToken""
            },
            ""connectionTimeoutMs"": 45000,
            ""initializationTimeoutMs"": 60000,
            ""shutdownTimeoutMs"": 8000,
            ""clientName"": ""hpd-agent"",
            ""clientVersion"": ""1.2.3"",
            ""protocolVersion"": ""2026-07-28"",
            ""enableResources"": true,
            ""maxResourceListResults"": 50,
            ""maxResourceContentLength"": 12345,
            ""enablePrompts"": true,
            ""maxPromptListResults"": 25,
            ""maxPromptContentLength"": 6789,
            ""processIsolation"": {
                ""mode"": ""Isolated"",
                ""profile"": ""filesystem-only"",
                ""allowWrite"": ["".""],
                ""denyRead"": [""~/.ssh""],
                ""networkMode"": ""Blocked""
            }
        }";

        var config = JsonSerializer.Deserialize<MCPServerConfig>(json, MCPJsonSerializerContext.Default.MCPServerConfig);

        config.Should().NotBeNull();
        config!.Name.Should().Be("filesystem");
        config.Transport.Should().Be("stdio");
        config.Command.Should().Be("npx");
        config.Arguments.Should().Contain("@modelcontextprotocol/server-filesystem");
        config.WorkingDirectory.Should().Be("/workspace");
        config.InheritEnvironmentVariables.Should().BeFalse();
        config.UseDefaultEnvironmentVariables.Should().BeTrue();
        config.Environment.Should().ContainKey("FILESYSTEM_ROOT").WhoseValue.Should().Be("/tmp");
        config.Environment.Should().ContainKey("REMOVE_ME").WhoseValue.Should().BeNull();
        config.EnvironmentSecretKeys.Should().ContainKey("GITHUB_TOKEN").WhoseValue.Should().Be("mcp:filesystem:GitHubToken");
        config.ConnectionTimeoutMs.Should().Be(45000);
        config.InitializationTimeoutMs.Should().Be(60000);
        config.ShutdownTimeoutMs.Should().Be(8000);
        config.ClientName.Should().Be("hpd-agent");
        config.ClientVersion.Should().Be("1.2.3");
        config.ProtocolVersion.Should().Be("2026-07-28");
        config.EnableResources.Should().BeTrue();
        config.MaxResourceListResults.Should().Be(50);
        config.MaxResourceContentLength.Should().Be(12345);
        config.EnablePrompts.Should().BeTrue();
        config.MaxPromptListResults.Should().Be(25);
        config.MaxPromptContentLength.Should().Be(6789);
        config.InvocationModePolicy.Should().Be(AgentInvocationModePolicy.SynchronousOnly);
        config.BackgroundNotification.Should().BeOfType<BackgroundTaskNotificationRule.OnFinalStateRule>();
        config.ProcessIsolation.Should().NotBeNull();
        config.ProcessIsolation!.Profile.Should().Be("filesystem-only");
        config.ProcessIsolation.AllowWrite.Should().Contain(".");
        config.ProcessIsolation.DenyRead.Should().Contain("~/.ssh");
        config.Invoking(c => c.Validate()).Should().NotThrow();

        // ToolHarness-awareness fields should have defaults
        config.ParentToolHarness.Should().BeNull();
        config.CollapseWithinToolHarness.Should().BeFalse();
    }

    [Fact]
    public void JsonDeserialization_WithInvocationModePolicy_DeserializesBackgroundSettings()
    {
        var json = @"{
            ""name"": ""filesystem"",
            ""transport"": ""stdio"",
            ""command"": ""npx"",
            ""invocationModePolicy"": ""modelChoice"",
            ""toolInvocationModePolicies"": {
                ""read_file"": ""synchronousOnly"",
                ""long_running_search"": ""backgroundOnly""
            },
            ""backgroundNotification"": {
                ""kind"": ""on_final_state"",
                ""completed"": true,
                ""faulted"": true,
                ""cancelled"": false
            }
        }";

        var config = JsonSerializer.Deserialize<MCPServerConfig>(json, MCPJsonSerializerContext.Default.MCPServerConfig);

        config.Should().NotBeNull();
        config!.InvocationModePolicy.Should().Be(AgentInvocationModePolicy.ModelChoice);
        config.ToolInvocationModePolicies.Should().ContainKey("read_file")
            .WhoseValue.Should().Be(AgentInvocationModePolicy.SynchronousOnly);
        config.ToolInvocationModePolicies.Should().ContainKey("long_running_search")
            .WhoseValue.Should().Be(AgentInvocationModePolicy.BackgroundOnly);
        config.BackgroundNotification.Should().Be(new BackgroundTaskNotificationRule.OnFinalStateRule(
            Completed: true,
            Faulted: true,
            Cancelled: false));
    }

    [Fact]
    public void HttpValidate_WithProcessIsolation_Throws()
    {
        var config = new MCPServerConfig
        {
            Name = "remote",
            Transport = "http",
            Endpoint = "https://mcp.example.com/mcp",
            ProcessIsolation = new MCPProcessIsolationConfig()
        };

        config.Invoking(c => c.Validate())
            .Should()
            .Throw<ArgumentException>()
            .WithMessage("*Process isolation is only supported for stdio MCP servers*");
    }

    [Fact]
    public void HttpJsonDeserialization_UsesEndpointBasedSchema()
    {
        var json = @"{
            ""name"": ""remote"",
            ""transport"": ""http"",
            ""endpoint"": ""https://mcp.example.com/mcp"",
            ""httpTransportMode"": ""streamable-http"",
            ""headers"": {
                ""X-Workspace"": ""test""
            },
            ""headerSecretKeys"": {
                ""Authorization"": ""mcp:remote:Authorization""
            },
            ""knownSessionId"": ""session-123"",
            ""ownsSession"": false
        }";

        var config = JsonSerializer.Deserialize<MCPServerConfig>(json, MCPJsonSerializerContext.Default.MCPServerConfig);

        config.Should().NotBeNull();
        config!.Name.Should().Be("remote");
        config.Transport.Should().Be("http");
        config.Endpoint.Should().Be("https://mcp.example.com/mcp");
        config.HttpTransportMode.Should().Be("streamable-http");
        config.Headers.Should().ContainKey("X-Workspace").WhoseValue.Should().Be("test");
        config.HeaderSecretKeys.Should().ContainKey("Authorization").WhoseValue.Should().Be("mcp:remote:Authorization");
        config.KnownSessionId.Should().Be("session-123");
        config.OwnsSession.Should().BeFalse();
        config.Invoking(c => c.Validate()).Should().NotThrow();
    }

    [Fact]
    public void HttpJsonDeserialization_WithOAuth_DeserializesCorrectly()
    {
        var json = @"{
            ""name"": ""enterprise"",
            ""transport"": ""http"",
            ""endpoint"": ""https://mcp.example.com/mcp"",
            ""oauth"": {
                ""redirectUri"": ""http://localhost:8787/callback"",
                ""clientId"": ""client-id"",
                ""clientSecretKey"": ""mcp:enterprise:ClientSecret"",
                ""clientMetadataDocumentUri"": ""https://client.example.com/oauth/metadata.json"",
                ""scopes"": [""read"", ""write""],
                ""additionalAuthorizationParameters"": {
                    ""audience"": ""mcp""
                },
                ""dynamicClientRegistration"": {
                    ""clientName"": ""HPD Agent"",
                    ""clientUri"": ""https://hpd.example.com"",
                    ""initialAccessTokenKey"": ""mcp:enterprise:RegistrationToken""
                }
            }
        }";

        var config = JsonSerializer.Deserialize<MCPServerConfig>(json, MCPJsonSerializerContext.Default.MCPServerConfig);

        config.Should().NotBeNull();
        config!.OAuth.Should().NotBeNull();
        config.OAuth!.RedirectUri.Should().Be("http://localhost:8787/callback");
        config.OAuth.ClientId.Should().Be("client-id");
        config.OAuth.ClientSecretKey.Should().Be("mcp:enterprise:ClientSecret");
        config.OAuth.ClientMetadataDocumentUri.Should().Be("https://client.example.com/oauth/metadata.json");
        config.OAuth.Scopes.Should().ContainInOrder("read", "write");
        config.OAuth.AdditionalAuthorizationParameters.Should().ContainKey("audience").WhoseValue.Should().Be("mcp");
        config.OAuth.DynamicClientRegistration.Should().NotBeNull();
        config.OAuth.DynamicClientRegistration!.ClientName.Should().Be("HPD Agent");
        config.OAuth.DynamicClientRegistration.ClientUri.Should().Be("https://hpd.example.com");
        config.OAuth.DynamicClientRegistration.InitialAccessTokenKey.Should().Be("mcp:enterprise:RegistrationToken");
        config.Invoking(c => c.Validate()).Should().NotThrow();
    }

    [Fact]
    public void ParentToolHarness_DefaultNull()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Transport = "stdio",
            Command = "node"
        };

        config.ParentToolHarness.Should().BeNull();
    }

    [Fact]
    public void CollapseWithinToolHarness_DefaultFalse()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Transport = "stdio",
            Command = "node"
        };

        config.CollapseWithinToolHarness.Should().BeFalse();
    }

    [Fact]
    public void RequiresPermission_DefaultFalse()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Transport = "stdio",
            Command = "node"
        };

        config.RequiresPermission.Should().BeFalse();
    }

    [Fact]
    public void ResourceOptions_DefaultsAreConservative()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Transport = "stdio",
            Command = "node"
        };

        config.EnableResources.Should().BeFalse();
        config.MaxResourceListResults.Should().Be(100);
        config.MaxResourceContentLength.Should().Be(200_000);
        config.EnablePrompts.Should().BeFalse();
        config.MaxPromptListResults.Should().Be(100);
        config.MaxPromptContentLength.Should().Be(200_000);
        config.Invoking(c => c.Validate()).Should().NotThrow();
    }

    [Fact]
    public void Validate_InvalidResourceOrPromptLimits_Throws()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Transport = "stdio",
            Command = "node",
            MaxResourceListResults = 0
        };

        config.Invoking(c => c.Validate())
            .Should().Throw<ArgumentException>()
            .WithMessage("*resource list results*");

        config.MaxResourceListResults = 1;
        config.MaxResourceContentLength = 0;

        config.Invoking(c => c.Validate())
            .Should().Throw<ArgumentException>()
            .WithMessage("*resource content length*");

        config.MaxResourceContentLength = 1;
        config.MaxPromptListResults = 0;

        config.Invoking(c => c.Validate())
            .Should().Throw<ArgumentException>()
            .WithMessage("*prompt list results*");

        config.MaxPromptListResults = 1;
        config.MaxPromptContentLength = 0;

        config.Invoking(c => c.Validate())
            .Should().Throw<ArgumentException>()
            .WithMessage("*prompt content length*");
    }

    [Fact]
    public void RequiresPermission_JsonWithTrue_DeserializesCorrectly()
    {
        var json = @"{
            ""name"": ""dangerous-server"",
            ""transport"": ""stdio"",
            ""command"": ""node"",
            ""requiresPermission"": true
        }";

        var config = JsonSerializer.Deserialize<MCPServerConfig>(json, MCPJsonSerializerContext.Default.MCPServerConfig);

        config.Should().NotBeNull();
        config!.RequiresPermission.Should().BeTrue();
    }

    [Fact]
    public void RequiresPermission_JsonWithout_DefaultsFalse()
    {
        var json = @"{
            ""name"": ""safe-server"",
            ""transport"": ""stdio"",
            ""command"": ""node""
        }";

        var config = JsonSerializer.Deserialize<MCPServerConfig>(json, MCPJsonSerializerContext.Default.MCPServerConfig);

        config.Should().NotBeNull();
        config!.RequiresPermission.Should().BeFalse();
    }

    [Fact]
    public void RuntimeFieldsCanBeSet_WithoutAffectingSerialization()
    {
        var config = new MCPServerConfig
        {
            Name = "wolfram",
            Transport = "stdio",
            Command = "npx",
            Arguments = new List<string> { "wolfram-mcp" },
            ParentToolHarness = "SearchToolHarness",
            CollapseWithinToolHarness = true
        };

        // Verify the fields are set
        config.ParentToolHarness.Should().Be("SearchToolHarness");
        config.CollapseWithinToolHarness.Should().BeTrue();

        // Serialize and verify they're excluded
        var json = JsonSerializer.Serialize(config, MCPJsonSerializerContext.Default.MCPServerConfig);
        json.Should().NotContain("ParentToolHarness");
        json.Should().NotContain("CollapseWithinToolHarness");

        // Deserialize back — fields have defaults
        var deserialized = JsonSerializer.Deserialize<MCPServerConfig>(json, MCPJsonSerializerContext.Default.MCPServerConfig);
        deserialized!.ParentToolHarness.Should().BeNull();
        deserialized.CollapseWithinToolHarness.Should().BeFalse();
    }

    [Fact]
    public void Validate_MissingTransport_Throws()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Command = "node"
        };

        config.Invoking(c => c.Validate())
            .Should().Throw<ArgumentException>()
            .WithMessage("*transport*");
    }

    [Fact]
    public void Validate_StdioWithoutCommand_Throws()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Transport = "stdio"
        };

        config.Invoking(c => c.Validate())
            .Should().Throw<ArgumentException>()
            .WithMessage("*command*");
    }

    [Fact]
    public void Validate_HttpWithoutEndpoint_Throws()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Transport = "http"
        };

        config.Invoking(c => c.Validate())
            .Should().Throw<ArgumentException>()
            .WithMessage("*endpoint*");
    }

    [Fact]
    public void Validate_StdioWithOAuth_Throws()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Transport = "stdio",
            Command = "node",
            OAuth = new MCPOAuthConfig
            {
                RedirectUri = "http://localhost:8787/callback"
            }
        };

        config.Invoking(c => c.Validate())
            .Should().Throw<ArgumentException>()
            .WithMessage("*OAuth*HTTP*");
    }

    [Fact]
    public void Validate_HttpOAuthWithoutRedirectUri_Throws()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Transport = "http",
            Endpoint = "https://mcp.example.com/mcp",
            OAuth = new MCPOAuthConfig()
        };

        config.Invoking(c => c.Validate())
            .Should().Throw<ArgumentException>()
            .WithMessage("*redirectUri*");
    }

    [Fact]
    public void Validate_HttpOAuthWithNonHttpsMetadataDocument_Throws()
    {
        var config = new MCPServerConfig
        {
            Name = "test",
            Transport = "http",
            Endpoint = "https://mcp.example.com/mcp",
            OAuth = new MCPOAuthConfig
            {
                RedirectUri = "http://localhost:8787/callback",
                ClientMetadataDocumentUri = "http://client.example.com/metadata.json"
            }
        };

        config.Invoking(c => c.Validate())
            .Should().Throw<ArgumentException>()
            .WithMessage("*clientMetadataDocumentUri*HTTPS*");
    }

    [Fact]
    public async Task ResolveServerSecretsAsync_ResolvesSecretBackedFields()
    {
        var resolver = new ExplicitSecretResolver(new Dictionary<string, string>
        {
            ["mcp:test:Env"] = "env-secret",
            ["mcp:test:Authorization"] = "Bearer header-secret",
            ["mcp:test:ClientSecret"] = "oauth-secret",
            ["mcp:test:RegistrationToken"] = "registration-secret"
        });
        var config = new MCPServerConfig
        {
            Name = "test",
            Transport = "http",
            Endpoint = "https://mcp.example.com/mcp",
            EnvironmentSecretKeys = new Dictionary<string, string>
            {
                ["API_TOKEN"] = "mcp:test:Env"
            },
            HeaderSecretKeys = new Dictionary<string, string>
            {
                ["Authorization"] = "mcp:test:Authorization"
            },
            OAuth = new MCPOAuthConfig
            {
                RedirectUri = "http://localhost:8787/callback",
                ClientSecretKey = "mcp:test:ClientSecret",
                DynamicClientRegistration = new MCPDynamicClientRegistrationConfig
                {
                    InitialAccessTokenKey = "mcp:test:RegistrationToken"
                }
            }
        };

        await MCPClientManager.ResolveServerSecretsAsync(config, resolver);

        config.Environment.Should().ContainKey("API_TOKEN").WhoseValue.Should().Be("env-secret");
        config.Headers.Should().ContainKey("Authorization").WhoseValue.Should().Be("Bearer header-secret");
        config.OAuth.ClientSecret.Should().Be("oauth-secret");
        config.OAuth.DynamicClientRegistration!.InitialAccessToken.Should().Be("registration-secret");
    }

    [Fact]
    public async Task ResolveServerSecretsAsync_LiteralValuesWinOverSecretKeys()
    {
        var resolver = new ExplicitSecretResolver(new Dictionary<string, string>
        {
            ["mcp:test:Env"] = "env-secret",
            ["mcp:test:Authorization"] = "Bearer header-secret",
            ["mcp:test:ClientSecret"] = "oauth-secret",
            ["mcp:test:RegistrationToken"] = "registration-secret"
        });
        var config = new MCPServerConfig
        {
            Name = "test",
            Transport = "http",
            Endpoint = "https://mcp.example.com/mcp",
            Environment = new Dictionary<string, string?>
            {
                ["API_TOKEN"] = "literal-env"
            },
            EnvironmentSecretKeys = new Dictionary<string, string>
            {
                ["API_TOKEN"] = "mcp:test:Env"
            },
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer literal"
            },
            HeaderSecretKeys = new Dictionary<string, string>
            {
                ["Authorization"] = "mcp:test:Authorization"
            },
            OAuth = new MCPOAuthConfig
            {
                RedirectUri = "http://localhost:8787/callback",
                ClientSecret = "literal-oauth",
                ClientSecretKey = "mcp:test:ClientSecret",
                DynamicClientRegistration = new MCPDynamicClientRegistrationConfig
                {
                    InitialAccessToken = "literal-registration",
                    InitialAccessTokenKey = "mcp:test:RegistrationToken"
                }
            }
        };

        await MCPClientManager.ResolveServerSecretsAsync(config, resolver);

        config.Environment.Should().ContainKey("API_TOKEN").WhoseValue.Should().Be("literal-env");
        config.Headers.Should().ContainKey("Authorization").WhoseValue.Should().Be("Bearer literal");
        config.OAuth.ClientSecret.Should().Be("literal-oauth");
        config.OAuth.DynamicClientRegistration!.InitialAccessToken.Should().Be("literal-registration");
    }
}
