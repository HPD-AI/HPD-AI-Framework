# HPD-Agent FileSystem Toolkit


## 🚀 Features

### Core Operations
-  **ReadFile** - Read file contents with optional line ranges
-  **WriteFile** - Create or update files with permission control
-  **ListDirectory** - Browse directory contents recursively
-  **EditFile** - Smart file editing with diff preview (inspired by Gemini CLI)

### Advanced Search
-  **FindFiles** - Glob pattern matching (`**/*.cs`, `src/**/*.json`)
-  **SearchContent** - Regex search across files (grep-like)

### Safety Features
- 🔒 **Workspace isolation** - All operations restricted to workspace root
- 🛡️ **Permission system** - Write operations require explicit approval
- 📏 **Size limits** - Configurable maximum file size
- 🔍 **Encoding detection** - Automatic charset detection using UDE
- 🎯 **Context-aware** - Conditional functions based on configuration

## 📦 Installation

### Via NuGet (when published)
```bash
dotnet add package HPD.Agent.Toolkits.FileSystem
```

### Via Project Reference
```xml
<ItemGroup>
  <ProjectReference Include="path/to/HPD-Agent.Toolkits/HPD-Agent.Toolkits.FileSystem/HPD-Agent.Toolkits.FileSystem.csproj" />
</ItemGroup>
```

## 🎯 Quick Start

### Option 1: Simple Usage (Default Settings)

```csharp
using HPD.Agent.Toolkits.FileSystem;

// Just add the Toolkit - uses current directory with safe defaults
var agent = new AgentBuilder()
     .WithToolkit<FileSystemToolkit>()  // ← That's it!
    .Build();

// Default context:
// - workspaceRoot: Current directory
// - enableSearch: true
// - allowOutsideWorkspace: false (secure!)
// - maxFileSize: 10 MB
```

### Option 2: Custom Context (Full Control)

```csharp
using HPD.Agent.Toolkits.FileSystem;

// 1. Create a custom context
var context = new FileSystemContext(
    workspaceRoot: "/path/to/your/project",
    allowOutsideWorkspace: false,      // Security: keep operations in workspace
    respectGitIgnore: true,             // Honor .gitignore patterns
    respectGeminiIgnore: true,          // Honor .geminiignore patterns
    maxFileSize: 10_000_000,            // 10 MB max per file
    enableSearch: true                  // Enable glob/grep operations
);

// 2. Create Toolkit instance with context
var fileSystemToolkit = new FileSystemToolkit(context);

// 3. Register with AgentBuilder
var agent = new AgentBuilder()
     .WithTool(fileSystemToolkit, context)  // ← Pass instance + context
    .Build();
```

### Option 3: Using ToolkitManager Directly

```csharp
var context = new FileSystemContext("/workspace");
var ToolkitManager = new ToolkitManager();

// Register and create functions
ToolkitManager.RegisterToolkit<FileSystemToolkit>();
var functions = ToolkitManager.CreateAllFunctions(context);

// Add to your AI client
chatClient.Tools = functions;
```

### 3. Use with Your Agent

```csharp
// The AI can now call these functions automatically:
var response = await chatClient.CompleteAsync("Read the contents of README.md");
// → AI calls ReadFile("/path/to/your/project/README.md")

var response = await chatClient.CompleteAsync("Find all TypeScript files in src/");
// → AI calls FindFiles("src/**/*.ts")

var response = await chatClient.CompleteAsync("Search for 'TODO' comments in the codebase");
// → AI calls SearchContent("TODO", filePattern: "*.cs")
```

## 📖 Available Functions

### ReadFile
Read file contents with optional pagination.

```csharp
// Read entire file
await ReadFile("/absolute/path/to/file.txt");

// Read specific line range (for large files)
await ReadFile("/absolute/path/to/large.log", offset: 100, limit: 50);
```

