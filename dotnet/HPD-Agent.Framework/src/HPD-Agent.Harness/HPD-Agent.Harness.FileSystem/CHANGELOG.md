# Changelog

All notable changes to the HPD-Agent FileSystem ToolHarness will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-11-08

### Deprecated
- Deprecated the FileSystem ToolHarness. It remains packable for migration but
  is not a supported workspace-isolation or command-execution security
  boundary.
- Added `CS0618` compiler warnings to `FileSystemTools` and
  `FileSystemContext`.
- Added migration guidance for `HPD-Agent.ToolHarness.Coding` and the
  HPD sandbox runtimes.
- Removed stale examples that referenced retired APIs and types.

### Added
- Initial release of FileSystem ToolHarness
- **Core Operations:**
  - `ReadFile` - Read file contents with optional line ranges
  - `WriteFile` - Create or update files with permission control
  - `ListDirectory` - Browse directory contents recursively
  - `EditFile` - Smart file editing with diff preview
- **Advanced Search:**
  - `FindFiles` - Glob pattern matching for file discovery
  - `SearchContent` - Regex-based content search (grep-like)
- **Legacy constraints (not a security boundary):**
  - Basic workspace path checks
  - Permission system integration via `[RequiresPermission]`
  - Configurable file size limits
  - Binary file detection
- **Smart Features:**
  - Automatic encoding detection using UDE
  - Diff generation using DiffPlex
  - Context-aware conditional functions
  - AOT-compatible source generation
- **Libraries:**
  - `DotNet.Glob` for glob pattern matching
  - `DiffPlex` for diff generation
  - `Ude.NetStandard` for encoding detection
  - `MAB.DotIgnore` for .gitignore support
