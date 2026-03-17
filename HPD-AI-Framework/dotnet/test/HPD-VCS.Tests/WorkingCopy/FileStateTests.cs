using System;
using System.Globalization;
using HPD.VCS.WorkingCopy;
using Xunit;

namespace HPD.VCS.Tests.WorkingCopy;

public class FileStateTests
{
    [Fact]
    public void FileState_Construction_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var fileType = FileType.NormalFile;
        var mTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var size = 1024L;

        // Act
        var fileState = new FileState(fileType, mTime, size);

        // Assert
        Assert.Equal(fileType, fileState.Type);
        Assert.Equal(mTime, fileState.MTimeUtc);
        Assert.Equal(size, fileState.Size);
    }

    [Fact]
    public void FileState_SymlinkConstruction_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var fileType = FileType.Symlink;
        var mTime = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var targetSize = 64L;

        // Act
        var fileState = new FileState(fileType, mTime, targetSize);

        // Assert
        Assert.Equal(fileType, fileState.Type);
        Assert.Equal(mTime, fileState.MTimeUtc);
        Assert.Equal(targetSize, fileState.Size);
    }

    [Theory]
    [InlineData(FileType.NormalFile, FileType.NormalFile, true)]  // Same type
    [InlineData(FileType.Symlink, FileType.Symlink, true)]       // Same type
    [InlineData(FileType.NormalFile, FileType.Symlink, false)]   // Different type
    [InlineData(FileType.Symlink, FileType.NormalFile, false)]   // Different type
    public void FileState_Equality_ShouldWorkCorrectly(FileType type1, FileType type2, bool expectedEqual)
    {
        // Arrange
        var time = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var size = 100L;
        var state1 = new FileState(type1, time, size);
        var state2 = new FileState(type2, time, size);

        // Act & Assert
        Assert.Equal(expectedEqual, state1 == state2);
        Assert.Equal(expectedEqual, state1.Equals(state2));
        if (expectedEqual)
        {
            Assert.Equal(state1.GetHashCode(), state2.GetHashCode());
        }
    }

    [Fact]
    public void FileState_EqualityWithDifferentTime_ShouldReturnFalse()
    {
        // Arrange
        var time1 = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var time2 = new DateTime(2024, 1, 1, 12, 0, 1, DateTimeKind.Utc); // 1 second difference
        var state1 = new FileState(FileType.NormalFile, time1, 100);
        var state2 = new FileState(FileType.NormalFile, time2, 100);

        // Act & Assert
        Assert.NotEqual(state1, state2);
        Assert.False(state1 == state2);
    }

    [Fact]
    public void FileState_EqualityWithDifferentSize_ShouldReturnFalse()
    {
        // Arrange
        var time = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var state1 = new FileState(FileType.NormalFile, time, 100);
        var state2 = new FileState(FileType.NormalFile, time, 200);

        // Act & Assert
        Assert.NotEqual(state1, state2);
        Assert.False(state1 == state2);    }

    [Theory]
    [InlineData(FileType.NormalFile, FileType.NormalFile, true, true, true, true, false, false)]   // All same
    [InlineData(FileType.NormalFile, FileType.Symlink, true, true, false, false, false, false)]    // Different type
    [InlineData(FileType.NormalFile, FileType.NormalFile, false, true, false, false, false, false)] // Different time
    [InlineData(FileType.NormalFile, FileType.NormalFile, true, false, false, false, false, false)] // Different size
    [InlineData(FileType.NormalFile, FileType.NormalFile, false, false, false, false, false, false)] // Different time and size
    [InlineData(FileType.NormalFile, FileType.NormalFile, true, true, false, false, true, false)] // Same data, but one is placeholder
    [InlineData(FileType.NormalFile, FileType.NormalFile, true, true, true, true, true, true)] // Both are placeholders
    public void FileState_IsClean_ShouldDetectChangesCorrectly(
        FileType currentType, FileType previousType,
        bool sameTime, bool sameSize, bool expectedUnchanged, bool expectedClean, 
        bool currentIsPlaceholder = false, bool previousIsPlaceholder = false)
    {
        // Arrange
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var baseSize = 100L;
        var currentTime = sameTime ? baseTime : baseTime.AddMinutes(1);
        var currentSize = sameSize ? baseSize : baseSize + 50;
        
        var currentState = new FileState(currentType, currentTime, currentSize, isPlaceholder: currentIsPlaceholder);
        var previousState = new FileState(previousType, baseTime, baseSize, isPlaceholder: previousIsPlaceholder);
        
        // Act
        var isClean = currentState.IsClean(previousState);
        var isUnchanged = currentState.Equals(previousState);

        // Assert
        Assert.Equal(expectedClean, isClean);
        Assert.Equal(expectedUnchanged, isUnchanged);
    }

    [Fact]
    public void FileState_Immutability_ShouldCreateNewInstancesForChanges()
    {
        // Arrange
        var originalTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var originalState = new FileState(FileType.NormalFile, originalTime, 500);

        // Act - Create modified copy using 'with' expression
        var modifiedState = originalState with { Size = 1000 };

        // Assert
        Assert.Equal(500, originalState.Size); // Original unchanged
        Assert.Equal(1000, modifiedState.Size); // Modified copy has new size
        Assert.Equal(originalState.Type, modifiedState.Type); // Other properties preserved
        Assert.Equal(originalState.MTimeUtc, modifiedState.MTimeUtc);
    }

    [Fact]
    public void FileState_Placeholder_ShouldCreateDefaultState()
    {
        // Act
        var placeholder = FileState.Placeholder();

        // Assert
        Assert.Equal(FileType.NormalFile, placeholder.Type);
        Assert.Equal(DateTimeOffset.UnixEpoch, placeholder.MTimeUtc);
        Assert.Equal(0, placeholder.Size);
    }

    [Fact]
    public void FileState_ForFile_ShouldCreateNormalFileState()
    {
        // Arrange
        var mTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var size = 2048L;

        // Act
        var fileState = FileState.ForFile(mTime, size);

        // Assert
        Assert.Equal(FileType.NormalFile, fileState.Type);
        Assert.Equal(mTime, fileState.MTimeUtc);
        Assert.Equal(size, fileState.Size);
    }

    [Fact]
    public void FileState_ForSymlink_ShouldCreateSymlinkState()
    {
        // Arrange
        var mTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var targetSize = 32L;

        // Act
        var symlinkState = FileState.ForSymlink(mTime, targetSize);

        // Assert
        Assert.Equal(FileType.Symlink, symlinkState.Type);
        Assert.Equal(mTime, symlinkState.MTimeUtc);
        Assert.Equal(targetSize, symlinkState.Size);
        Assert.False(symlinkState.IsPlaceholder);
    }

    [Fact]
    public void FileState_ToString_ShouldProvideReadableFormat()
    {
        // Arrange
        var time = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var normalFile = new FileState(FileType.NormalFile, time, 1024);
        var symlink = new FileState(FileType.Symlink, time, 64);

        // Act
        var normalFileString = normalFile.ToString();
        var symlinkString = symlink.ToString();

        // Assert
        Assert.Contains("NormalFile", normalFileString);
        Assert.Contains("2024-01-01", normalFileString);
        Assert.Contains("1024", normalFileString);

        Assert.Contains("Symlink", symlinkString);
        Assert.Contains("2024-01-01", symlinkString);
        Assert.Contains("64", symlinkString);
    }

    [Theory]
    [InlineData(0)]           // Zero size
    [InlineData(1)]           // Tiny file
    [InlineData(1024)]        // 1KB
    [InlineData(1048576)]     // 1MB
    [InlineData(long.MaxValue)] // Maximum size
    public void FileState_EdgeCaseSizes_ShouldHandleCorrectly(long size)
    {
        // Arrange
        var time = DateTime.UtcNow;

        // Act
        var fileState = new FileState(FileType.NormalFile, time, size);

        // Assert
        Assert.Equal(size, fileState.Size);
        Assert.Equal(FileType.NormalFile, fileState.Type);
        Assert.Equal(time, fileState.MTimeUtc);
    }

    [Theory]
    [InlineData("0001-01-01")]  // Minimum DateTime
    [InlineData("2024-01-01")]  // Normal date
    [InlineData("9999-12-31")]  // Maximum reasonable DateTime
    public void FileState_EdgeCaseDates_ShouldHandleCorrectly(string dateString)
    {
        // Arrange
        var time = DateTime.Parse(dateString + " 12:00:00", null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        // Act
        var fileState = new FileState(FileType.NormalFile, time, 100);

        // Assert
        Assert.Equal(time, fileState.MTimeUtc);
        Assert.Equal(FileType.NormalFile, fileState.Type);
        Assert.Equal(100, fileState.Size);
    }

    [Fact]
    public void FileState_ComparisonWithNull_ShouldHandleCorrectly()
    {
        // Arrange
        var fileState = new FileState(FileType.NormalFile, DateTime.UtcNow, 100);        // Act & Assert
        Assert.False(fileState.Equals((object?)null));
        Assert.False(fileState.Equals((FileState?)null));
        Assert.True(fileState.Equals((object)fileState));
    }

    [Fact]
    public void FileState_CleanCheckWithSameState_ShouldReturnTrue()
    {
        // Arrange
        var time = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var state1 = new FileState(FileType.NormalFile, time, 100);
        var state2 = new FileState(FileType.NormalFile, time, 100);

        // Act
        var isClean = state1.IsClean(state2);

        // Assert
        Assert.True(isClean);
    }

    [Fact]
    public void FileState_MultipleMtimeGranularityScenarios_ShouldBehaveConsistently()
    {
        // Arrange
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var baseState = new FileState(FileType.NormalFile, baseTime, 100);

        // Test various time differences
        var scenarios = new[]
        {
            (baseTime.AddMilliseconds(1), false),   // 1ms difference - should detect change
            (baseTime.AddMilliseconds(500), false), // 500ms difference - should detect change  
            (baseTime.AddSeconds(1), false),        // 1s difference - should detect change
            (baseTime, true)                        // Same time - should be clean
        };

        foreach (var (testTime, expectedClean) in scenarios)
        {
            // Act
            var testState = new FileState(FileType.NormalFile, testTime, 100);
            var isClean = testState.IsClean(baseState);

            // Assert
            Assert.Equal(expectedClean, isClean);
        }
    }
}