**Parameters:**
- `absolutePath` (required) - Absolute path to the file
- `offset` (optional) - Starting line number (0-based)
- `limit` (optional) - Maximum number of lines to read

**Features:**
- Automatic encoding detection
- File size validation
- Binary file detection
- Line range support for large files

---

### WriteFile
Create or overwrite files. **Requires permission approval.**

```csharp
await WriteFile("/absolute/path/to/file.txt", "Hello, World!");
```

**Parameters:**
- `filePath` (required) - Absolute path to the file
- `content` (required) - Content to write

**Features:**
- Automatic directory creation
- UTF-8 encoding by default
- Workspace validation
- Permission system integration

---

### ListDirectory
Browse directory contents.

```csharp
// List current directory
await ListDirectory("/path/to/directory");

// List recursively
await ListDirectory("/path/to/directory", recursive: true);
```

**Parameters:**
- `directoryPath` (required) - Absolute path to directory
- `recursive` (optional) - Include subdirectories

**Output:**
```
--- Directory: /path/to/directory ---
[DIR]  subfolder/
[FILE] README.md (1.2 KB)
[FILE] package.json (456 B)

Total: 1 directories, 2 files
```

---

### EditFile
Smart file editing with diff preview. **Requires permission approval.**

```csharp
await EditFile(
    filePath: "/path/to/file.cs",
    oldString: "public class OldName",
    newString: "public class NewName"
);
```

**Parameters:**
- `filePath` (required) - Absolute path to the file
- `oldString` (required) - Exact text to find (must be unique)
- `newString` (required) - Replacement text

**Features:**
- Uniqueness validation (prevents ambiguous edits)
- Diff preview using DiffPlex
- Workspace validation
- Permission system integration

**Output:**
```
✓ File edited successfully: /path/to/file.cs
Changed 1 lines

--- Diff ---
  namespace MyApp
  {
-     public class OldName
+     public class NewName
      {
          // ...
```

---

### FindFiles
Find files using glob patterns. **Only available when `EnableSearch = true`.**

```csharp
// Find all C# files
await FindFiles("**/*.cs");

// Find in specific directory
await FindFiles("*.json", searchPath: "config/");

// Complex patterns
await FindFiles("src/**/*.{ts,tsx}");
```

**Parameters:**
- `pattern` (required) - Glob pattern (`**/*.ext`, `dir/**/*`)
- `searchPath` (optional) - Directory to search in

**Supported Patterns:**
- `*.cs` - All .cs files in current directory
- `**/*.cs` - All .cs files recursively
- `src/**/*.json` - All .json files under src/
- `*.{ts,tsx}` - All .ts and .tsx files

**Output:**
```
--- Found 15 files matching '**/*.cs' ---
1. src/Program.cs (2.3 KB, modified 1h ago)
2. src/Models/User.cs (1.8 KB, modified 2d ago)
3. tests/UnitTests.cs (4.1 KB, modified 1w ago)
...
```

---

### SearchContent
Search for regex patterns in files (grep). **Only available when `EnableSearch = true`.**

```csharp
// Simple text search
await SearchContent("TODO");

// Regex search
await SearchContent(@"class\s+\w+Controller");

// With file filter
await SearchContent("import", filePattern: "*.ts");

// Case-sensitive
await SearchContent("ERROR", caseSensitive: true);
```

**Parameters:**
- `pattern` (required) - Regular expression pattern
- `searchPath` (optional) - Directory to search
- `filePattern` (optional) - Glob pattern to filter files
- `caseSensitive` (optional) - Case-sensitive matching

**Output:**
```
--- Found 12 matches for 'TODO' ---
File filter: *.cs

File: src/Program.cs
  Line 45: // TODO: Implement authentication
  Line 102: // TODO: Add error handling

File: src/Services/EmailService.cs
  Line 23: // TODO: Use async/await
...
```

---

##  Configuration

### FileSystemContext Options

