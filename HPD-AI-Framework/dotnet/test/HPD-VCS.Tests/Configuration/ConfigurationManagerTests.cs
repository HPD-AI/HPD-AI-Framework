using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using HPD.VCS.Configuration;
using Xunit;

namespace HPD.VCS.Tests.Configuration;

public class ConfigurationManagerTests : IDisposable
{
    private readonly MockFileSystem _mockFileSystem;
    private readonly string _repoPath;
    private readonly ConfigurationManager _configManager;

    public ConfigurationManagerTests()
    {
        _mockFileSystem = new MockFileSystem();
        _repoPath = "/repo";
        _mockFileSystem.AddDirectory(_repoPath);
        _mockFileSystem.AddDirectory(Path.Combine(_repoPath, ".hpd"));
        
        _configManager = new ConfigurationManager(_mockFileSystem, _repoPath);
    }

    public void Dispose()
    {
        // Nothing to dispose for MockFileSystem
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldSucceed()
    {
        // Act & Assert - Constructor should not throw
        var manager = new ConfigurationManager(_mockFileSystem, _repoPath);
        Assert.NotNull(manager);
    }

    [Fact]
    public void Constructor_WithNullFileSystem_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ConfigurationManager(null!, _repoPath));
    }

    [Fact]
    public void Constructor_WithNullRepoPath_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ConfigurationManager(_mockFileSystem, null!));
    }

    #endregion

    #region CreateDefaultConfigAsync Tests

    [Fact]
    public async Task CreateDefaultConfigAsync_ShouldCreateConfigFile()
    {
        // Act
        await _configManager.CreateDefaultConfigAsync();

        // Assert
        var configPath = Path.Combine(_repoPath, ".hpd", "config.json");
        Assert.True(_mockFileSystem.File.Exists(configPath));
    }

    [Fact]
    public async Task CreateDefaultConfigAsync_ShouldCreateExplicitMode()
    {
        // Act
        await _configManager.CreateDefaultConfigAsync();

        // Assert
        var config = await _configManager.ReadConfigAsync();
        Assert.Equal("explicit", config.WorkingCopy.Mode);
    }

    [Fact]
    public async Task CreateDefaultConfigAsync_CalledMultipleTimes_ShouldOverwriteExisting()
    {
        // Arrange - Create initial config
        await _configManager.CreateDefaultConfigAsync();
          // Manually modify the config to something else
        var customConfig = new RepositoryConfig
        {
            WorkingCopy = new WorkingCopyConfig { Mode = "live" }
        };
        await _configManager.WriteConfigAsync(customConfig);
        
        // Verify custom config was written
        var modifiedConfig = await _configManager.ReadConfigAsync();
        Assert.Equal("live", modifiedConfig.WorkingCopy.Mode);

        // Act - Create default config again
        await _configManager.CreateDefaultConfigAsync();

        // Assert - Should be back to default
        var finalConfig = await _configManager.ReadConfigAsync();
        Assert.Equal("explicit", finalConfig.WorkingCopy.Mode);
    }

    [Fact]
    public async Task CreateDefaultConfigAsync_WithouthpdDirectory_ShouldCreateDirectory()
    {
        // Arrange - Remove .hpd directory
        var hpdPath = Path.Combine(_repoPath, ".hpd");
        _mockFileSystem.Directory.Delete(hpdPath);
        Assert.False(_mockFileSystem.Directory.Exists(hpdPath));

        // Act
        await _configManager.CreateDefaultConfigAsync();

        // Assert
        Assert.True(_mockFileSystem.Directory.Exists(hpdPath));
        var configPath = Path.Combine(hpdPath, "config.json");
        Assert.True(_mockFileSystem.File.Exists(configPath));
    }

    #endregion

    #region WriteConfigAsync Tests

    [Fact]
    public async Task WriteConfigAsync_WithExplicitMode_ShouldWriteCorrectJson()
    {        // Arrange
        var config = new RepositoryConfig
        {
            WorkingCopy = new WorkingCopyConfig { Mode = "explicit" }
        };

        // Act
        await _configManager.WriteConfigAsync(config);

        // Assert
        var configPath = Path.Combine(_repoPath, ".hpd", "config.json");
        Assert.True(_mockFileSystem.File.Exists(configPath));
        
        var jsonContent = await _mockFileSystem.File.ReadAllTextAsync(configPath);
        Assert.Contains("\"workingCopy\"", jsonContent);
        Assert.Contains("\"mode\": \"explicit\"", jsonContent);
    }

    [Fact]
    public async Task WriteConfigAsync_WithLiveMode_ShouldWriteCorrectJson()
    {        // Arrange
        var config = new RepositoryConfig
        {
            WorkingCopy = new WorkingCopyConfig { Mode = "live" }
        };

        // Act
        await _configManager.WriteConfigAsync(config);

        // Assert
        var configPath = Path.Combine(_repoPath, ".hpd", "config.json");
        var jsonContent = await _mockFileSystem.File.ReadAllTextAsync(configPath);
        Assert.Contains("\"mode\": \"live\"", jsonContent);
    }

    [Fact]
    public async Task WriteConfigAsync_WithNullConfig_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _configManager.WriteConfigAsync(null!));
    }

    [Fact]
    public async Task WriteConfigAsync_ShouldCreatehpdDirectoryIfNotExists()
    {
        // Arrange - Remove .hpd directory
        var hpdPath = Path.Combine(_repoPath, ".hpd");
        _mockFileSystem.Directory.Delete(hpdPath);
        
        var config = new RepositoryConfig
        {
            WorkingCopy = new WorkingCopyConfig { Mode = "explicit" }
        };

        // Act
        await _configManager.WriteConfigAsync(config);

        // Assert
        Assert.True(_mockFileSystem.Directory.Exists(hpdPath));
        var configPath = Path.Combine(hpdPath, "config.json");
        Assert.True(_mockFileSystem.File.Exists(configPath));
    }

    #endregion

    #region ReadConfigAsync Tests    [Fact]    [Fact]
    public async Task ReadConfigAsync_WithValidExplicitConfig_ShouldReturnCorrectConfig()
    {
        // Arrange
        var originalConfig = new RepositoryConfig
        {
            WorkingCopy = new WorkingCopyConfig { Mode = "explicit" }
        };
        await _configManager.WriteConfigAsync(originalConfig);

        // Act
        var readConfig = await _configManager.ReadConfigAsync();

        // Assert
        Assert.Equal("explicit", readConfig.WorkingCopy.Mode);
    }    [Fact]
    public async Task ReadConfigAsync_WithValidLiveConfig_ShouldReturnCorrectConfig()
    {
        // Arrange
        var originalConfig = new RepositoryConfig
        {
            WorkingCopy = new WorkingCopyConfig { Mode = "live" }
        };
        await _configManager.WriteConfigAsync(originalConfig);

        // Act
        var readConfig = await _configManager.ReadConfigAsync();

        // Assert
        Assert.Equal("live", readConfig.WorkingCopy.Mode);
    }

    [Fact]
    public async Task ReadConfigAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
    {
        // Arrange - Ensure config file doesn't exist
        var configPath = Path.Combine(_repoPath, ".hpd", "config.json");
        if (_mockFileSystem.File.Exists(configPath))
        {
            _mockFileSystem.File.Delete(configPath);
        }

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => 
            _configManager.ReadConfigAsync());
    }

    [Fact]
    public async Task ReadConfigAsync_WithInvalidJson_ShouldThrowInvalidOperationException()
    {
        // Arrange - Write invalid JSON to config file
        var configPath = Path.Combine(_repoPath, ".hpd", "config.json");
        await _mockFileSystem.File.WriteAllTextAsync(configPath, "{ invalid json content");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            _configManager.ReadConfigAsync());
    }

    [Fact]
    public async Task ReadConfigAsync_WithMissingWorkingCopySection_ShouldThrowInvalidOperationException()
    {
        // Arrange - Write JSON without workingCopy section
        var configPath = Path.Combine(_repoPath, ".hpd", "config.json");
        await _mockFileSystem.File.WriteAllTextAsync(configPath, "{ \"someOtherSection\": {} }");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            _configManager.ReadConfigAsync());
    }

    [Fact]
    public async Task ReadConfigAsync_WithMissingModeField_ShouldThrowInvalidOperationException()
    {
        // Arrange - Write JSON without mode field
        var configPath = Path.Combine(_repoPath, ".hpd", "config.json");
        await _mockFileSystem.File.WriteAllTextAsync(configPath, "{ \"workingCopy\": { \"someOtherField\": \"value\" } }");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            _configManager.ReadConfigAsync());
    }

    #endregion

    #region Round-trip Tests    [Fact]
    public async Task ConfigRoundTrip_ExplicitMode_ShouldPreserveValues()
    {
        // Arrange
        var originalConfig = new RepositoryConfig
        {
            WorkingCopy = new WorkingCopyConfig { Mode = "explicit" }
        };

        // Act
        await _configManager.WriteConfigAsync(originalConfig);
        var readConfig = await _configManager.ReadConfigAsync();

        // Assert
        Assert.Equal(originalConfig.WorkingCopy.Mode, readConfig.WorkingCopy.Mode);
    }    [Fact]
    public async Task ConfigRoundTrip_LiveMode_ShouldPreserveValues()
    {
        // Arrange
        var originalConfig = new RepositoryConfig
        {
            WorkingCopy = new WorkingCopyConfig { Mode = "live" }
        };

        // Act
        await _configManager.WriteConfigAsync(originalConfig);
        var readConfig = await _configManager.ReadConfigAsync();

        // Assert
        Assert.Equal(originalConfig.WorkingCopy.Mode, readConfig.WorkingCopy.Mode);
    }    [Fact]
    public async Task ConfigRoundTrip_MultipleWrites_ShouldKeepLatestValue()
    {
        // Arrange
        var explicitConfig = new RepositoryConfig
        {
            WorkingCopy = new WorkingCopyConfig { Mode = "explicit" }
        };
        var liveConfig = new RepositoryConfig
        {
            WorkingCopy = new WorkingCopyConfig { Mode = "live" }
        };

        // Act
        await _configManager.WriteConfigAsync(explicitConfig);
        var firstRead = await _configManager.ReadConfigAsync();
        
        await _configManager.WriteConfigAsync(liveConfig);
        var secondRead = await _configManager.ReadConfigAsync();
        
        await _configManager.WriteConfigAsync(explicitConfig);
        var thirdRead = await _configManager.ReadConfigAsync();

        // Assert
        Assert.Equal("explicit", firstRead.WorkingCopy.Mode);
        Assert.Equal("live", secondRead.WorkingCopy.Mode);
        Assert.Equal("explicit", thirdRead.WorkingCopy.Mode);
    }

    #endregion
}

