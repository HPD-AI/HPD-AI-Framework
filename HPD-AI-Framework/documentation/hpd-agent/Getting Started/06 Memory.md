# Memory & Content Store

> Three distinct storage concepts: content store, session store, and agent memory

There are three storage abstractions in the framework. They serve different purposes and are often confused:

| | `IContentStore` | `ISessionStore` | Agent Memory (`/memory` folder) |
|--|--|--|--|
| **What it stores** | Files and documents | Session & branch state (conversation history) | Agent working notes (a folder inside `IContentStore`) |
| **Who uses it** | Agent tools (`content_read`, `content_write`, etc.) | The framework internals | The agent, via content tools |
| **Scope** | Agent-scoped or session-scoped | Session/branch | Agent-scoped |
| **You configure it** | `UseDefaultContentStore()` or `WithContentStore()` | `WithSessionStore()` | Automatically available when content store is set up |

---

## IContentStore — Files and Documents

`IContentStore` is the primary storage interface. It gives the agent filesystem-like access to persistent content: knowledge bases, uploaded files, working notes, and generated artifacts. The agent interacts with it through built-in tools (`content_read`, `content_write`, etc.).

### Quick Start

```csharp
var agent = await new AgentBuilder()
    .WithProvider("openai", "gpt-4o")
    .UseDefaultContentStore()  // In-memory store + default folders
    .BuildAsync();
```

For persistent storage, pass a `LocalFileContentStore`:

```csharp
var agent = await new AgentBuilder()
    .WithProvider("openai", "gpt-4o")
    .UseDefaultContentStore(new LocalFileContentStore("./data/content"))
    .BuildAsync();
```

### Default Folders

`UseDefaultContentStore()` creates these folders automatically:

| Folder | Scope | Permissions | Purpose |
|--------|-------|-------------|---------|
| `/knowledge` | Agent | Read | Static knowledge base (docs, guides) |
| `/memory` | Agent | Read/Write | Agent working notes |
| `/skills` | Agent | Read | Skill instruction documents |
| `/uploads` | Session | Read | User-uploaded files (added when session is active) |
| `/artifacts` | Session | Read/Write | Agent-generated outputs (added when session is active) |

**Agent-scoped** folders persist across sessions for the same agent. **Session-scoped** folders are specific to a single conversation.

### Agent Tools

When a content store is configured, the agent automatically gets these tools:

| Tool | Description |
|------|-------------|
| `content_list(path?)` | List files in a folder |
| `content_read(path, offset?, limit?)` | Read file contents |
| `content_write(path, content)` | Write or update a file |
| `content_glob(pattern, path?)` | Find files by name pattern |
| `content_delete(path)` | Delete a file |
| `content_tree(path?, depth?)` | Show folder hierarchy |
| `content_stat(path)` | Show file metadata |

Example agent usage:
```
Agent calls: content_list("/knowledge")
Agent calls: content_read("/knowledge/api-guide.md")
Agent calls: content_write("/memory/user-notes.md", "User prefers concise answers.")
```

### Loading Content at Startup

Use extension methods to populate the store before the agent runs:

```csharp
var store = new LocalFileContentStore("./data/content");

// Upload a knowledge document (upsert — safe to call on every startup)
await store.UploadKnowledgeDocumentAsync(
    agentName: "my-agent",
    documentName: "api-guide",      // Stable name — same name = overwrite
    data: File.ReadAllBytes("api-guide.md"),
    contentType: "text/markdown",
    description: "REST API reference guide");

// Upload a skill instruction document
await store.UploadSkillDocumentAsync(
    documentId: "code-review",
    content: File.ReadAllText("skills/code-review.md"),
    description: "Instructions for reviewing code",
    scope: null);  // null = visible to all agents

var agent = await new AgentBuilder()
    .WithProvider("openai", "gpt-4o")
    .UseDefaultContentStore(store)
    .BuildAsync();
```

Named upsert semantics mean these calls are **startup-safe**: calling them on every app start only overwrites when the content actually changes.

### IContentStore Interface

The extension methods above are the recommended way to interact with content storage, but the raw interface is available when you need full control:

```csharp
public interface IContentStore
{
    Task<string> PutAsync(
        string? scope,
        byte[] data,
        string contentType,
        ContentMetadata? metadata = null,
        CancellationToken cancellationToken = default);

    Task<ContentData?> GetAsync(
        string? scope,
        string contentId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string? scope,
        string contentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContentInfo>> QueryAsync(
        string? scope = null,
        ContentQuery? query = null,
        CancellationToken cancellationToken = default);
}
```

The `scope` parameter isolates content: pass `agentName` for agent-scoped content, `sessionId` for session-scoped content, or `null` to operate globally.

### Built-in Implementations

| Type | Use Case |
|------|----------|
| `InMemoryContentStore` | Testing, prototyping, ephemeral sessions |
| `LocalFileContentStore(path)` | Production local storage |

Implement `IContentStore` for custom backends (S3, Azure Blob, SQL, etc.).

### Custom Folders