```csharp
public class FileSystemContext
{
    public string WorkspaceRoot { get; }           // Required: Base directory
    public bool AllowOutsideWorkspace { get; }     // Default: false (recommended)
    public bool RespectGitIgnore { get; }          // Default: true
    public bool RespectGeminiIgnore { get; }       // Default: true
    public long MaxFileSize { get; }               // Default: 10 MB
    public bool EnableSearch { get; }              // Default: true
}
```

### Conditional Functions

Some functions are **conditionally available** based on context:

- `FindFiles` and `SearchContent` - Only available when `EnableSearch = true`

This is achieved through the `[ConditionalFunction]` attribute:

```csharp
[AIFunction<FileSystemContext>]
[ConditionalFunction("EnableSearch")]
public Task<string> FindFiles(...) { }
```

## 🛡️ Security & Permissions

### Workspace Isolation
All operations are validated against the workspace root:

```csharp
//  Allowed: /workspace/src/file.txt
//    Blocked: /etc/passwd
//    Blocked: ../../../etc/passwd
```

### Permission System
Write operations require explicit user approval via `[RequiresPermission]`:

```csharp
[AIFunction]
[RequiresPermission]  // User must approve before execution
public async Task<string> WriteFile(...) { }
```

### File Size Limits
Prevents reading massive files that could crash the agent:

```csharp
var context = new FileSystemContext(
    workspaceRoot: "/workspace",
    maxFileSize: 5_000_000  // 5 MB max
);
```

## 🔍 Under the Hood

### Libraries Used

| Feature | Library | Purpose |
|---------|---------|---------|
| Glob matching | `DotNet.Glob` | File pattern matching (`**/*.cs`) |
| Diff generation | `DiffPlex` | Show changes in EditFile |
| Encoding detection | `Ude.NetStandard` | Auto-detect file charset |
| .gitignore parsing | `MAB.DotIgnore` | Respect ignore patterns |
| Source generation | HPD-Agent.SourceGenerator | Compile-time code generation |

### AOT Compatibility
This Toolkit is fully AOT-compatible:
- No reflection at runtime
- All metadata generated at compile-time
- Manual JSON parsing for parameters

## 📚 Examples

### Example 1: Code Search & Replace

```csharp
// 1. Find all files with old API usage
var files = await FindFiles("**/*.cs");

// 2. Search for old pattern
var matches = await SearchContent("OldApiClient", filePattern: "*.cs");

// 3. Edit each file
await EditFile(
    "/workspace/src/Service.cs",
    oldString: "var client = new OldApiClient();",
    newString: "var client = new NewApiClient();"
);
```

### Example 2: Documentation Generator

```csharp
// 1. Find all source files
var sourceFiles = await FindFiles("src/**/*.cs");

// 2. Read each file
var content = await ReadFile("/workspace/src/Program.cs");

// 3. Generate docs and save
await WriteFile(
    "/workspace/docs/API.md",
    generatedDocumentation
);
```

### Example 3: Log Analysis

```csharp
// 1. Find log files
var logs = await FindFiles("logs/**/*.log");

// 2. Search for errors
var errors = await SearchContent(
    pattern: "ERROR|FATAL",
    searchPath: "logs/",
    filePattern: "*.log"
);

// 3. Read specific time range from large log
var recentLogs = await ReadFile(
    "/workspace/logs/app.log",
    offset: 10000,  // Skip first 10k lines
    limit: 1000     // Read next 1k lines
);
```

## 🤝 Contributing

This Toolkit is part of the HPD-Agent Toolkit ecosystem. Contributions are welcome!

## 📄 License

MIT License - See LICENSE file for details

## 🔗 Related

- [HPD-Agent Core](../../HPD-Agent/)
- [HPD-Agent Source Generator](../../HPD-Agent.SourceGenerator/)
- [Gemini CLI](https://github.com/google-gemini/gemini-cli) - Inspiration for this Toolkit

---

**Built with ❤️ for the HPD-Agent community**
