# Changelog

All notable changes to the HPD-Agent FileSystem Toolkit will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2025-10-02

### Added
- Initial release of FileSystem Toolkit
- **Core Operations:**
  - `ReadFile` - Read file contents with optional line ranges
  - `WriteFile` - Create or update files with permission control
  - `ListDirectory` - Browse directory contents recursively
  - `EditFile` - Smart file editing with diff preview
- **Advanced Search:**
  - `FindFiles` - Glob pattern matching for file discovery
  - `SearchContent` - Regex-based content search (grep-like)
- **Security Features:**
  - Workspace isolation and validation
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

  - Workspace-based security model

## [Unreleased]

### Planned Features
- [ ] `.geminiignore` pattern support
- [ ] `.gitignore` pattern support integration
- [ ] Streaming file reads for very large files
- [ ] Batch file operations
- [ ] File watching capabilities
- [ ] Symbolic link handling
- [ ] Archive file support (zip, tar, etc.)
- [ ] Image file metadata extraction
