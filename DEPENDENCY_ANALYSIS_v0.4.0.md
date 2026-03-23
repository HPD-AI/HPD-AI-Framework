# HPD-Agent-Framework Package Dependency Analysis
**Report Generated:** March 22, 2026
**Target Release:** v0.4.0

---

## Executive Summary

Analyzed **113 unique NuGet packages** across **187 .csproj files** in the HPD-Agent-Framework codebase. Found several outdated packages requiring updates for v0.4.0 release, particularly in:
- Microsoft.NET.Test.Sdk (Testing infrastructure)
- Some AI/ML provider packages
- CodeAnalysis packages

---

## Critical Updates Required

### Microsoft.NET.Test.Sdk (Testing)
**Priority: HIGH** - Used across all test projects
- **Current Versions:** 17.6.0, 17.8.0, 17.11.1, 17.12.*, 17.14.1, 18.3.0
- **Latest Available:** 18.4.0+ (as of Mar 2026)
- **Update Type:** MINOR/PATCH
- **Impact:** Inconsistent versions across test projects. Standardize to latest stable.
- **Affected Files:** 20+ test .csproj files

**RECOMMENDATION:** Update to 18.4.0 for consistency and latest test infrastructure improvements.

---

### Microsoft.CodeAnalysis packages
**Priority: HIGH** - Code quality and analysis
- **Microsoft.CodeAnalysis.CSharp:**
  - Current: 4.12.0, 5.0.0
  - Latest: 5.0.0+
  - **Status:** 5.0.0 is current for .NET 9/10 targets
  - **Update Type:** Already on latest (5.0.0)

- **Microsoft.CodeAnalysis.Analyzers:**
  - Current: 3.3.4, 3.11.0
  - Latest: 3.11.0+
  - **Status:** Mixed versions, standardize to 3.11.0
  - **Update Type:** MINOR

---

## Medium Priority Updates

### Microsoft.Extensions.* Packages
**Priority: MEDIUM** - Widely used infrastructure

| Package | Current | Latest | Status | Notes |
|---------|---------|--------|--------|-------|
| Microsoft.Extensions.DependencyInjection | 10.0.3 | 10.0.3 | ✓ Current | - |
| Microsoft.Extensions.Logging | 10.0.3, 9.0.5 | 10.0.3 | ⚠ Mixed | Standardize to 10.0.3 |
| Microsoft.Extensions.Configuration | 10.0.3 | 10.0.3 | ✓ Current | - |
| Microsoft.Extensions.Caching.Memory | 10.0.3 | 10.0.3 | ✓ Current | - |
| Microsoft.Extensions.Options | 10.0.3 | 10.0.3 | ✓ Current | - |
| Microsoft.Extensions.FileSystemGlobbing | 10.0.3 | 10.0.3 | ✓ Current | - |

**RECOMMENDATION:** Standardize all 10.x versions to 10.0.3 across the codebase.

---

### Microsoft.AspNetCore Packages
**Priority: MEDIUM** - ASP.NET Core support

| Package | Current | Latest | Status | Notes |
|---------|---------|--------|--------|-------|
| Microsoft.AspNetCore.Mvc.Testing | 8.0.15, 9.0.5, 10.0.3 | 10.0.3 | ⚠ Mixed TFM | Correct per TFM |
| Microsoft.AspNetCore.Authentication.* | 10.0.* | 10.0.3 | ⚠ Partial | Use exact 10.0.3 |
| Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | 10.0.* | 10.0.3 | ⚠ Partial | Use exact 10.0.3 |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.* | 10.0.3 | ⚠ Partial | Use exact 10.0.3 |

**RECOMMENDATION:** Replace all `10.0.*` wildcards with exact `10.0.3` versions.

---

## AI/ML Provider Packages
**Priority: MEDIUM-HIGH** - Core functionality

### Anthropic SDK
- **Current:** 12.* (wildcard)
- **Latest:** 12.0.0+ (check Anthropic release notes)
- **Status:** ⚠ Wildcard constraint - should pin exact version
- **Update Type:** Standardize to specific 12.x version
- **Recommendation:** Pin to specific 12.x.x release after v0.4.0 requirements confirmed

### OpenAI SDK
- **Current:** 2.9.1
- **Latest:** 2.9.1+ (verify with OpenAI releases)
- **Status:** ✓ Recent version
- **Update Type:** Minor version consideration

