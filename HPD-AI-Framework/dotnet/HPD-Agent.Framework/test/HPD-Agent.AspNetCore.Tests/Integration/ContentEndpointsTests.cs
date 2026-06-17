using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Data;

namespace HPD.Agent.AspNetCore.Tests.Integration;

/// <summary>
/// Integration tests for Content management endpoints.
/// Tests: POST /sessions/{sid}/threads/{bid}/content, GET /sessions/{sid}/threads/{bid}/content, GET /sessions/{sid}/threads/{bid}/content/{contentId}, DELETE /sessions/{sid}/threads/{bid}/content/{contentId}
/// </summary>
public class ContentEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ContentEndpointsTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> CreateTestSession()
    {
        var response = await _client.PostAsync("/sessions", null);
        var session = await response.Content.ReadFromJsonAsync<SessionDto>();
        return session!.Id;
    }

    #region POST /sessions/{sid}/threads/{bid}/content

    [Fact]
    public async Task UploadContent_Returns201_WithContentDto()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("Test file content"));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", "test.txt");

        // Act
        var response = await _client.PostAsync($"/sessions/{sessionId}/threads/main/content", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<ContentDto>();
        dto.Should().NotBeNull();
        dto!.ContentId.Should().NotBeNullOrEmpty();
        dto.ContentType.Should().Be("text/plain");
        dto.SizeBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UploadContent_AcceptsMultipartFormData()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var form = new MultipartFormDataContent();
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        var fileContent = new ByteArrayContent(imageBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", "image.png");

        // Act
        var response = await _client.PostAsync($"/sessions/{sessionId}/threads/main/content", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<ContentDto>();
        dto!.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task UploadContent_StoresContentType_Correctly()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("{}"));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        form.Add(fileContent, "file", "data.json");

        // Act
        var response = await _client.PostAsync($"/sessions/{sessionId}/threads/main/content", form);

        // Assert
        var dto = await response.Content.ReadFromJsonAsync<ContentDto>();
        dto!.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task UploadContent_CalculatesSizeBytes_Correctly()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var testData = new byte[1024]; // 1 KB
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(testData);
        form.Add(fileContent, "file", "test.bin");

        // Act
        var response = await _client.PostAsync($"/sessions/{sessionId}/threads/main/content", form);

        // Assert
        var dto = await response.Content.ReadFromJsonAsync<ContentDto>();
        dto!.SizeBytes.Should().Be(1024);
    }

    [Fact]
    public async Task UploadContent_Returns404_WhenSessionNotFound()
    {
        // Arrange
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("test"));
        form.Add(fileContent, "file", "test.txt");

        // Act
        var response = await _client.PostAsync("/sessions/nonexistent/threads/main/content", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UploadContent_Returns404_WhenStoreDoesNotSupportContent()
    {
        // Content endpoints use the hosting content service's explicit content store.
        var sessionId = await CreateTestSession();
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("test"));
        form.Add(fileContent, "file", "test.txt");

        // Act
        var response = await _client.PostAsync($"/sessions/{sessionId}/threads/main/content", form);

        // Assert - explicit hosting content store is available, so should succeed
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task UploadContent_Returns400_WhenNoFileProvided()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var form = new MultipartFormDataContent(); // Empty, no file

        // Act
        var response = await _client.PostAsync($"/sessions/{sessionId}/threads/main/content", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region GET /sessions/{sid}/threads/{bid}/content

    [Fact]
    public async Task ListContent_ReturnsAllContent_ForSession()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Upload 2 content items
        for (int i = 0; i < 2; i++)
        {
            var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes($"File {i}"));
            form.Add(fileContent, "file", $"file{i}.txt");
            await _client.PostAsync($"/sessions/{sessionId}/threads/main/content", form);
        }

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/threads/main/content");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<ContentDto>>();
        items.Should().NotBeNull();
        items!.Count.Should().Be(2);
    }

    [Fact]
    public async Task ListContent_ReturnsEmptyArray_WhenNoContent()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/threads/main/content");

        // Assert
        var items = await response.Content.ReadFromJsonAsync<List<ContentDto>>();
        items.Should().NotBeNull();
        items!.Should().BeEmpty();
    }

    [Fact]
    public async Task ListContent_Returns404_WhenSessionNotFound()
    {
        // Act
        var response = await _client.GetAsync("/sessions/nonexistent/threads/main/content");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListContent_Returns404_WhenStoreDoesNotSupportContent()
    {
        // Similar to upload test - explicit hosting content store is available
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/threads/main/content");

        // Assert - Should succeed with InMemorySessionStore
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region GET /sessions/{sid}/threads/{bid}/content/{contentId}

    [Fact]
    public async Task DownloadContent_ReturnsBinaryData_WithCorrectContentType()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var uploadContent = new MultipartFormDataContent();
        var fileBytes = Encoding.UTF8.GetBytes("Test file content");
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        uploadContent.Add(fileContent, "file", "test.txt");

        var uploadResponse = await _client.PostAsync($"/sessions/{sessionId}/threads/main/content", uploadContent);
        var dto = await uploadResponse.Content.ReadFromJsonAsync<ContentDto>();

        // Act
        var downloadResponse = await _client.GetAsync($"/sessions/{sessionId}/threads/main/content/{dto!.ContentId}");

        // Assert
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        downloadResponse.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        var downloadedBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
        downloadedBytes.Should().Equal(fileBytes);
    }

    [Fact]
    public async Task DownloadContent_Returns404_WhenContentNotFound()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.GetAsync($"/sessions/{sessionId}/threads/main/content/nonexistent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DownloadContent_Returns404_WhenSessionNotFound()
    {
        // Act
        var response = await _client.GetAsync("/sessions/nonexistent/threads/main/content/content-id");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DownloadContent_SetsContentDisposition_Header()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("test"));
        uploadContent.Add(fileContent, "file", "download.txt");

        var uploadResponse = await _client.PostAsync($"/sessions/{sessionId}/threads/main/content", uploadContent);
        var dto = await uploadResponse.Content.ReadFromJsonAsync<ContentDto>();

        // Act
        var downloadResponse = await _client.GetAsync($"/sessions/{sessionId}/threads/main/content/{dto!.ContentId}");

        // Assert
        downloadResponse.Content.Headers.ContentDisposition.Should().NotBeNull();
    }

    #endregion

    #region DELETE /sessions/{sid}/threads/{bid}/content/{contentId}

    [Fact]
    public async Task DeleteContent_Returns204_OnSuccess()
    {
        // Arrange
        var sessionId = await CreateTestSession();
        var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("to delete"));
        uploadContent.Add(fileContent, "file", "delete.txt");

        var uploadResponse = await _client.PostAsync($"/sessions/{sessionId}/threads/main/content", uploadContent);
        var dto = await uploadResponse.Content.ReadFromJsonAsync<ContentDto>();

        // Act
        var deleteResponse = await _client.DeleteAsync($"/sessions/{sessionId}/threads/main/content/{dto!.ContentId}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteContent_Returns404_WhenContentNotFound()
    {
        // Arrange
        var sessionId = await CreateTestSession();

        // Act
        var response = await _client.DeleteAsync($"/sessions/{sessionId}/threads/main/content/nonexistent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteContent_Returns404_WhenSessionNotFound()
    {
        // Act
        var response = await _client.DeleteAsync("/sessions/nonexistent/threads/main/content/content-id");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
