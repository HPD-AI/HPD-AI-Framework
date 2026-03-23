using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using HPD.VCS;
using HPD.VCS.Core;
using HPD.VCS.Storage;

namespace HPD.VCS.Tests.Storage
{
    public class FileSystemOperationHeadStoreTests
    {
        private readonly MockFileSystem _mockFileSystem;
        private readonly FileSystemOperationHeadStore _headStore;
        private readonly string _basePath;

        // Define the hex strings as constants for consistency
        private const string HexString1 = "abc123def456abc123def456abc123def456abc123def456abc123def456abc1";
        private const string HexString2 = "def456abc123def456abc123def456abc123def456abc123def456abc123def4";
        private const string HexString3 = "fed654cba987fed654cba987fed654cba987fed654cba987fed654cba987fed6";

        public FileSystemOperationHeadStoreTests()
        {
            _mockFileSystem = new MockFileSystem();
            _basePath = "/test/repo/.vcs";
            _headStore = new FileSystemOperationHeadStore(_mockFileSystem, _basePath);
        }

        [Fact]
        public async Task GetHeadOperationIdsAsync_EmptyFile_ReturnsEmptyList()
        {
            // Arrange
            var headsFilePath = Path.Combine(_basePath, "heads");
            _mockFileSystem.AddFile(headsFilePath, "");

            // Act
            var result = await _headStore.GetHeadOperationIdsAsync();

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetHeadOperationIdsAsync_NonExistentFile_ReturnsEmptyList()
        {
            // Act
            var result = await _headStore.GetHeadOperationIdsAsync();

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task ReadHeadOperationIdsAsync_ValidHeads_ReturnsOperationIds()
        {
            // Arrange
            var headsFilePath = Path.Combine(_basePath, "heads");
            var operationId1 = OperationId.FromHexString(HexString1);
            var operationId2 = OperationId.FromHexString(HexString2);
            
            // Use full hex strings in file content, not ToString()
            var headsContent = $"{HexString1}\n{HexString2}\n";
            _mockFileSystem.AddFile(headsFilePath, headsContent);
            
            // Act
            var result = await _headStore.GetHeadOperationIdsAsync();

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Contains(operationId1, result);
            Assert.Contains(operationId2, result);
        }

        [Fact]
        public async Task GetHeadOperationIdsAsync_MalformedFile_ThrowsInvalidDataException()
        {
            // Arrange
            var headsFilePath = Path.Combine(_basePath, "heads");
            var malformedContent = "not-a-valid-operation-id\nabc123"; // Invalid hex strings
            _mockFileSystem.AddFile(headsFilePath, malformedContent);

            // Act & Assert
            await Assert.ThrowsAsync<VCS.Storage.CorruptObjectException>(
                () => _headStore.GetHeadOperationIdsAsync());
        }

        [Fact]
        public async Task GetHeadOperationIdsAsync_TruncatedHex_ThrowsInvalidDataException()
        {
            // Arrange
            var headsFilePath = Path.Combine(_basePath, "heads");
            var truncatedContent = "abc123def456abc123def456abc123def456abc123def456abc123def456abc"; // 63 chars instead of 64
            _mockFileSystem.AddFile(headsFilePath, truncatedContent);

            // Act & Assert
            await Assert.ThrowsAsync<VCS.Storage.CorruptObjectException>(
                () => _headStore.GetHeadOperationIdsAsync());
        }

        [Fact]
        public async Task UpdateHeadOperationIdsAsync_EmptyOldHeads_CreatesNewHeadsFile()
        {
            // Arrange
            var newOperationId = OperationId.FromHexString(HexString1);

            // Act
            await _headStore.UpdateHeadOperationIdsAsync(new List<OperationId>(), newOperationId);

            // Assert
            var headsFilePath = Path.Combine(_basePath, "heads");
            Assert.True(_mockFileSystem.FileExists(headsFilePath));
            var content = await _mockFileSystem.File.ReadAllTextAsync(headsFilePath);
            
            // Expect the full hex string in the file, not ToString()
            Assert.Equal($"{HexString1}\n", content);
        }

        [Fact]
        public async Task UpdateHeadOperationIdsAsync_ReplaceExistingHead_UpdatesFile()
        {
            // Arrange
            var oldOperationId = OperationId.FromHexString(HexString1);
            var newOperationId = OperationId.FromHexString(HexString2);
            
            // Set up initial heads file with full hex string
            var headsFilePath = Path.Combine(_basePath, "heads");
            _mockFileSystem.AddFile(headsFilePath, $"{HexString1}\n");

            // Act
            await _headStore.UpdateHeadOperationIdsAsync(new List<OperationId> { oldOperationId }, newOperationId);

            // Assert
            var content = await _mockFileSystem.File.ReadAllTextAsync(headsFilePath);
            Assert.Equal($"{HexString2}\n", content);
        }

        [Fact]
        public async Task UpdateHeadOperationIdsAsync_CASFailure_ThrowsInvalidOperationException()
        {
            // Arrange - Simulate concurrent modification by having different heads in file
            var expectedOldOperationId = OperationId.FromHexString(HexString1);
            var actualHeadInFile = OperationId.FromHexString(HexString2);
            var newOperationId = OperationId.FromHexString(HexString3);
            
            // Set up heads file with different content than expected (using full hex string)
            var headsFilePath = Path.Combine(_basePath, "heads");
            _mockFileSystem.AddFile(headsFilePath, $"{HexString2}\n");

            // Act & Assert - CAS should fail because current heads don't match expected
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _headStore.UpdateHeadOperationIdsAsync(new List<OperationId> { expectedOldOperationId }, newOperationId));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _headStore.UpdateHeadOperationIdsAsync(new List<OperationId> { expectedOldOperationId }, newOperationId));
        }

