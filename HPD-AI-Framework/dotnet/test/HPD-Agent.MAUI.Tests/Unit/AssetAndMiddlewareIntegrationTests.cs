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
/// Integration, edge case, and concurrency tests for asset management and middleware responses.
/// </summary>
public class AssetAndMiddlewareIntegrationTests : IDisposable
{
    private readonly Mock<IHybridWebView> _mockWebView;
    private readonly MauiSessionManager _sessionManager;
    private readonly TestProxy _proxy;
    private readonly InMemorySessionStore _store;

    public AssetAndMiddlewareIntegrationTests()
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

    #region Integration Tests

    [Fact]
    public async Task Integration_UploadAssetToNewSession()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var base64Data = Convert.ToBase64String("Test data"u8.ToArray());

        // Act
        var assetJson = await _proxy.UploadAsset(session!.Id, base64Data, "text/plain", "test.txt");
        var asset = System.Text.Json.JsonSerializer.Deserialize<AssetDto>(assetJson);

        // Assert
        asset.Should().NotBeNull();
        asset!.AssetId.Should().NotBeNullOrEmpty();

        // Verify it's in the list
        var listJson = await _proxy.ListAssets(session.Id);
        var assets = System.Text.Json.JsonSerializer.Deserialize<List<AssetDto>>(listJson);
        assets!.Should().ContainSingle();
        assets[0].AssetId.Should().Be(asset.AssetId);
    }

    [Fact]
    public async Task Integration_ListAssetsAfterMultipleUploads()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var base64Data = Convert.ToBase64String("test"u8.ToArray());

        // Act - Upload 3 assets
        var asset1Json = await _proxy.UploadAsset(session!.Id, base64Data, "text/plain", "file1.txt");
        var asset2Json = await _proxy.UploadAsset(session.Id, base64Data, "image/png", "image.png");
        var asset3Json = await _proxy.UploadAsset(session.Id, base64Data, "application/pdf", "doc.pdf");

        var asset1 = System.Text.Json.JsonSerializer.Deserialize<AssetDto>(asset1Json);
        var asset2 = System.Text.Json.JsonSerializer.Deserialize<AssetDto>(asset2Json);
        var asset3 = System.Text.Json.JsonSerializer.Deserialize<AssetDto>(asset3Json);

        // Assert - List should contain all 3
        var listJson = await _proxy.ListAssets(session.Id);
        var assets = System.Text.Json.JsonSerializer.Deserialize<List<AssetDto>>(listJson);
        assets!.Count.Should().Be(3);
        assets.Should().Contain(a => a.AssetId == asset1!.AssetId);
        assets.Should().Contain(a => a.AssetId == asset2!.AssetId);
        assets.Should().Contain(a => a.AssetId == asset3!.AssetId);
    }

    [Fact]
    public async Task Integration_DeleteAssetAndVerifyNotInList()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var base64Data = Convert.ToBase64String("test"u8.ToArray());

        var assetJson = await _proxy.UploadAsset(session!.Id, base64Data, "text/plain", "test.txt");
        var asset = System.Text.Json.JsonSerializer.Deserialize<AssetDto>(assetJson);

        // Act - Delete
        await _proxy.DeleteAsset(session.Id, asset!.AssetId);

        // Assert - List should be empty
        var listJson = await _proxy.ListAssets(session.Id);
        var assets = System.Text.Json.JsonSerializer.Deserialize<List<AssetDto>>(listJson);
        assets!.Should().BeEmpty();
    }

    [Fact]
    public async Task Integration_AssetsPersistAcrossBranches()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var base64Data = Convert.ToBase64String("test"u8.ToArray());

        // Upload asset
        await _proxy.UploadAsset(session!.Id, base64Data, "text/plain", "test.txt");

        // Create a new branch
        var createBranchJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            BranchId = "new-branch",
            Name = "New Branch",
            Description = (string?)null,
            Tags = (List<string>?)null
        });
        await _proxy.CreateBranch(session.Id, createBranchJson);

        // Act - List assets (should still be there, assets are session-scoped)
        var listJson = await _proxy.ListAssets(session.Id);
        var assets = System.Text.Json.JsonSerializer.Deserialize<List<AssetDto>>(listJson);

        // Assert
        assets!.Should().ContainSingle();
    }

    [Fact]
    public async Task Integration_DeleteSessionRemovesAssets()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var base64Data = Convert.ToBase64String("test"u8.ToArray());

        await _proxy.UploadAsset(session!.Id, base64Data, "text/plain", "test.txt");

        // Act - Delete session
        await _proxy.DeleteSession(session.Id);

        // Assert - Session no longer exists
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.ListAssets(session.Id));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task EdgeCase_UploadEmptyFile()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var emptyData = Array.Empty<byte>();
        var base64Data = Convert.ToBase64String(emptyData);

        // Act
        var assetJson = await _proxy.UploadAsset(session!.Id, base64Data, "text/plain", "empty.txt");
        var asset = System.Text.Json.JsonSerializer.Deserialize<AssetDto>(assetJson);

        // Assert
        asset!.SizeBytes.Should().Be(0);
    }

    [Fact]
    public async Task EdgeCase_UploadVeryLongFilename()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var base64Data = Convert.ToBase64String("test"u8.ToArray());
        var longFilename = new string('a', 300) + ".txt"; // 300+ characters

        // Act
        var assetJson = await _proxy.UploadAsset(session!.Id, base64Data, "text/plain", longFilename);
        var asset = System.Text.Json.JsonSerializer.Deserialize<AssetDto>(assetJson);

        // Assert - Should not throw, just store it
        asset.Should().NotBeNull();
    }

    [Fact]
    public async Task EdgeCase_UploadSpecialCharactersInFilename()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var base64Data = Convert.ToBase64String("test"u8.ToArray());
        var specialFilename = "file with spaces & symbols! (2024) [final].txt";

        // Act
        var assetJson = await _proxy.UploadAsset(session!.Id, base64Data, "text/plain", specialFilename);
        var asset = System.Text.Json.JsonSerializer.Deserialize<AssetDto>(assetJson);

        // Assert
        asset.Should().NotBeNull();
    }

    [Fact]
    public void EdgeCase_PermissionResponseWithNullReason()
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

        // Act & Assert - Should handle null reason
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
    public void EdgeCase_ClientToolResponseWithNullErrorMessage()
    {
        // Arrange
        var request = new
        {
            SessionId = "test-session",
            RequestId = "req-123",
            Success = true,
            Content = new List<object>(),
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
    public async Task EdgeCase_UploadAfterSessionDeleted()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        await _proxy.DeleteSession(session!.Id);

        var base64Data = Convert.ToBase64String("test"u8.ToArray());

        // Act & Assert - Should fail gracefully
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _proxy.UploadAsset(session.Id, base64Data, "text/plain", "test.txt"));
    }

    [Fact]
    public async Task EdgeCase_ListAssetsAfterAllDeleted()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var base64Data = Convert.ToBase64String("test"u8.ToArray());

        var asset1Json = await _proxy.UploadAsset(session!.Id, base64Data, "text/plain", "file1.txt");
        var asset2Json = await _proxy.UploadAsset(session.Id, base64Data, "text/plain", "file2.txt");

        var asset1 = System.Text.Json.JsonSerializer.Deserialize<AssetDto>(asset1Json);
        var asset2 = System.Text.Json.JsonSerializer.Deserialize<AssetDto>(asset2Json);

        await _proxy.DeleteAsset(session.Id, asset1!.AssetId);
        await _proxy.DeleteAsset(session.Id, asset2!.AssetId);

        // Act
        var listJson = await _proxy.ListAssets(session.Id);
        var assets = System.Text.Json.JsonSerializer.Deserialize<List<AssetDto>>(listJson);

        // Assert - Should return empty array, not null
        assets.Should().NotBeNull();
        assets!.Should().BeEmpty();
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task Concurrency_ConcurrentAssetUploads()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var base64Data = Convert.ToBase64String("test"u8.ToArray());

        // Act - Upload 5 assets concurrently
        var tasks = Enumerable.Range(0, 5).Select(i =>
            _proxy.UploadAsset(session!.Id, base64Data, "text/plain", $"file{i}.txt"));
        var results = await Task.WhenAll(tasks);

        // Assert - All should succeed
        results.Should().HaveCount(5);
        results.Should().AllSatisfy(json => json.Should().Contain("AssetId"));

        // Verify all are in the list
        var listJson = await _proxy.ListAssets(session!.Id);
        var assets = System.Text.Json.JsonSerializer.Deserialize<List<AssetDto>>(listJson);
        assets!.Count.Should().Be(5);
    }

    [Fact]
    public async Task Concurrency_UploadAndListSimultaneously()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var base64Data = Convert.ToBase64String("test"u8.ToArray());

        // Act - Upload and list at the same time
        var uploadTask = _proxy.UploadAsset(session!.Id, base64Data, "text/plain", "test.txt");
        var listTask = _proxy.ListAssets(session.Id);

        await Task.WhenAll(uploadTask, listTask);

        // Assert - Both should complete without error
        uploadTask.IsCompletedSuccessfully.Should().BeTrue();
        listTask.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task Concurrency_UploadAndDeleteSimultaneously()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var base64Data = Convert.ToBase64String("test"u8.ToArray());

        // Upload first asset
        var assetJson = await _proxy.UploadAsset(session!.Id, base64Data, "text/plain", "existing.txt");
        var asset = System.Text.Json.JsonSerializer.Deserialize<AssetDto>(assetJson);

        // Act - Upload new asset and delete existing asset simultaneously
        var uploadTask = _proxy.UploadAsset(session.Id, base64Data, "text/plain", "new.txt");
        var deleteTask = _proxy.DeleteAsset(session.Id, asset!.AssetId);

        await Task.WhenAll(uploadTask, deleteTask);

        // Assert - Both should complete
        uploadTask.IsCompletedSuccessfully.Should().BeTrue();
        deleteTask.IsCompletedSuccessfully.Should().BeTrue();

        // Verify final state
        var listJson = await _proxy.ListAssets(session.Id);
        var assets = System.Text.Json.JsonSerializer.Deserialize<List<AssetDto>>(listJson);
        assets!.Should().ContainSingle(); // Only the new asset
    }

    #endregion

    #region Serialization Tests

    [Fact]
    public async Task Serialization_AssetDtoRoundTrip()
    {
        // Arrange
        var sessionJson = await _proxy.CreateSession();
        var session = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(sessionJson);
        var base64Data = Convert.ToBase64String("test data"u8.ToArray());

        // Act
        var assetJson = await _proxy.UploadAsset(session!.Id, base64Data, "text/plain", "test.txt");
        var asset1 = System.Text.Json.JsonSerializer.Deserialize<AssetDto>(assetJson);

        // Serialize and deserialize again
        var reserializedJson = System.Text.Json.JsonSerializer.Serialize(asset1);
        var asset2 = System.Text.Json.JsonSerializer.Deserialize<AssetDto>(reserializedJson);

        // Assert
        asset2.Should().NotBeNull();
        asset2!.AssetId.Should().Be(asset1!.AssetId);
        asset2.ContentType.Should().Be(asset1.ContentType);
        asset2.SizeBytes.Should().Be(asset1.SizeBytes);
        asset2.CreatedAt.Should().Be(asset1.CreatedAt);
    }

    [Fact]
    public void Serialization_PermissionRequestRoundTrip()
    {
        // Arrange
        var request1 = new PermissionResponseRequest(
            SessionId: "test-session",
            PermissionId: "perm-123",
            Approved: true,
            Reason: "User approved",
            Choice: "allow_always");

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(request1);
        var request2 = System.Text.Json.JsonSerializer.Deserialize<PermissionResponseRequest>(json);

        // Assert
        request2.Should().NotBeNull();
        request2!.SessionId.Should().Be(request1.SessionId);
        request2.PermissionId.Should().Be(request1.PermissionId);
        request2.Approved.Should().Be(request1.Approved);
        request2.Reason.Should().Be(request1.Reason);
        request2.Choice.Should().Be(request1.Choice);
    }

    [Fact]
    public void Serialization_ClientToolRequestRoundTrip()
    {
        // Arrange
        var content = new List<ClientToolContentDto>
        {
            new ClientToolContentDto("text", "Hello", null, null)
        };
        var request1 = new ClientToolResponseRequest(
            SessionId: "test-session",
            RequestId: "req-123",
            Success: true,
            Content: content,
            ErrorMessage: null);

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(request1);
        var request2 = System.Text.Json.JsonSerializer.Deserialize<ClientToolResponseRequest>(json);

        // Assert
        request2.Should().NotBeNull();
        request2!.SessionId.Should().Be(request1.SessionId);
        request2.RequestId.Should().Be(request1.RequestId);
        request2.Success.Should().Be(request1.Success);
        request2.Content.Should().HaveCount(1);
        request2.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Serialization_HandlesNullOptionalFields()
    {
        // Arrange - All optional fields are null
        var permissionRequest = new PermissionResponseRequest(
            SessionId: "test-session",
            PermissionId: "perm-123",
            Approved: true,
            Reason: null,
            Choice: null);

        var clientToolRequest = new ClientToolResponseRequest(
            SessionId: "test-session",
            RequestId: "req-123",
            Success: true,
            Content: null,
            ErrorMessage: null);

        // Act
        var permissionJson = System.Text.Json.JsonSerializer.Serialize(permissionRequest);
        var clientToolJson = System.Text.Json.JsonSerializer.Serialize(clientToolRequest);

        var permission2 = System.Text.Json.JsonSerializer.Deserialize<PermissionResponseRequest>(permissionJson);
        var clientTool2 = System.Text.Json.JsonSerializer.Deserialize<ClientToolResponseRequest>(clientToolJson);

        // Assert - Should handle nulls gracefully
        permission2!.Reason.Should().BeNull();
        permission2.Choice.Should().BeNull();
        clientTool2!.Content.Should().BeNull();
        clientTool2.ErrorMessage.Should().BeNull();
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
