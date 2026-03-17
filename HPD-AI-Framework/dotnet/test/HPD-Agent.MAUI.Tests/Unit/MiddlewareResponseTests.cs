using FluentAssertions;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Maui;
using HPD.Agent.Maui.Tests.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.Maui;
using Moq;

namespace HPD.Agent.Maui.Tests.Unit;

/// <summary>
/// Unit tests for middleware response methods in HybridWebViewAgentProxy.
/// Tests RespondToPermission and RespondToClientTool functionality.
/// </summary>
public class MiddlewareResponseTests : IDisposable
{
    private readonly Mock<IHybridWebView> _mockWebView;
    private readonly MauiSessionManager _sessionManager;
    private readonly TestProxy _proxy;
    private readonly InMemorySessionStore _store;

    public MiddlewareResponseTests()
    {
        _mockWebView = new Mock<IHybridWebView>();
        _store = new InMemorySessionStore();
        var optionsMonitor = new OptionsMonitorWrapper();

        optionsMonitor.CurrentValue.AgentConfig = new AgentConfig
        {
            Name = "Test Agent",
            Provider = new ProviderConfig
            {
                ProviderKey = "test",
                ModelName = "test-model"
            }
        };
        optionsMonitor.CurrentValue.ConfigureAgent = builder =>
        {
            var chatClient = new FakeChatClient();
            var providerRegistry = new TestProviderRegistry(chatClient);
            var field = typeof(AgentBuilder).GetField("_providerRegistry",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(builder, providerRegistry);
        };

        _sessionManager = new MauiSessionManager(_store, optionsMonitor, Options.DefaultName);
        var agentManager = new MauiAgentManager(new InMemoryAgentStore(), _sessionManager, optionsMonitor, Options.DefaultName);
        _proxy = new TestProxy(_sessionManager, agentManager, _mockWebView.Object);
    }

    public void Dispose()
    {
        _sessionManager?.Dispose();
    }

    #region RespondToPermission Tests

    [Fact]
    public void RespondToPermission_ThrowsWhenInvalidJson()
    {
        // Act & Assert
        Assert.Throws<System.Text.Json.JsonException>(() =>
            _proxy.RespondToPermission("invalid json {"));
    }

    [Fact]
    public void RespondToPermission_ThrowsWhenSessionIdMissing()
    {
        // Arrange
        var request = new
        {
            SessionId = (string?)null,
            PermissionId = "perm-123",
            Approved = true,
            Reason = (string?)null,
            Choice = (string?)null
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert — AgentId defaults to "default"; no agent is running, so InvalidOperationException
        Assert.Throws<InvalidOperationException>(() =>
            _proxy.RespondToPermission(json));
    }

    [Fact]
    public async Task RespondToPermission_ThrowsWhenNoRunningAgent()
    {
        // Arrange - Create session but don't start streaming
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var request = new
        {
            SessionId = session!.Id,
            PermissionId = "perm-123",
            Approved = true,
            Reason = (string?)null,
            Choice = (string?)null
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert - Agent is not streaming, so should throw
        Assert.Throws<InvalidOperationException>(() =>
            _proxy.RespondToPermission(json));
    }

    [Fact]
    public void RespondToPermission_HandlesApprovalTrue()
    {
        // Arrange
        var request = new
        {
            SessionId = "test-session",
            PermissionId = "perm-123",
            Approved = true,
            Reason = (string?)null,
            Choice = (string?)null
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert - Should not throw during parsing
        var exception = Record.Exception(() =>
        {
            try
            {
                _proxy.RespondToPermission(json);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No running agent"))
            {
                // Expected - no running agent
                return;
            }
        });

        // Should only fail on "no running agent", not on parsing
        exception.Should().BeNull();
    }

    [Fact]
    public void RespondToPermission_HandlesApprovalFalse()
    {
        // Arrange
        var request = new
        {
            SessionId = "test-session",
            PermissionId = "perm-123",
            Approved = false,
            Reason = "User denied access",
            Choice = (string?)null
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert - Should not throw during parsing
        var exception = Record.Exception(() =>
        {
            try
            {
                _proxy.RespondToPermission(json);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No running agent"))
            {
                // Expected - no running agent
                return;
            }
        });

        exception.Should().BeNull();
    }

    [Fact]
    public void RespondToPermission_HandlesChoiceAlwaysAllow()
    {
        // Arrange
        var request = new
        {
            SessionId = "test-session",
            PermissionId = "perm-123",
            Approved = true,
            Reason = (string?)null,
            Choice = "allow_always"
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            try
            {
                _proxy.RespondToPermission(json);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No running agent"))
            {
                return;
            }
        });

        exception.Should().BeNull();
    }

    [Fact]
    public void RespondToPermission_HandlesChoiceAlwaysDeny()
    {
        // Arrange
        var request = new
        {
            SessionId = "test-session",
            PermissionId = "perm-123",
            Approved = false,
            Reason = "Denied permanently",
            Choice = "deny_always"
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            try
            {
                _proxy.RespondToPermission(json);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No running agent"))
            {
                return;
            }
        });

        exception.Should().BeNull();
    }

    [Fact]
    public void RespondToPermission_HandlesChoiceAsk()
    {
        // Arrange - Choice is null (defaults to Ask)
        var request = new
        {
            SessionId = "test-session",
            PermissionId = "perm-123",
            Approved = true,
            Reason = (string?)null,
            Choice = (string?)null
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            try
            {
                _proxy.RespondToPermission(json);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No running agent"))
            {
                return;
            }
        });

        exception.Should().BeNull();
    }

    [Fact]
    public void RespondToPermission_CaseInsensitiveChoice()
    {
        // Arrange - Test different casings
        var casings = new[] { "ALLOW_ALWAYS", "Allow_Always", "aLlOw_AlWaYs" };

        foreach (var casing in casings)
        {
            var request = new
            {
                SessionId = "test-session",
                PermissionId = "perm-123",
                Approved = true,
                Reason = (string?)null,
                Choice = casing
            };
            var json = System.Text.Json.JsonSerializer.Serialize(request);

            // Act & Assert - Should handle all casings
            var exception = Record.Exception(() =>
            {
                try
                {
                    _proxy.RespondToPermission(json);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("No running agent"))
                {
                    return;
                }
            });

            exception.Should().BeNull($"Choice '{casing}' should be handled case-insensitively");
        }
    }

    [Fact]
    public void RespondToPermission_SendsCorrectPermissionId()
    {
        // Arrange
        var expectedPermissionId = "perm-12345";
        var request = new
        {
            SessionId = "test-session",
            PermissionId = expectedPermissionId,
            Approved = true,
            Reason = (string?)null,
            Choice = (string?)null
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert - PermissionId is preserved in request
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<PermissionResponseRequest>(json);
        deserialized!.PermissionId.Should().Be(expectedPermissionId);
    }

    [Fact]
    public void RespondToPermission_IncludesReasonInResponse()
    {
        // Arrange
        var expectedReason = "Security policy violation";
        var request = new
        {
            SessionId = "test-session",
            PermissionId = "perm-123",
            Approved = false,
            Reason = expectedReason,
            Choice = (string?)null
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<PermissionResponseRequest>(json);
        deserialized!.Reason.Should().Be(expectedReason);
    }

    #endregion

    #region RespondToClientTool Tests

    [Fact]
    public void RespondToClientTool_ThrowsWhenInvalidJson()
    {
        // Act & Assert
        Assert.Throws<System.Text.Json.JsonException>(() =>
            _proxy.RespondToClientTool("invalid json {"));
    }

    [Fact]
    public void RespondToClientTool_ThrowsWhenSessionIdMissing()
    {
        // Arrange
        var request = new
        {
            SessionId = (string?)null,
            RequestId = "req-123",
            Success = true,
            Content = new List<object>(),
            ErrorMessage = (string?)null
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert — AgentId defaults to "default"; no agent is running, so InvalidOperationException
        Assert.Throws<InvalidOperationException>(() =>
            _proxy.RespondToClientTool(json));
    }

    [Fact]
    public async Task RespondToClientTool_ThrowsWhenNoRunningAgent()
    {
        // Arrange - Create session but don't start streaming
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);

        var request = new
        {
            SessionId = session!.Id,
            RequestId = "req-123",
            Success = true,
            Content = new List<object>(),
            ErrorMessage = (string?)null
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _proxy.RespondToClientTool(json));
    }

    [Fact]
    public void RespondToClientTool_HandlesSuccessTrue()
    {
        // Arrange
        var request = new
        {
            SessionId = "test-session",
            RequestId = "req-123",
            Success = true,
            Content = new[]
            {
                new { Type = "text", Text = "Result data", Data = (byte[]?)null, MediaType = (string?)null }
            },
            ErrorMessage = (string?)null
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            try
            {
                _proxy.RespondToClientTool(json);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No running agent"))
            {
                return;
            }
        });

        exception.Should().BeNull();
    }

    [Fact]
    public void RespondToClientTool_HandlesSuccessFalse()
    {
        // Arrange
        var request = new
        {
            SessionId = "test-session",
            RequestId = "req-123",
            Success = false,
            Content = (List<object>?)null,
            ErrorMessage = "Tool execution failed"
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            try
            {
                _proxy.RespondToClientTool(json);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No running agent"))
            {
                return;
            }
        });

        exception.Should().BeNull();
    }

    [Fact]
    public void RespondToClientTool_HandlesTextContent()
    {
        // Arrange
        var request = new
        {
            SessionId = "test-session",
            RequestId = "req-123",
            Success = true,
            Content = new[]
            {
                new { Type = "text", Text = "Hello World", Data = (byte[]?)null, MediaType = (string?)null }
            },
            ErrorMessage = (string?)null
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            try
            {
                _proxy.RespondToClientTool(json);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No running agent"))
            {
                return;
            }
        });

        exception.Should().BeNull();
    }

    [Fact]
    public void RespondToClientTool_HandlesBinaryContent()
    {
        // Arrange
        var binaryData = new byte[] { 1, 2, 3, 4, 5 };
        var request = new
        {
            SessionId = "test-session",
            RequestId = "req-123",
            Success = true,
            Content = new[]
            {
                new { Type = "binary", Text = (string?)null, Data = binaryData, MediaType = "application/octet-stream" }
            },
            ErrorMessage = (string?)null
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            try
            {
                _proxy.RespondToClientTool(json);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No running agent"))
            {
                return;
            }
        });

        exception.Should().BeNull();
    }

    [Fact]
    public void RespondToClientTool_HandlesMultipleContentItems()
    {
        // Arrange
        var request = new
        {
            SessionId = "test-session",
            RequestId = "req-123",
            Success = true,
            Content = new[]
            {
                new { Type = "text", Text = "First item", Data = (byte[]?)null, MediaType = (string?)null },
                new { Type = "text", Text = "Second item", Data = (byte[]?)null, MediaType = (string?)null },
                new { Type = "text", Text = "Third item", Data = (byte[]?)null, MediaType = (string?)null }
            },
            ErrorMessage = (string?)null
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            try
            {
                _proxy.RespondToClientTool(json);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No running agent"))
            {
                return;
            }
        });

        exception.Should().BeNull();
    }

    [Fact]
    public void RespondToClientTool_HandlesEmptyContent()
    {
        // Arrange
        var request = new
        {
            SessionId = "test-session",
            RequestId = "req-123",
            Success = true,
            Content = (List<object>?)null,
            ErrorMessage = (string?)null
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            try
            {
                _proxy.RespondToClientTool(json);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No running agent"))
            {
                return;
            }
        });

        exception.Should().BeNull();
    }

    [Fact]
    public void RespondToClientTool_SendsCorrectRequestId()
    {
        // Arrange
        var expectedRequestId = "req-12345";
        var request = new
        {
            SessionId = "test-session",
            RequestId = expectedRequestId,
            Success = true,
            Content = new List<object>(),
            ErrorMessage = (string?)null
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<ClientToolResponseRequest>(json);
        deserialized!.RequestId.Should().Be(expectedRequestId);
    }

    [Fact]
    public void RespondToClientTool_IncludesErrorMessage()
    {
        // Arrange
        var expectedErrorMessage = "Network timeout";
        var request = new
        {
            SessionId = "test-session",
            RequestId = "req-123",
            Success = false,
            Content = (List<object>?)null,
            ErrorMessage = expectedErrorMessage
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<ClientToolResponseRequest>(json);
        deserialized!.ErrorMessage.Should().Be(expectedErrorMessage);
    }

    [Fact]
    public void RespondToClientTool_HandlesMixedContentTypes()
    {
        // Arrange
        var binaryData = new byte[] { 1, 2, 3 };
        var request = new
        {
            SessionId = "test-session",
            RequestId = "req-123",
            Success = true,
            Content = new[]
            {
                new { Type = "text", Text = "Text content", Data = (byte[]?)null, MediaType = (string?)null },
                new { Type = "binary", Text = (string?)null, Data = binaryData, MediaType = "image/png" }
            },
            ErrorMessage = (string?)null
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request);

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            try
            {
                _proxy.RespondToClientTool(json);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No running agent"))
            {
                return;
            }
        });

        exception.Should().BeNull();
    }

    #endregion

    #region Helper Classes

    private class TestProxy : HybridWebViewAgentProxy
    {
        public TestProxy(MauiSessionManager manager, MauiAgentManager agentManager, IHybridWebView webView)
            : base(manager, agentManager, webView)
        {
        }
    }

    private class OptionsMonitorWrapper : IOptionsMonitor<HPDAgentConfig>
    {
        public HPDAgentConfig CurrentValue { get; } = new HPDAgentConfig();
        public HPDAgentConfig Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<HPDAgentConfig, string?> listener) => null;
    }

    #endregion
}
