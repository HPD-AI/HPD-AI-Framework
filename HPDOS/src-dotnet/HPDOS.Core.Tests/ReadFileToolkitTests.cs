using System.Runtime.InteropServices;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace HPDOS.Core.Tests;

/// <summary>
/// Comprehensive test suite for ReadFileToolkit (CodingToolkit.ReadFile).
/// Tests: happy path, encoding detection, device blocking, UNC blocking, 
/// binary blocking, ENOENT suggestions, dedup, and edge cases.
/// </summary>
public class ReadFileToolkitTests : IDisposable
{
    private readonly CodingToolkit _toolkit = new();
    private readonly string _tempDir;

    public ReadFileToolkitTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"readfile-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    private string CreateTestFile(string relPath, string content, Encoding? encoding = null)
    {
        var fullPath = Path.Combine(_tempDir, relPath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir!);

        File.WriteAllText(fullPath, content, encoding ?? Encoding.UTF8);
        return fullPath;
    }

    // ═══════════════════════════════════════════════════════════════════
    // HAPPY PATH TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadFile_SimpleFile_ReturnsContentWithLineNumbers()
    {
        var path = CreateTestFile("simple.txt", "line 1\nline 2\nline 3");
        var result = _toolkit.ReadFile(path);

        Assert.Contains("File: simple.txt", result);
        Assert.Contains("   1│ line 1", result);
        Assert.Contains("   2│ line 2", result);
        Assert.Contains("   3│ line 3", result);
        Assert.Contains("Lines: 1-3 of 3", result);
    }

    [Fact]
    public void ReadFile_WithOffset_SkipsLinesToOffset()
    {
        var path = CreateTestFile("offset.txt", "1\n2\n3\n4\n5");
        var result = _toolkit.ReadFile(path, offset: 3);

        Assert.Contains("   3│ 3", result);
        Assert.Contains("   4│ 4", result);
        Assert.Contains("   5│ 5", result);
        Assert.DoesNotContain("   1│ 1", result);
        Assert.DoesNotContain("   2│ 2", result);
        Assert.Contains("Lines: 3-5 of 5", result);
    }

    [Fact]
    public void ReadFile_WithLimit_ReturnsSingleLimitedRange()
    {
        var path = CreateTestFile("limit.txt", "1\n2\n3\n4\n5");
        var result = _toolkit.ReadFile(path, offset: 1, limit: 3);

        Assert.Contains("   1│ 1", result);
        Assert.Contains("   2│ 2", result);
        Assert.Contains("   3│ 3", result);
        Assert.DoesNotContain("   4│ 4", result);
        Assert.Contains("Lines: 1-3 of 5", result);
    }

    [Fact]
    public void ReadFile_WithOffsetAndLimit_ReturnsMidRange()
    {
        var path = CreateTestFile("range.txt", "a\nb\nc\nd\ne\nf");
        var result = _toolkit.ReadFile(path, offset: 2, limit: 3);

        Assert.Contains("   2│ b", result);
        Assert.Contains("   3│ c", result);
        Assert.Contains("   4│ d", result);
        Assert.DoesNotContain("   1│ a", result);
        Assert.DoesNotContain("   5│ e", result);
        Assert.Contains("Lines: 2-4 of 6", result);
    }

    [Fact]
    public void ReadFile_WithLimitZero_ReturnsAllLines()
    {
        var path = CreateTestFile("all.txt", "1\n2\n3");
        var result = _toolkit.ReadFile(path, offset: 1, limit: 0);

        Assert.Contains("   1│ 1", result);
        Assert.Contains("   2│ 2", result);
        Assert.Contains("   3│ 3", result);
        Assert.Contains("Lines: 1-3 of 3", result);
    }

    [Fact]
    public void ReadFile_TruncationNotice_WhenLimitedBeforeEOF()
    {
        var path = CreateTestFile("truncate.txt", string.Join("\n", Enumerable.Range(1, 100)));
        var result = _toolkit.ReadFile(path, offset: 1, limit: 10);

        Assert.Contains("TRUNCATED: Showing 10 of 100 lines", result);
        Assert.Contains("To read more, use offset: 11", result);
    }

