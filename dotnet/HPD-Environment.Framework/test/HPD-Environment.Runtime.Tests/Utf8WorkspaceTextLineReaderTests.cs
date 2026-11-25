using System.Text;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime.Workspaces;

namespace HPD.Environment.Runtime.Tests;

public sealed class Utf8WorkspaceTextLineReaderTests
{
    [Fact]
    public async Task Host_directory_store_reads_cr_lines_through_shared_reader()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"hpd-line-reader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await using var store = new HostDirectoryWorkspaceFileStore(
                new WorkspaceRootId("workspace"),
                root,
                AccessMode.ReadWrite,
                new WorkspacePathSemantics(
                    OperatingSystem.IsLinux(),
                    SymlinkEvaluationPolicy.DenySymlinks));
            var path = new WorkspacePath(
                new WorkspaceRootId("workspace"),
                "lines.txt");
            var created = await store.WriteTextAsync(
                path,
                "first\rsecond\rthird",
                WorkspaceWriteCondition.RequireCreate);
            Assert.Equal(
                WorkspaceMutationOutcome.Succeeded,
                created.Outcome);

            var result = await store.ReadTextAsync(
                path,
                new WorkspaceTextReadOptions
                {
                    StartLine = 2,
                    MaxLines = 1,
                });

            Assert.NotNull(result);
            Assert.Equal("second\r", result.Content);
            Assert.Equal(2, result.StartLine);
            Assert.Equal(2, result.EndLine);
            Assert.Equal(3, result.TotalLines);

            var updated = await store.WriteTextAsync(
                path,
                "updated\n",
                new WorkspaceWriteCondition
                {
                    ExpectedVersion = created.Version,
                });
            Assert.Equal(
                WorkspaceMutationOutcome.Succeeded,
                updated.Outcome);
            Assert.Equal(
                "updated\n",
                await File.ReadAllTextAsync(Path.Combine(root, "lines.txt")));
            var final = await store.ReadTextAsync(
                path,
                new WorkspaceTextReadOptions());
            Assert.NotNull(final);
            Assert.Equal("updated\n", final.Content);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Reads_cr_lf_and_crlf_lines_with_one_line_numbering_scheme()
    {
        var source = Encoding.UTF8.GetBytes(
            "first\rsecond\r\nthird\nfourth");

        var result = Utf8WorkspaceTextLineReader.Read(
            source,
            new WorkspaceTextReadOptions
            {
                StartLine = 2,
                MaxLines = 2,
                MaxBytes = 1024,
            });

        Assert.Equal("second\r\nthird\n", result.Content);
        Assert.Equal(2, result.StartLine);
        Assert.Equal(3, result.EndLine);
        Assert.Equal(4, result.TotalLines);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Final_lone_cr_line_is_addressable()
    {
        var source = Encoding.UTF8.GetBytes("first\rsecond\r");

        var result = Utf8WorkspaceTextLineReader.Read(
            source,
            new WorkspaceTextReadOptions
            {
                StartLine = 2,
                MaxLines = 1,
                MaxBytes = 1024,
            });

        Assert.Equal("second\r", result.Content);
        Assert.Equal(2, result.StartLine);
        Assert.Equal(2, result.EndLine);
        Assert.Equal(2, result.TotalLines);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Byte_limit_does_not_split_a_utf8_sequence()
    {
        var source = Encoding.UTF8.GetBytes("a\r界");

        var result = Utf8WorkspaceTextLineReader.Read(
            source,
            new WorkspaceTextReadOptions
            {
                StartLine = 2,
                MaxLines = 1,
                MaxBytes = 2,
            });

        Assert.Equal(string.Empty, result.Content);
        Assert.Equal(1, result.EndLine);
        Assert.True(result.Truncated);
    }
}