        [Fact]
        public async Task UpdateHeadOperationIdsAsync_MultipleOldHeads_ReplacesAllWithNew()
        {
            // Arrange
            var oldOperationId1 = OperationId.FromHexString(HexString1);
            var oldOperationId2 = OperationId.FromHexString(HexString2);
            var newOperationId = OperationId.FromHexString(HexString3);
            
            // Set up initial heads file with multiple heads (using full hex strings)
            var headsFilePath = Path.Combine(_basePath, "heads");
            _mockFileSystem.AddFile(headsFilePath, $"{HexString1}\n{HexString2}\n");

            // Act
            await _headStore.UpdateHeadOperationIdsAsync(
                new List<OperationId> { oldOperationId1, oldOperationId2 }, 
                newOperationId);

            // Assert
            var content = await _mockFileSystem.File.ReadAllTextAsync(headsFilePath);
            Assert.Equal($"{HexString3}\n", content);
        }

        [Fact]
        public async Task UpdateHeadOperationIdsAsync_PartialOldHeadsMatch_ThrowsInvalidOperationException()
        {
            // Arrange - File has different heads than expected
            var expectedOldOperationId1 = OperationId.FromHexString(HexString1);
            var expectedOldOperationId2 = OperationId.FromHexString(HexString2);
            var actualHeadInFile = OperationId.FromHexString(HexString3);
            var newOperationId = OperationId.FromHexString(HexString1);
            
            // Set up heads file with different head than what we expect
            var headsFilePath = Path.Combine(_basePath, "heads");
            _mockFileSystem.AddFile(headsFilePath, $"{HexString3}\n{HexString2}\n");

            // Act & Assert - Should fail because first head doesn't match what we expect
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _headStore.UpdateHeadOperationIdsAsync(new List<OperationId> { expectedOldOperationId1, expectedOldOperationId2 }, newOperationId));
        }

        [Fact]
        public async Task UpdateHeadOperationIdsAsync_CreatesDirectoryIfNotExists()
        {
            // Arrange
            var differentBasePath = "/different/path/.vcs";
            var headStore = new FileSystemOperationHeadStore(_mockFileSystem, differentBasePath);
            var newOperationId = OperationId.FromHexString(HexString1);

            // Act
            await headStore.UpdateHeadOperationIdsAsync(new List<OperationId>(), newOperationId);

            // Assert
            Assert.True(_mockFileSystem.Directory.Exists(differentBasePath));
            var headsFilePath = Path.Combine(differentBasePath, "heads");
            Assert.True(_mockFileSystem.FileExists(headsFilePath));
        }

        [Fact]
        public async Task UpdateHeadOperationIdsAsync_OrderDependentComparison_SucceedsWithSameOrder()
        {
            // Arrange
            var operationId1 = OperationId.FromHexString(HexString1);
            var operationId2 = OperationId.FromHexString(HexString2);
            var newOperationId = OperationId.FromHexString(HexString3);
            
            // Set up heads file with specific order (using full hex strings)
            var headsFilePath = Path.Combine(_basePath, "heads");
            _mockFileSystem.AddFile(headsFilePath, $"{HexString1}\n{HexString2}\n");

            // Act - Pass old heads in same order as in file
            await _headStore.UpdateHeadOperationIdsAsync(
                new List<OperationId> { operationId1, operationId2 }, 
                newOperationId);

            // Assert - Should succeed because order matches
            var content = await _mockFileSystem.File.ReadAllTextAsync(headsFilePath);
            Assert.Equal($"{HexString3}\n", content);
        }

        [Fact]
        public async Task UpdateHeadOperationIdsAsync_OrderDependentComparison_FailsWithDifferentOrder()
        {
            // Arrange
            var operationId1 = OperationId.FromHexString(HexString1);
            var operationId2 = OperationId.FromHexString(HexString2);
            var newOperationId = OperationId.FromHexString(HexString3);
            
            // Set up heads file with specific order (using full hex strings)
            var headsFilePath = Path.Combine(_basePath, "heads");
            _mockFileSystem.AddFile(headsFilePath, $"{HexString1}\n{HexString2}\n");

            // Act & Assert - Should fail because order doesn't match
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _headStore.UpdateHeadOperationIdsAsync(
                    new List<OperationId> { operationId2, operationId1 }, 
                    newOperationId));
        }

        [Fact]
        public async Task RoundTrip_WriteAndRead_MaintainsConsistency()
        {
            // Arrange
            var operationIds = new List<OperationId>
            {
                OperationId.FromHexString(HexString1),
                OperationId.FromHexString(HexString2),
                OperationId.FromHexString(HexString3)
            };

            // Act - Write multiple operations sequentially
            await _headStore.UpdateHeadOperationIdsAsync(new List<OperationId>(), operationIds[0]);
            await _headStore.UpdateHeadOperationIdsAsync(new List<OperationId> { operationIds[0] }, operationIds[1]);
            await _headStore.UpdateHeadOperationIdsAsync(new List<OperationId> { operationIds[1] }, operationIds[2]);

            // Read back
            var readOperationIds = await _headStore.GetHeadOperationIdsAsync();

            // Assert
            Assert.Single(readOperationIds);
            Assert.Equal(operationIds[2], readOperationIds[0]);
        }
    }
}