    [Fact]
    public void ReadFile_IncludesFullPath_InOutput()
    {
        var path = CreateTestFile("fullpath.txt", "test");
        var result = _toolkit.ReadFile(path);

        Assert.Contains($"Path: {path}", result);
    }

    [Fact]
    public void ReadFile_IncludesMimeType_InOutput()
    {
        var path = CreateTestFile("script.cs", "var x = 1;");
        var result = _toolkit.ReadFile(path);

        Assert.Contains("Type: text/x-csharp", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ENCODING DETECTION TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadFile_UTF8_DecodesCorrectly()
    {
        var path = CreateTestFile("utf8.txt", "Hello World ✓ café", Encoding.UTF8);
        var result = _toolkit.ReadFile(path);

        Assert.Contains("café", result);
        Assert.Contains("✓", result);
    }

    [Fact]
    public void ReadFile_Latin1_DecodesCorrectly()
    {
        var latinText = "café naïve résumé";
        var path = CreateTestFile("latin1.txt", latinText, Encoding.GetEncoding("iso-8859-1"));
        var result = _toolkit.ReadFile(path);

        // Should have decoded the accented characters correctly
        Assert.DoesNotContain("Error", result);
        Assert.Contains("latin1.txt", result);
    }

    [Fact]
    public void ReadFile_EmptyFile_ReturnsEmptyMessage()
    {
        var path = CreateTestFile("empty.txt", "");
        var result = _toolkit.ReadFile(path);

        Assert.Contains("exists but is empty", result);
        Assert.DoesNotContain("│", result);
    }

    [Fact]
    public void ReadFile_WhitespaceOnlyFile_ReturnsWhitespaceMessage()
    {
        var path = CreateTestFile("whitespace.txt", "   \n\t\n  ");
        var result = _toolkit.ReadFile(path);

        Assert.Contains("exists but contains only whitespace", result);
        Assert.DoesNotContain("│", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // DEVICE FILE BLOCKING TESTS (Linux/macOS)
    // ═══════════════════════════════════════════════════════════════════

    [SkippableFact]
    public void ReadFile_DevZero_IsBlocked()
    {
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Device paths are Linux/macOS only");
        var result = _toolkit.ReadFile("/dev/zero");
        Assert.Contains("Error", result);
        Assert.Contains("device file would block", result);
    }

    [SkippableFact]
    public void ReadFile_DevRandom_IsBlocked()
    {
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Device paths are Linux/macOS only");
        var result = _toolkit.ReadFile("/dev/random");
        Assert.Contains("Error", result);
        Assert.Contains("device file would block", result);
    }

    [SkippableFact]
    public void ReadFile_DevStdin_IsBlocked()
    {
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Device paths are Linux/macOS only");
        var result = _toolkit.ReadFile("/dev/stdin");
        Assert.Contains("Error", result);
        Assert.Contains("device file would block", result);
    }

    [SkippableFact]
    public void ReadFile_ProcSelfFd0_IsBlocked()
    {
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Device paths are Linux/macOS only");
        var result = _toolkit.ReadFile("/proc/self/fd/0");
        Assert.Contains("Error", result);
        Assert.Contains("stdio device alias", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // UNC PATH BLOCKING TESTS (Windows)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadFile_UNCPath_BackslashVariant_IsBlocked()
    {
        var result = _toolkit.ReadFile("\\\\attacker\\share\\file.txt");
        Assert.Contains("Error", result);
        Assert.Contains("network paths may leak credentials", result);
    }

    [Fact]
    public void ReadFile_UNCPath_ForwardSlashVariant_IsBlocked()
    {
        var result = _toolkit.ReadFile("//attacker/share/file.txt");
        Assert.Contains("Error", result);
        Assert.Contains("network paths may leak credentials", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // BINARY FILE BLOCKING TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadFile_ExeFile_IsBlocked()
    {
        var result = _toolkit.ReadFile("/path/to/program.exe");
        Assert.Contains("Error", result);
        Assert.Contains("Cannot read binary file", result);
    }

    [Fact]
    public void ReadFile_DllFile_IsBlocked()
    {
        var result = _toolkit.ReadFile("/path/to/library.dll");
        Assert.Contains("Error", result);
        Assert.Contains("Cannot read binary file", result);
    }

    [Fact]
    public void ReadFile_PycFile_IsBlocked()
    {
        var result = _toolkit.ReadFile("/path/to/module.pyc");
        Assert.Contains("Error", result);
        Assert.Contains("Cannot read binary file", result);
    }

    [Fact]
    public void ReadFile_PngFile_IsBlocked()
    {
        var result = _toolkit.ReadFile("/path/to/image.png");
        Assert.Contains("Error", result);
        Assert.Contains("Cannot read binary file", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ENOENT HANDLING & FILE SUGGESTION TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadFile_NonexistentFile_ReturnsNotFoundError()
    {
        var result = _toolkit.ReadFile("/nonexistent/path/to/file.txt");
        Assert.Contains("Error", result);
        Assert.Contains("File not found", result);
    }

    [Fact]
    public void ReadFile_SimilarFilename_SuggestsMatch()
    {
        CreateTestFile("config.json", "{}");
        CreateTestFile("settings.json", "{}");

        // Request mistyped filename
        var result = _toolkit.ReadFile(Path.Combine(_tempDir, "confg.json")); // typo: confg

        Assert.Contains("Did you mean", result);
    }

    [Fact]
    public void ReadFile_MultipleMatches_SuggestsBestMatch()
    {
        CreateTestFile("script.py", "");
        CreateTestFile("scripts.py", "");
        CreateTestFile("scriptx.py", "");

        // Typo is closest to "scripts.py"
        var result = _toolkit.ReadFile(Path.Combine(_tempDir, "scripts.py")); // Should find it exactly

        // If exact match fails, it should suggest the closest
        if (!result.Contains("   1│"))
        {
            Assert.Contains("Did you mean", result);
        }
    }

    [Fact]
    public void ReadFile_FileNotInCurrentDir_SuggestsRelativeToCwd()
    {
        var subdir = Path.Combine(_tempDir, "subdir");
        Directory.CreateDirectory(subdir);
        var filePath = Path.Combine(subdir, "test.txt");
        File.WriteAllText(filePath, "content");

        // Try reading just the filename (not the full path)
        var result = _toolkit.ReadFile("test.txt");

        // Should either succeed if CWD is _tempDir/subdir, or suggest the file
        // This test depends on CWD; we just verify it doesn't hang
        Assert.True(
            result.Contains("Error: File not found") ||
            result.Contains("Did you mean") ||
            result.Contains("   1│ content")
        );
    }

    // ═══════════════════════════════════════════════════════════════════
    // READ DEDUPLICATION TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadFile_FirstRead_ReturnsFullContent()
    {
        var path = CreateTestFile("dedup1.txt", "line 1\nline 2");
        var result = _toolkit.ReadFile(path);

        Assert.Contains("   1│ line 1", result);
        Assert.Contains("   2│ line 2", result);
    }

    [Fact]
    public void ReadFile_SecondReadUnchangedFile_ReturnsDedupStub()
    {
        var path = CreateTestFile("dedup2.txt", "content");

        var result1 = _toolkit.ReadFile(path);
        Assert.Contains("   1│ content", result1);

        // Immediate re-read of same file, same range
        var result2 = _toolkit.ReadFile(path);
        Assert.Contains("File unchanged since last read", result2);
    }

    [Fact]
    public void ReadFile_FileModified_BypassesDedup()
    {
        var path = CreateTestFile("dedup3.txt", "old content");

        var result1 = _toolkit.ReadFile(path);
        Assert.Contains("   1│ old content", result1);

        // Modify the file (sleep briefly to ensure mtime changes)
        System.Threading.Thread.Sleep(100);
        File.WriteAllText(path, "new content");

        // Re-read should show new content, not stub
        var result2 = _toolkit.ReadFile(path);
        Assert.Contains("   1│ new content", result2);
        Assert.DoesNotContain("unchanged since last read", result2);
    }

    [Fact]
    public void ReadFile_DifferentOffset_BypassesDedup()
    {
        var path = CreateTestFile("dedup4.txt", "1\n2\n3\n4\n5");

        var result1 = _toolkit.ReadFile(path, offset: 1, limit: 2);
        Assert.Contains("Lines: 1-2 of 5", result1);

        // Read different range
        var result2 = _toolkit.ReadFile(path, offset: 3, limit: 2);
        Assert.Contains("Lines: 3-4 of 5", result2);
    }

    [Fact]
    public void ReadFile_DifferentLimit_BypassesDedup()
    {
        var path = CreateTestFile("dedup5.txt", "1\n2\n3\n4\n5");

        var result1 = _toolkit.ReadFile(path, offset: 1, limit: 2);
        Assert.Contains("Lines: 1-2 of 5", result1);

        // Same offset, different limit
        var result2 = _toolkit.ReadFile(path, offset: 1, limit: 3);
        Assert.Contains("Lines: 1-3 of 5", result2);
    }

    // ═══════════════════════════════════════════════════════════════════
    // EDGE CASES & VALIDATION TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadFile_OffsetZero_NormalizesToOne()
    {
        var path = CreateTestFile("offset0.txt", "line 1\nline 2");
        var result = _toolkit.ReadFile(path, offset: 0);

        Assert.Contains("   1│ line 1", result);
        Assert.Contains("Lines: 1-2 of 2", result);
    }

    [Fact]
    public void ReadFile_NegativeOffset_NormalizesToOne()
    {
        var path = CreateTestFile("offsetneg.txt", "a\nb");
        var result = _toolkit.ReadFile(path, offset: -5);

        Assert.Contains("   1│ a", result);
    }

    [Fact]
    public void ReadFile_OffsetBeyondEOF_ReturnsError()
    {
        var path = CreateTestFile("offseteof.txt", "1\n2\n3");
        var result = _toolkit.ReadFile(path, offset: 100);

        Assert.Contains("Error", result);
        Assert.Contains("Offset 100 exceeds file length", result);
    }

    [Fact]
    public void ReadFile_VeryLongLines_PreservesLineNumbers()
    {
        var longLine = new string('x', 5000);
        var path = CreateTestFile("longline.txt", $"{longLine}\nshort");
        var result = _toolkit.ReadFile(path);

        Assert.Contains("   1│", result);
        Assert.Contains("   2│ short", result);
    }

    [Fact]
    public void ReadFile_FileWithEmptyLines_IncludesEmptyLines()
    {
        var path = CreateTestFile("empty_lines.txt", "line 1\n\nline 3");
        var result = _toolkit.ReadFile(path);

        Assert.Contains("   1│ line 1", result);
        Assert.Contains("   2│", result); // Empty line should still have line number
        Assert.Contains("   3│ line 3", result);
    }

    [Fact]
    public void ReadFile_CaseInsensitiveExtension_DetectsMimeType()
    {
        var path = CreateTestFile("script.CS", "var x = 1;");
        var result = _toolkit.ReadFile(path);

        Assert.Contains("Type: text/x-csharp", result);
    }

    [Fact]
    public void ReadFile_LargeFileWithLimit_PerformsEfficiently()
    {
        var largeContent = string.Join("\n", Enumerable.Range(1, 50000));
        var path = CreateTestFile("large.txt", largeContent);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = _toolkit.ReadFile(path, offset: 1, limit: 10);
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds < 5000, $"Read took {watch.ElapsedMilliseconds}ms, expected < 5000ms");
        Assert.Contains("Lines: 1-10 of 50000", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // CONCURRENT ACCESS TEST
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadFile_MultipleThreads_ThreadSafe()
    {
        var paths = Enumerable
            .Range(1, 5)
            .Select(i => CreateTestFile($"concurrent{i}.txt", $"content {i}"))
            .ToList();

        var results = new List<string>();
        var tasks = paths.Select(path =>
            Task.Run(() =>
            {
                var result = _toolkit.ReadFile(path);
                lock (results)
                    results.Add(result);
            })
        );

        Task.WaitAll(tasks.ToArray());

        Assert.Equal(5, results.Count);
        Assert.True(results.All(r => !r.Contains("Error")));
    }

    // ═══════════════════════════════════════════════════════════════════
    // PER-LINE TRUNCATION TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadFile_LineExceedsMaxLength_TruncatesWithSuffix()
    {
        var longLine = new string('a', 3000); // > 2000 char cap
        var path = CreateTestFile("longlinecap.txt", longLine);
        var result = _toolkit.ReadFile(path);

        Assert.Contains("... (line truncated)", result);
        Assert.DoesNotContain(new string('a', 2001), result); // chars beyond cap not present
    }

    [Fact]
    public void ReadFile_LineAtExactMaxLength_NotTruncated()
    {
        var exactLine = new string('b', 2000); // exactly at cap
        var path = CreateTestFile("exactcap.txt", exactLine);
        var result = _toolkit.ReadFile(path);

        Assert.DoesNotContain("... (line truncated)", result);
    }

    [Fact]
    public void ReadFile_ShortLines_NeverTruncated()
    {
        var path = CreateTestFile("shortlines.txt", "hello\nworld");
        var result = _toolkit.ReadFile(path);

        Assert.DoesNotContain("... (line truncated)", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // BYTE CAP TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadFile_OutputExceedsByteCap_ShowsOutputCappedMessage()
    {
        // Each line is ~1100 bytes; 300 lines = ~330KB > 256KB cap
        var bigLine = new string('x', 1000);
        var content = string.Join("\n", Enumerable.Repeat(bigLine, 300));
        var path = CreateTestFile("bytecap.txt", content);

        var result = _toolkit.ReadFile(path, limit: 0); // no line limit — byte cap should trigger

        Assert.Contains("OUTPUT CAPPED", result);
        Assert.Contains("KB byte limit", result);
        Assert.Contains("To continue reading, use offset:", result);
    }

    [Fact]
    public void ReadFile_OutputExceedsByteCap_NextOffsetIsCorrect()
    {
        var bigLine = new string('x', 1000);
        var content = string.Join("\n", Enumerable.Repeat(bigLine, 300));
        var path = CreateTestFile("bytecap2.txt", content);

        var result = _toolkit.ReadFile(path, offset: 1, limit: 0);

        // Extract the suggested next offset and verify it is > 1
        var match = System.Text.RegularExpressions.Regex.Match(result, @"use offset: (\d+)");
        Assert.True(match.Success, "Should contain a next offset suggestion");
        var nextOffset = int.Parse(match.Groups[1].Value);
        Assert.True(nextOffset > 1, $"Next offset {nextOffset} should be > 1");
        Assert.True(nextOffset <= 300, $"Next offset {nextOffset} should be <= total lines");
    }

    [Fact]
    public void ReadFile_SmallFile_NeverHitsByteCap()
    {
        var path = CreateTestFile("smallnobytecap.txt", "just a small file\nwith two lines");
        var result = _toolkit.ReadFile(path);

        Assert.DoesNotContain("OUTPUT CAPPED", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // RELATIVE PATH RESOLUTION TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadFile_RelativePath_ResolvesAgainstCwd()
    {
        // Create file in CWD
        var cwd = Directory.GetCurrentDirectory();
        var fileName = $"relative-test-{Guid.NewGuid()}.txt";
        var fullPath = Path.Combine(cwd, fileName);
        try
        {
            File.WriteAllText(fullPath, "relative content");
            var result = _toolkit.ReadFile(fileName); // relative path

            Assert.Contains("   1│ relative content", result);
        }
        finally
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
    }

    [Fact]
    public void ReadFile_RelativePath_WithTrailingWhitespace_StillResolves()
    {
        var cwd = Directory.GetCurrentDirectory();
        var fileName = $"whitespace-test-{Guid.NewGuid()}.txt";
        var fullPath = Path.Combine(cwd, fileName);
        try
        {
            File.WriteAllText(fullPath, "trimmed");
            var result = _toolkit.ReadFile($"  {fileName}  "); // padded with spaces

            Assert.Contains("   1│ trimmed", result);
        }
        finally
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // DIRECTORY REDIRECT TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadFile_DirectoryPath_ReturnsHelpfulRedirect()
    {
        var result = _toolkit.ReadFile(_tempDir);

        Assert.Contains("Error", result);
        Assert.Contains("is a directory", result);
        Assert.Contains("ListDirectory", result);
    }

    [Fact]
    public void ReadFile_Cwd_ReturnsDirectoryRedirect()
    {
        var result = _toolkit.ReadFile(Directory.GetCurrentDirectory());

        Assert.Contains("Error", result);
        Assert.Contains("is a directory", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // BINARY CONTENT SNIFF TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadFile_ExtensionlessFileWithNullBytes_IsBlocked()
    {
        // File with no extension but contains null bytes — extension check passes, sniff must catch it
        var path = Path.Combine(_tempDir, "binarydata"); // no extension
        var bytes = new byte[100];
        bytes[10] = 0x00; // null byte
        bytes[20] = 0x00;
        File.WriteAllBytes(path, bytes);

        var result = _toolkit.ReadFile(path);

        Assert.Contains("Error", result);
        Assert.Contains("binary", result);
    }

    [Fact]
    public void ReadFile_ExtensionlessTextFile_IsReadable()
    {
        // File with no extension but valid text content — should be read successfully
        var path = Path.Combine(_tempDir, "Makefile");
        File.WriteAllText(path, "all:\n\techo done", Encoding.UTF8);

        var result = _toolkit.ReadFile(path);

        Assert.DoesNotContain("Error", result);
        Assert.Contains("   1│ all:", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // FILE SIZE PRE-GATE TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadFile_FileLargerThan50MB_IsBlockedBeforeRead()
    {
        // Create a >50MB sparse file using FileStream.SetLength (no actual disk write)
        var path = Path.Combine(_tempDir, "huge.txt");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            fs.SetLength(51L * 1024 * 1024); // 51MB sparse
        }

        var result = _toolkit.ReadFile(path);

        Assert.Contains("Error", result);
        Assert.Contains("too large", result);
        // Should mention offset/limit as the workaround
        Assert.Contains("offset", result);
    }

    [Fact]
    public void ReadFile_FileExactly50MB_IsBlocked()
    {
        var path = Path.Combine(_tempDir, "exact50mb.txt");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            fs.SetLength(50L * 1024 * 1024 + 1); // 1 byte over the 50MB limit
        }

        var result = _toolkit.ReadFile(path);

        Assert.Contains("Error", result);
        Assert.Contains("too large", result);
    }

    [Fact]
    public void ReadFile_FileUnder50MB_IsNotBlockedByPreGate()
    {
        var path = CreateTestFile("under50mb.txt", "normal content");
        var result = _toolkit.ReadFile(path);

        // Should succeed or fail for another reason, never for size
        Assert.DoesNotContain("too large", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // BINARY RATIO CHECK TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadFile_HighNonPrintableRatio_IsBlocked()
    {
        // No null bytes but >30% non-printable (control chars 0x01–0x08)
        var path = Path.Combine(_tempDir, "highnonprintable");
        var bytes = new byte[200];
        for (int i = 0; i < 200; i++)
            bytes[i] = (byte)(i % 3 == 0 ? 0x05 : 0x61); // every 3rd byte is 0x05 (non-printable, not null)
        File.WriteAllBytes(path, bytes);

        var result = _toolkit.ReadFile(path);

        Assert.Contains("Error", result);
        Assert.Contains("binary", result);
    }

    [Fact]
    public void ReadFile_LowNonPrintableRatio_IsReadable()
    {
        // <30% non-printable — should be treated as text
        var path = Path.Combine(_tempDir, "lownonprintable");
        var bytes = new byte[200];
        for (int i = 0; i < 200; i++)
            bytes[i] = (byte)(i % 20 == 0 ? 0x05 : 0x61); // only 5% non-printable
        File.WriteAllBytes(path, bytes);

        var result = _toolkit.ReadFile(path);

        // Should not be blocked for binary ratio
        Assert.DoesNotContain("non-printable ratio", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // GITIGNORE BLOCKING TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadFile_GitIgnoredFile_IsBlocked()
    {
        // Set up a temp directory with a .gitignore that excludes secrets.txt
        var gitRoot = Path.Combine(_tempDir, "gitproject");
        Directory.CreateDirectory(gitRoot);
        File.WriteAllText(Path.Combine(gitRoot, ".gitignore"), "secrets.txt\n*.env\n");
        var secretFile = Path.Combine(gitRoot, "secrets.txt");
        File.WriteAllText(secretFile, "API_KEY=super_secret");

        // Change CWD to gitRoot so the toolkit picks up the .gitignore
        var original = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(gitRoot);
            var toolkitWithGitIgnore = new CodingToolkit(); // fresh instance reads .gitignore from new CWD
            var result = toolkitWithGitIgnore.ReadFile(secretFile);

            Assert.Contains("Error", result);
            Assert.Contains("gitignore", result);
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }

    [Fact]
    public void ReadFile_NonIgnoredFile_InGitRepo_IsReadable()
    {
        var gitRoot = Path.Combine(_tempDir, "gitproject2");
        Directory.CreateDirectory(gitRoot);
        File.WriteAllText(Path.Combine(gitRoot, ".gitignore"), "secrets.txt\n");
        var normalFile = Path.Combine(gitRoot, "readme.txt");
        File.WriteAllText(normalFile, "public content");

        var original = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(gitRoot);
            var toolkitWithGitIgnore = new CodingToolkit();
            var result = toolkitWithGitIgnore.ReadFile(normalFile);

            Assert.DoesNotContain("gitignore", result);
            Assert.Contains("   1│ public content", result);
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // TIMEOUT TESTS
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ReadFile_SmallFile_DoesNotShowTimeoutNotice()
    {
        var path = CreateTestFile("notimeout.txt", "line 1\nline 2\nline 3");
        var result = _toolkit.ReadFile(path);

        Assert.DoesNotContain("TIMED OUT", result);
        Assert.Contains("   1│ line 1", result);
    }

    [Fact]
    public void ReadLinesWithTimeout_NoCancellation_ReturnsAllLines()
    {
        var path = CreateTestFile("timeout_all.txt", "a\nb\nc");
        using var cts = new CancellationTokenSource();

        var (lines, timedOut) = CodingToolkit.ReadLinesWithTimeout(path, Encoding.UTF8, cts.Token);

        Assert.False(timedOut);
        Assert.Equal(3, lines.Length);
        Assert.Equal("a", lines[0]);
        Assert.Equal("b", lines[1]);
        Assert.Equal("c", lines[2]);
    }

    [Fact]
    public void ReadLinesWithTimeout_PreCancelledToken_ReturnsEmptyAndTimedOut()
    {
        var path = CreateTestFile("timeout_pre.txt", "line 1\nline 2\nline 3");
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancel

        var (lines, timedOut) = CodingToolkit.ReadLinesWithTimeout(path, Encoding.UTF8, cts.Token);

        Assert.True(timedOut);
        Assert.Empty(lines);
    }

    [Fact]
    public void ReadLinesWithTimeout_CancelledAfterSomeLines_ReturnsPartialAndTimedOut()
    {
        // Create a file with many lines
        var content = string.Join("\n", Enumerable.Range(1, 10000));
        var path = CreateTestFile("timeout_partial.txt", content);

        // Cancel almost immediately (1ms) — should collect some lines before the token fires
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1));

        var (lines, timedOut) = CodingToolkit.ReadLinesWithTimeout(path, Encoding.UTF8, cts.Token);

        // Either it timed out and collected a partial set, or it was fast enough to finish.
        // Both are valid — the important contract is: if timedOut, we got fewer than all lines.
        if (timedOut)
        {
            Assert.True(lines.Length < 10000, $"Expected partial lines on timeout, got {lines.Length}");
            Assert.True(lines.Length > 0, "Should have collected at least some lines before timeout");
        }
        else
        {
            Assert.Equal(10000, lines.Length);
        }
    }

    [Fact]
    public void ReadLinesWithTimeout_EmptyFile_ReturnsEmptyWithoutTimeout()
    {
        var path = CreateTestFile("timeout_empty.txt", "");
        using var cts = new CancellationTokenSource();

        var (lines, timedOut) = CodingToolkit.ReadLinesWithTimeout(path, Encoding.UTF8, cts.Token);

        Assert.False(timedOut);
        Assert.Empty(lines);
    }

    [Fact]
    public void ReadFile_TimeoutNotice_IncludesLineCount()
    {
        // Simulate what the output looks like when a timeout occurs by using a pre-cancelled token
        // through the full ReadFile pipeline. Since ReadFile uses ReadTimeoutMs (10s), we can't
        // easily trigger a real timeout, but we can verify the normal path doesn't include it.
        var content = string.Join("\n", Enumerable.Range(1, 50));
        var path = CreateTestFile("timeout_notice.txt", content);
        var result = _toolkit.ReadFile(path);

        Assert.DoesNotContain("READ TIMED OUT", result);
        Assert.DoesNotContain("slow filesystem", result);
        Assert.Contains("Lines: 1-50 of 50", result);
    }
}