### Azure.AI.OpenAI
- **Current:** 2.8.0-beta.1, 2.* (mixed)
- **Latest:** Check Azure SDK releases (typically 2.0.0+)
- **Status:** ⚠ Beta version in use, mixed with wildcard
- **Recommendation:** Evaluate moving from beta to stable release

### Google Generative AI
- **Current:** 3.6.3
- **Latest:** Check Google SDK releases
- **Status:** ✓ Stable version

---

## Testing Framework Packages

### xunit
- **Current:** 2.4.2, 2.6.2, 2.9.0, 2.9.3 (Multiple versions)
- **Latest:** 2.9.3+
- **Status:** ⚠ Inconsistent - standardize to 2.9.3
- **Update Type:** PATCH
- **Affected:** Multiple test projects

**RECOMMENDATION:** Standardize all test projects to xunit 2.9.3 and xunit.runner.visualstudio 3.1.5

### FluentAssertions
- **Current:** 6.12.*, 8.8.0
- **Latest:** 8.8.0+
- **Status:** ⚠ Major version split (6.x vs 8.x)
- **Update Type:** MAJOR or MINOR
- **Recommendation:** Standardize to 8.8.0 (latest)

### Moq
- **Current:** 4.20.*, 4.20.72
- **Latest:** 4.20.72+
- **Status:** ✓ Current
- **Update Type:** MINOR available

---

## Low Priority - Currently Adequate

### Documentation/Parsing
- DiffPlex: 1.7.*, 1.8.0, 1.9.0 → Current: 1.9.0
- Markdig: 0.38.* → Stable, used for markdown processing
- HtmlAgilityPack: 1.12.4 → Current stable
- DocumentFormat.OpenXml: 3.4.1 → Current

### Database/Storage
- Microsoft.EntityFrameworkCore.Sqlite: 10.0.* → Correct for .NET 10
- Neo4j.Driver: 6.0.0 → Current stable
- Microsoft.Data.Sqlite: 10.0.3 → Current

### Utilities
- CliWrap: 3.10.0 → Current
- Cronos: 0.11.1 → Current
- Spectre.Console: 0.49.1 → Current

---

## Version Inconsistencies Found

### Critical Inconsistencies

1. **Microsoft.NET.Test.Sdk** - 6 different versions across projects
   - 17.6.0, 17.8.0, 17.11.1, 17.12.*, 17.14.1, 18.3.0
   - **Action:** Standardize to 18.3.0 (latest in use)

2. **xunit** - 5 different versions
   - 2.*, 2.4.2, 2.6.2, 2.9.*, 2.9.0, 2.9.3
   - **Action:** Standardize to 2.9.3

3. **FluentAssertions** - 2 major versions
   - 6.12.*, 8.8.0
   - **Action:** Migrate all projects to 8.8.0

4. **Microsoft.AspNetCore.Mvc.Testing** - 4 versions across TFMs
   - 8.0.15, 9.0.5, 10.0.*, 10.0.3
   - **Action:** Keep per TFM but standardize wildcards

### Minor Inconsistencies

5. **Microsoft.Extensions.DependencyInjection** - 2 versions
   - 10.*, 10.0.3
   - **Action:** Use exact 10.0.3

6. **Microsoft.Extensions.Logging** - 3 versions
   - 10.*, 10.0.3, 9.0.5
   - **Action:** Standardize to 10.0.3 for .NET 10 projects

7. **Microsoft.Maui.Controls** - 2 versions
   - 9.0.120, 10.0.1
   - **Action:** Clarify TFM requirements, update older projects

---

## Wildcard Version Patterns to Address

Several packages use wildcard constraints that should be pinned:

| Package | Current Pattern | Recommended |
|---------|-----------------|-------------|
| Anthropic | 12.* | Pin to 12.x.x (exact version) |
| Azure.Identity | 1.* | Pin to 1.x.x |
| Azure.AI.OpenAI | 2.* | Evaluate beta→stable |
| AWSSDK.* | 4.* | Pin to 4.0.x.x |
| Markdig | 0.38.* | Pin to 0.38.x |
| Microsoft.AspNetCore.* | 10.0.* | Pin to 10.0.3 |
| Microsoft.Extensions.* | 10.* | Pin to 10.0.3 |