public class RepositoryConfigTests
{
    #region Constructor Tests    [Fact]
    public void Constructor_WithValidWorkingCopyConfig_ShouldSucceed()
    {
        // Arrange
        var workingCopyConfig = new WorkingCopyConfig { Mode = "explicit" };

        // Act
        var repositoryConfig = new RepositoryConfig
        {
            WorkingCopy = workingCopyConfig
        };

        // Assert
        Assert.Equal(workingCopyConfig, repositoryConfig.WorkingCopy);
    }

    [Fact]
    public void Constructor_WithNullWorkingCopyConfig_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RepositoryConfig { WorkingCopy = null! });
    }

    #endregion
}

public class WorkingCopyConfigTests
{
    #region Constructor Tests    [Fact]
    public void Constructor_WithValidMode_ShouldSucceed()
    {
        // Arrange & Act
        var config = new WorkingCopyConfig { Mode = "explicit" };

        // Assert
        Assert.Equal("explicit", config.Mode);
    }

    [Fact]
    public void Constructor_WithNullMode_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new WorkingCopyConfig { Mode = null! });
    }

    [Fact]
    public void Constructor_WithEmptyMode_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new WorkingCopyConfig { Mode = "" });
    }

    [Fact]
    public void Constructor_WithWhitespaceMode_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new WorkingCopyConfig { Mode = "   " });
    }

    #endregion

    #region ToWorkingCopyMode Tests    [Fact]
    public void ToWorkingCopyMode_WithExplicit_ShouldReturnExplicitMode()
    {
        // Arrange
        var config = new WorkingCopyConfig { Mode = "explicit" };

        // Act
        var mode = config.Mode.ToWorkingCopyMode();

        // Assert
        Assert.Equal(WorkingCopyMode.Explicit, mode);
    }

    [Fact]
    public void ToWorkingCopyMode_WithLive_ShouldReturnLiveMode()
    {
        // Arrange
        var config = new WorkingCopyConfig { Mode = "live" };

        // Act
        var mode = config.Mode.ToWorkingCopyMode();

        // Assert
        Assert.Equal(WorkingCopyMode.Live, mode);
    }

    [Fact]
    public void ToWorkingCopyMode_WithUnknownMode_ShouldThrowArgumentException()
    {
        // Arrange
        var config = new WorkingCopyConfig { Mode = "unknown" };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => config.Mode.ToWorkingCopyMode());
        Assert.Contains("unknown", exception.Message);
    }

    [Fact]
    public void ToWorkingCopyMode_WithCaseVariations_ShouldBeCaseSensitive()
    {
        // Arrange
        var explicitUpper = new WorkingCopyConfig { Mode = "EXPLICIT" };
        var liveUpper = new WorkingCopyConfig { Mode = "LIVE" };

        // Act & Assert - Should throw because it's case sensitive
        Assert.Throws<ArgumentException>(() => explicitUpper.Mode.ToWorkingCopyMode());
        Assert.Throws<ArgumentException>(() => liveUpper.Mode.ToWorkingCopyMode());
    }

    #endregion
}
