using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.VCS.Configuration;

namespace HPD.VCS.Configuration;

/// <summary>
/// Raw DTO for deserializing repository configuration without validation.
/// </summary>
internal class RawRepositoryConfig
{
    [JsonPropertyName("workingCopy")]
    public RawWorkingCopyConfig? WorkingCopy { get; set; }
}

/// <summary>
/// Raw DTO for deserializing working copy configuration without validation.
/// </summary>
internal class RawWorkingCopyConfig
{
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }
}

/// <summary>
/// Manages repository configuration file operations.
/// </summary>
public class ConfigurationManager
{
    private readonly IFileSystem _fileSystem;
    private readonly string _configFilePath;

    /// <summary>
    /// Initializes a new instance of the ConfigurationManager class.
    /// </summary>
    /// <param name="fileSystem">The file system abstraction</param>
    /// <param name="repositoryPath">The repository root path</param>
    public ConfigurationManager(IFileSystem fileSystem, string repositoryPath)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _configFilePath = _fileSystem.Path.Combine(repositoryPath, ".hpd", "config.json");
    }    /// <summary>
    /// Creates a default configuration file for a new repository.
    /// </summary>
    /// <param name="mode">The initial working copy mode</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public async Task CreateDefaultConfigAsync(WorkingCopyMode mode = WorkingCopyMode.Explicit)
    {
        var config = new RepositoryConfig
        {
            WorkingCopy = new WorkingCopyConfig
            {
                Mode = mode.ToModeString()
            }
        };

        await WriteConfigAsync(config);
    }    /// <summary>
    /// Reads the repository configuration from the config file.
    /// </summary>
    /// <returns>The repository configuration</returns>
    /// <exception cref="FileNotFoundException">Thrown when the configuration file doesn't exist</exception>
    /// <exception cref="InvalidOperationException">Thrown when the configuration file is invalid</exception>
    public async Task<RepositoryConfig> ReadConfigAsync()
    {
        if (!_fileSystem.File.Exists(_configFilePath))
        {
            throw new FileNotFoundException($"Configuration file not found: {_configFilePath}");
        }

        try
        {
            var jsonContent = await _fileSystem.File.ReadAllTextAsync(_configFilePath);
            
            // First deserialize to a raw DTO to avoid property validation
            var rawConfig = JsonSerializer.Deserialize<RawRepositoryConfig>(jsonContent);
            
            if (rawConfig == null)
            {
                throw new InvalidOperationException($"Failed to deserialize configuration from {_configFilePath}");
            }

            // Validate the raw configuration first
            ValidateRawConfig(rawConfig);
            
            // Now create the real config objects with validated values
            var config = new RepositoryConfig
            {
                WorkingCopy = new WorkingCopyConfig
                {
                    Mode = rawConfig.WorkingCopy!.Mode!
                }
            };
            
            return config;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON in configuration file {_configFilePath}: {ex.Message}", ex);
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            throw new InvalidOperationException($"Failed to read configuration file {_configFilePath}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Writes the repository configuration to the config file.
    /// </summary>
    /// <param name="config">The configuration to write</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public async Task WriteConfigAsync(RepositoryConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        
        ValidateConfig(config);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var jsonContent = JsonSerializer.Serialize(config, options);
        
        // Ensure the directory exists
        var configDirectory = _fileSystem.Path.GetDirectoryName(_configFilePath);
        if (!string.IsNullOrEmpty(configDirectory) && !_fileSystem.Directory.Exists(configDirectory))
        {
            _fileSystem.Directory.CreateDirectory(configDirectory);
        }

        await _fileSystem.File.WriteAllTextAsync(_configFilePath, jsonContent);
    }    /// <summary>
    /// Gets the working copy mode from the configuration.
    /// </summary>
    /// <returns>The working copy mode</returns>
    public async Task<WorkingCopyMode> GetWorkingCopyModeAsync()
    {
        var config = await ReadConfigAsync();
        return config.WorkingCopy!.Mode!.ToWorkingCopyMode();
    }

    /// <summary>
    /// Updates the working copy mode in the configuration.
    /// </summary>
    /// <param name="mode">The new working copy mode</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public async Task SetWorkingCopyModeAsync(WorkingCopyMode mode)
    {
        var config = await ReadConfigAsync();
        config.WorkingCopy!.Mode = mode.ToModeString();
        await WriteConfigAsync(config);
    }    /// <summary>
    /// Validates the repository configuration.
    /// </summary>
    /// <param name="config">The configuration to validate</param>
    /// <exception cref="InvalidOperationException">Thrown when the configuration is invalid</exception>
    private static void ValidateConfig(RepositoryConfig config)
    {
        if (config.WorkingCopy == null)
        {
            throw new InvalidOperationException("Configuration must contain a workingCopy section");
        }

        if (string.IsNullOrWhiteSpace(config.WorkingCopy.Mode))
        {
            throw new InvalidOperationException("Working copy mode cannot be null or empty");
        }        try
        {
            // This will throw an ArgumentException if the mode is invalid
            config.WorkingCopy.Mode!.ToWorkingCopyMode();
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"Invalid working copy mode in configuration: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Validates the raw repository configuration DTO.
    /// </summary>
    /// <param name="config">The raw configuration to validate</param>
    /// <exception cref="InvalidOperationException">Thrown when the configuration is invalid</exception>
    private static void ValidateRawConfig(RawRepositoryConfig config)
    {
        if (config.WorkingCopy == null)
        {
            throw new InvalidOperationException("Configuration must contain a workingCopy section");
        }

        if (string.IsNullOrWhiteSpace(config.WorkingCopy.Mode))
        {
            throw new InvalidOperationException("Working copy mode cannot be null or empty");
        }        try
        {
            // This will throw an ArgumentException if the mode is invalid
            config.WorkingCopy.Mode!.ToWorkingCopyMode();
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"Invalid working copy mode in configuration: {ex.Message}", ex);
        }
    }
}