**RECOMMENDATION:** Pin all production dependencies to exact versions for reproducible builds.

---

## HPD-Agent.* Package Versions

All internal HPD-Agent packages are at version 0.2.0:
- HPD-Agent.Events: 0.2.0
- HPD-Agent.FFI: 0.2.0
- HPD-Agent.Framework: 0.2.0
- HPD-Agent.MCP: 0.2.0
- HPD-Agent.Memory: 0.2.0
- HPD-Agent.Providers.Anthropic: 0.2.0
- HPD-Agent.Providers.OpenAI: 0.2.0
- HPD-Agent.TextExtraction: 0.2.0
- HPD-Agent.Toolkit.FileSystem: 0.2.0
- HPD-Agent.Toolkit.WebSearch: 0.2.0

**Action for v0.4.0:** Update to 0.4.0 version

---

## Summary Table: Required Updates

### MUST UPDATE (Blocking for v0.4.0)

| Package | Current | Target | Type | Impact |
|---------|---------|--------|------|--------|
| Microsoft.NET.Test.Sdk | Mixed | 18.3.0+ | Standardize | HIGH |
| xunit | Mixed | 2.9.3 | Standardize | HIGH |
| FluentAssertions | 6.12.*/8.8.0 | 8.8.0 | MAJOR | MEDIUM |
| Microsoft.CodeAnalysis.Analyzers | 3.3.4/3.11.0 | 3.11.0 | Standardize | MEDIUM |
| HPD-Agent.* | 0.2.0 | 0.4.0 | VERSION | HIGH |

### SHOULD UPDATE (v0.4.0 preparation)

| Package | Current | Target | Type | Impact |
|---------|---------|--------|------|--------|
| Microsoft.Extensions.* | 10.0.*, 9.0.5 | 10.0.3 | Standardize | MEDIUM |
| Microsoft.AspNetCore.* | 10.0.* | 10.0.3 | Pin exact | MEDIUM |
| Anthropic | 12.* | 12.x.x | Pin exact | MEDIUM |
| Azure.AI.OpenAI | 2.8.0-beta | 2.x.x | Evaluate beta | MEDIUM |

---

## Recommended Actions for v0.4.0

### Phase 1: Immediate (Critical Fixes)
1. Update all HPD-Agent.* packages from 0.2.0 to 0.4.0
2. Standardize Microsoft.NET.Test.Sdk to 18.3.0 across all test projects
3. Update xunit projects to use 2.9.3 consistently
4. Update FluentAssertions from 6.12.* to 8.8.0

### Phase 2: Short-term (Standardization)
5. Pin all wildcard versions to exact versions
6. Standardize Microsoft.Extensions.* to 10.0.3
7. Standardize Microsoft.AspNetCore.* to 10.0.3
8. Standardize Microsoft.CodeAnalysis.Analyzers to 3.11.0

### Phase 3: Medium-term (Provider Updates)
9. Review and pin Anthropic SDK to specific 12.x.x version
10. Evaluate moving Azure.AI.OpenAI from beta to stable
11. Verify all AI/ML provider SDKs are compatible with framework changes

### Phase 4: Testing & Release
12. Run full test suite after updates
13. Verify AOT compilation compatibility where applicable
14. Document breaking changes in release notes

---

## Files Requiring Updates

**Total .csproj files:** 187

**Most frequently updated files (by package count):**
- Test projects: 50+ files (Microsoft.NET.Test.Sdk, xunit, FluentAssertions)
- Source projects: 100+ files (Microsoft.Extensions.*, Framework packages)
- Special projects: 37 NuGetTest files (local testing frameworks)

---

## .NET Target Framework Status

HPD-Agent-Framework supports multiple target frameworks:
- .NET 6 (EOL support, may be deprecated)
- .NET 8 (LTS, actively supported)
- .NET 9 (Current)
- .NET 10 (Latest, recommended)

**Recommendation:** Review .NET 6 support for v0.4.0 - consider dropping if not required by customers.

---

## Notes

- Analysis performed on March 22, 2026
- Package versions reflect current codebase state
- Latest versions based on NuGet registry knowledge
- Some beta versions (Azure.AI.Projects, Google AI) may be intentional for experimental features
- Performance impact from updates expected to be minimal
- No breaking changes anticipated from patch/minor updates
- Major version updates (e.g., FluentAssertions 6→8) should be tested thoroughly