Register custom folders beyond the defaults:

```csharp
var store = new LocalFileContentStore("./data");

store.CreateFolder("reports", new FolderOptions
{
    Description = "Generated reports",
    Permissions = ContentPermissions.ReadWrite
});

var agent = await new AgentBuilder()
    .WithContentStore(store)  // Manual setup instead of UseDefaultContentStore
    .BuildAsync();
```

### Permissions

```csharp
[Flags]
public enum ContentPermissions
{
    Read      = 1,   // content_read, content_list, content_glob, content_stat, content_tree
    Write     = 2,   // content_write
    Delete    = 4,   // content_delete
    ReadWrite = Read | Write,
    Full      = Read | Write | Delete
}
```

### Querying Content

```csharp
// List all knowledge documents for an agent
var docs = await store.QueryAsync(
    scope: "my-agent",
    query: new ContentQuery
    {
        Tags = new Dictionary<string, string> { ["folder"] = "/knowledge" },
        Limit = 50
    });

foreach (var doc in docs)
    Console.WriteLine($"{doc.Name} ({doc.SizeBytes} bytes, {doc.ContentType})");
```

### AgentBuilder Reference

| Method | Description |
|--------|-------------|
| `UseDefaultContentStore(store?)` | Register store + create default folders + register content tools |
| `WithContentStore(store)` | Register store only (manage folders manually) |

---

## ISessionStore — Session and Branch State

`ISessionStore` persists **conversation history**: sessions and their branches. It is not for files or agent-generated content — that's `IContentStore`'s job.

The framework uses `ISessionStore` internally to save and load conversation turns. You configure it once and the agent handles everything else.

```csharp
var agent = await new AgentBuilder()
    .WithProvider("openai", "gpt-4o")
    .WithSessionStore(new LocalFileSessionStore("./sessions"), persistAfterTurn: true)
    .BuildAsync();
```

`persistAfterTurn: true` saves conversation history automatically after each completed turn.

### What ISessionStore persists

- **Sessions** — metadata (ID, name, creation time)
- **Branches** — the actual message history for each conversation branch
- **Uncommitted turns** — crash recovery: the in-progress turn that wasn't saved yet

`ISessionStore` does **not** store files, knowledge, or agent-generated content. Use `IContentStore` for those.

### Built-in Implementations

| Type | Use Case |
|------|----------|
| `InMemorySessionStore` | Default; no persistence across restarts |
| `LocalFileSessionStore(path)` | JSON files on disk |
| `JsonSessionStore(path)` | Alias for `LocalFileSessionStore` |

### Web apps

In web applications, the session store is configured through `HPDAgentConfig` rather than directly on `AgentBuilder`. The hosting layer owns the store so session endpoints work independently of the agent lifecycle.

```csharp
builder.Services.AddHPDAgent(options =>
{
    options.SessionStore = new JsonSessionStore("./sessions");
    options.PersistAfterTurn = true;
});
```

→ See [08 Building Web Apps.md](08%20Building%20Web%20Apps.md) for full hosting configuration.

---

## Agent Memory — The `/memory` Folder

"Agent memory" is not a separate system — it's simply the `/memory` folder inside `IContentStore`. It's a read/write folder (agent-scoped) where the agent can store working notes between conversations.

The agent writes to it using `content_write`, reads from it using `content_read`, and you can pre-populate it or read it programmatically:

```csharp
// Write a memory entry programmatically (upsert by title)
await store.WriteMemoryAsync(
    agentName: "my-agent",
    title: "user-preferences",
    content: "User prefers Python examples. Timezone: UTC+1.");
```

The agent can also write its own memory during a conversation:
```
Agent calls: content_write("/memory/user-preferences.md", "User prefers Python examples.")
```

Memory persists across sessions when using a durable `IContentStore` (e.g., `LocalFileContentStore`). With the default `InMemoryContentStore`, it is lost when the process restarts.

---

## Image and Document Content

For sending files to the agent as part of a message (not stored in the content store), use the typed content helpers:

```csharp
// Image from file
var image = await ImageContent.FromFileAsync("photo.jpg");

// Document from file
var pdf = await DocumentContent.FromFileAsync("report.pdf");
// Or use factory methods:
var doc = DocumentContent.Pdf(File.ReadAllBytes("report.pdf"));

// Send with a message
await foreach (var evt in agent.RunAsync(
    [new ChatMessage(ChatRole.User, ["What's in this image?", image])],
    branch))
{ }
```

`ImageContent` supports: PNG, JPEG, GIF, WebP, BMP, HEIC, AVIF, SVG.
`DocumentContent` supports: PDF, Word, Excel, PowerPoint, HTML, Markdown, plain text.

For remote files:

```csharp
// Passthrough — URL sent directly to provider (no download)
var content = await ImageContent.FromUriAsync(new Uri("https://example.com/image.png"));

// Download — fetch and convert to base64 (for providers that require it)
var content = await ImageContent.FromUriAsync(new Uri("https://example.com/image.png"), download: true);
```
