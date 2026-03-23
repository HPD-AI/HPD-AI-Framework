# HPD-Agent-Framework Dependency Analysis Report
**Analysis Date:** March 22, 2026
**Target Release:** v0.4.0
**Analysis Scope:** 187 .csproj files, 113 unique packages

---

## Quick Start

This analysis provides a comprehensive review of all NuGet package dependencies in the HPD-Agent-Framework codebase and identifies which packages need updating for the v0.4.0 release.

### Files in This Analysis

1. **DEPENDENCY_ANALYSIS_v0.4.0.md** - Full detailed analysis report
   - Executive summary
   - Critical and medium priority updates
   - Version inconsistencies
   - Wildcard patterns to address
   - Recommended update strategy in 4 phases

2. **HPD-Agent-Framework-Dependencies.csv** - Machine-readable package inventory
   - All 113 packages found in codebase
   - Current versions and status
   - Recommendations and priority levels
   - Sortable for analysis

3. **DEPENDENCY_ANALYSIS_SUMMARY.txt** - Quick reference guide
   - Key findings and statistics
   - Files requiring updates (36 projects)
   - Update strategy by phase
   - Notes and recommendations

---

## Key Findings Summary

### 5 Critical Updates Required (Priority: HIGH)

1. **Microsoft.NET.Test.Sdk** - 6 inconsistent versions
   - Current: 17.6.0, 17.8.0, 17.11.1, 17.12.*, 17.14.1, 18.3.0
   - Target: 18.3.0
   - Impact: 20+ test projects

2. **xunit** - 5 inconsistent versions
   - Current: 2.*, 2.4.2, 2.6.2, 2.9.*, 2.9.0, 2.9.3
   - Target: 2.9.3
   - Impact: 15+ test projects

3. **FluentAssertions** - Major version split
   - Current: 6.12.*, 8.8.0 (split between 6.x and 8.x)
   - Target: 8.8.0
   - Impact: All test projects (breaking change)

4. **HPD-Agent.* Internal Packages** - Version bump for release
   - Current: 0.2.0 (all 10 packages)
   - Target: 0.4.0
   - Impact: All source and test projects

5. **Microsoft.CodeAnalysis.Analyzers** - Version inconsistency
   - Current: 3.3.4, 3.11.0
   - Target: 3.11.0

### Statistics

- **Total packages analyzed:** 113
- **Files requiring updates:** 36 .csproj files
- **Wildcard constraints:** 14 packages using wildcards (should be pinned)
- **Beta/Preview versions:** 8 packages (mostly intentional)
- **High priority updates:** 5 packages
- **Medium priority updates:** 12 packages
- **Low priority/adequate:** 96 packages

---

## Quick Decision Matrix

| Package | Current | Target | Priority | Action |
|---------|---------|--------|----------|--------|
| Microsoft.NET.Test.Sdk | Mixed | 18.3.0 | HIGH | Standardize all projects |
| xunit | Mixed | 2.9.3 | HIGH | Standardize all projects |
| FluentAssertions | 6.12.*/8.8.0 | 8.8.0 | HIGH | Update 6.x projects, test for breaking changes |
| HPD-Agent.* | 0.2.0 | 0.4.0 | HIGH | Bulk update across codebase |
| Microsoft.CodeAnalysis.Analyzers | 3.3.4/3.11.0 | 3.11.0 | HIGH | Standardize |
| Microsoft.Extensions.Logging | 9.0.5/10.x | 10.0.3 | MEDIUM | Standardize .NET 10 projects |
| coverlet.collector | 6.0.0/6.0.4/8.0.0 | 8.0.0 | MEDIUM | Standardize test projects |
| xunit.runner.visualstudio | Mixed | 3.1.5 | MEDIUM | Standardize to latest |
| Anthropic | 12.* | 12.x.x (pin) | MEDIUM | Evaluate and pin exact version |
| Azure.AI.OpenAI | 2.*/beta | 2.x.x stable | MEDIUM | Evaluate stable release |

---

## Affected Project Categories

### Test Projects (50+ files)
These projects are most impacted due to testing infrastructure:
- HPD-RAG.*.Tests.csproj
- HPD-Agent.*.Tests.csproj
- HPD.MultiAgent.Tests.csproj
- Helium.*.Tests.csproj
- Rhodium.*.Tests.csproj

**Key packages to update:**
- Microsoft.NET.Test.Sdk
- xunit
- xunit.runner.visualstudio
- FluentAssertions
- coverlet.collector

### Source Projects (100+ files)
Infrastructure and framework implementations:

**Key updates needed:**
- HPD-Agent.* packages (0.2.0 → 0.4.0)
- Microsoft.Extensions.* (standardize versions)
- Microsoft.CodeAnalysis.* (analyzers)
- AI/ML provider packages (Anthropic, OpenAI, Azure)

### Shared Library Projects (37+ files)
Located in `/dotnet/src/shared/`:

**Helium.*.Tests & Rhodium.*.Tests:**
- Need Microsoft.NET.Test.Sdk updates
- coverlet.collector standardization

---

## Update Strategy by Phase

### Phase 1: Immediate (Blocking Issues) - Week 1
```
1. Update all HPD-Agent.* packages: 0.2.0 → 0.4.0
2. Microsoft.NET.Test.Sdk: Standardize to 18.3.0
3. xunit: Standardize to 2.9.3
4. FluentAssertions: 6.12.* → 8.8.0 (with testing)
5. Microsoft.CodeAnalysis.Analyzers: 3.3.4 → 3.11.0
```

**Estimated impact:** 50-70 file changes

### Phase 2: Standardization - Week 2
```
6. Pin all wildcard versions to exact versions
7. Microsoft.Extensions.Logging: 9.0.5 → 10.0.3
8. coverlet.collector: 6.0.x → 8.0.0
9. xunit.runner.visualstudio: Mixed → 3.1.5
10. Microsoft.AspNetCore.*: 10.0.* → 10.0.3
```

**Estimated impact:** 30-40 file changes

### Phase 3: Provider Reviews - Week 2-3
```
11. Anthropic SDK: Evaluate and pin exact 12.x.x
12. Azure.AI.OpenAI: Assess beta→stable transition
13. Verify all AI/ML provider compatibility
14. Review Microsoft.Extensions.AI.* versions
```

**Estimated impact:** 5-10 file changes

### Phase 4: Testing & Validation - Week 3-4
```
15. Full test suite execution
16. AOT compilation verification
17. Multi-framework testing (.NET 8, 9, 10)
18. Breaking change documentation
```

---

## Wildcard Packages to Address

The following packages use wildcard constraints and should be replaced with exact versions for reproducible builds:

| Package | Current | Recommended |
|---------|---------|------------|
| Anthropic | 12.* | 12.x.x (specific version TBD) |
| AspNet.Security.OAuth.GitHub | 10.0.* | 10.0.x |
| Azure.Identity | 1.* | 1.x.x |
| Azure.AI.OpenAI | 2.* | 2.x.x stable |
| Markdig | 0.38.* | 0.38.x |
| Microsoft.AspNetCore.Authentication.Google | 10.0.* | 10.0.3 |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.* | 10.0.3 |
| Microsoft.AspNetCore.Authentication.MicrosoftAccount | 10.0.* | 10.0.3 |
| Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | 10.0.* | 10.0.3 |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.* | 10.0.3 |
| Microsoft.Extensions.Caching.Abstractions | 10.* | 10.0.3 |
| Microsoft.Extensions.Caching.Memory | 10.* | 10.0.3 |
| Microsoft.Extensions.DependencyInjection | 10.* | 10.0.3 |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.* | 10.0.3 |
| Microsoft.Extensions.FileSystemGlobbing | 10.* | 10.0.3 |
| Microsoft.Extensions.Logging | 10.* | 10.0.3 |
| Microsoft.Extensions.Logging.Abstractions | 10.* | 10.0.3 |

---

## Breaking Changes to Consider

### FluentAssertions 6.12.* → 8.8.0 (MAJOR)
This is a major version update with potential breaking changes:
- Review assertion syntax in test files
- Ensure new API compatibility
- Test all assertion calls

**Mitigation:**
1. Update a subset of projects first
2. Run full test suite
3. Fix any syntax/API issues
4. Roll out to remaining projects

### Other Updates
Most other updates (minor/patch) should be backward compatible:
- Microsoft.NET.Test.Sdk: 17.x → 18.x (minor update)
- xunit: 2.9.0 → 2.9.3 (patch update)
- coverlet.collector: 6.x → 8.x (major, but code-level compatible)

---

## Files Requiring Attention

### High-Impact Projects (Multiple Updates)
```
/dotnet/src/shared/Helium/test/Helium.*.Tests.csproj
- Microsoft.NET.Test.Sdk: 17.14.1 → 18.3.0
- coverlet.collector: 6.0.4 → 8.0.0

/dotnet/src/shared/Rhodium/test/Rhodium.*.Tests.csproj
- Multiple SDK and testing package updates needed

/dotnet/src/shared/Rhodium/test/Rhodium.Data.Tests.csproj
- Microsoft.NET.Test.Sdk: 17.8.0 → 18.3.0
- xunit: 2.6.2 → 2.9.3
- xunit.runner.visualstudio: 2.5.4 → 3.1.5
- coverlet.collector: 6.0.0 → 8.0.0
```

### Medium-Impact Projects
```
/dotnet/test/HPD-Agent.Tests.csproj
/dotnet/test/HPD-Agent.OpenApi.Tests.csproj
/dotnet/test/HPD-RAG.IntegrationTests.csproj
- Multiple testing framework updates
```

### Single-Update Projects
```
/dotnet/src/HPD-ML.Framework/HPD.ML.SourceGen/HPD.ML.SourceGen.csproj
- Microsoft.CodeAnalysis.Analyzers: 3.3.4 → 3.11.0

/dotnet/src/shared/HPD-VCS/HPD-VCS.csproj
- Microsoft.Extensions.Logging: 9.0.5 → 10.0.3
```

---

## Preview/Beta Packages (Intentional - Monitor Only)

These packages use preview/beta versions and should be monitored for stable releases:

| Package | Version | Status | Action |
|---------|---------|--------|--------|
| Azure.AI.Inference | 1.0.0-beta.5 | Beta | Track for stable |
| Azure.AI.Projects | 2.0.0-beta.1 | Beta | Experimental feature |
| Microsoft.Extensions.AI.AzureAIInference | 10.0.0-preview.1.25559.3 | Preview | Track for stable |
| Microsoft.Extensions.AI.Evaluation.NLP | 10.3.0-preview.1.26109.11 | Preview | Track for stable |
| Microsoft.Extensions.DataIngestion | 10.3.0-preview.1.26109.11 | Preview | Track for stable |
| Microsoft.Extensions.DataIngestion.MarkItDown | 10.3.0-preview.1.26109.11 | Preview | Track for stable |
| Microsoft.Extensions.DataIngestion.Markdig | 10.3.0-preview.1.26109.11 | Preview | Track for stable |
| Microsoft.OpenApi.Readers | 2.0.0-preview.13 | Preview | Track for stable |
| System.Numerics.Tensors | 10.0.0-preview.5.25277.114 | Preview | .NET 10 preview |
| HuggingFace | 0.4.1-dev.23 | Dev | Experimental feature |

---

## .NET Target Framework Considerations

HPD-Agent-Framework currently supports:
- **.NET 6** (EOL) - Consider deprecating for v0.4.0
- **.NET 8** (LTS) - Active support recommended
- **.NET 9** (Current) - Active support required
- **.NET 10** (Latest) - Primary target recommended

**Recommendation:** Review .NET 6 support requirements. If not needed by major customers, dropping support simplifies dependency management.

---

## Performance & Compatibility Notes

### Expected Impact
- **Performance:** No negative impact expected. Newer versions typically include optimizations.
- **Breaking changes:** Minimal, except for FluentAssertions 6→8
- **Compatibility:** All updates target .NET Standard 2.1+ - no issues expected
- **AOT compilation:** Verify post-update for projects using AOT

### Testing Recommendations
1. Run full unit test suite after each phase
2. Verify integration tests with updated packages
3. Test AOT compilation if applicable
4. Performance baseline comparison (optional)
5. Multi-framework testing across all supported .NET versions

---

## How to Use These Reports

### For Release Planning
- Use DEPENDENCY_ANALYSIS_SUMMARY.txt for quick overview
- Reference the 4-phase strategy for timeline planning
- Identify blocking issues that need immediate attention

### For Development
- Use HPD-Agent-Framework-Dependencies.csv to search specific packages
- Reference file paths in DEPENDENCY_ANALYSIS_v0.4.0.md for detailed info
- Follow the update strategy phase-by-phase

### For Testing
- Verify test projects get priority in Phase 1
- Focus on FluentAssertions compatibility testing
- Ensure all test frameworks standardize in Phase 2

### For Documentation
- Reference breaking changes section when creating release notes
- Document wildcard → pinned version changes
- Include .NET 6 deprecation decision (if applicable)

---

## Next Steps

1. **Validate findings** with development team
2. **Review .NET 6 support** decision
3. **Plan update timeline** (4-week estimate)
4. **Assign update tasks** by project groups
5. **Execute Phase 1** updates
6. **Run comprehensive testing**
7. **Document breaking changes** for release notes
8. **Prepare release announcement**

---

## Questions or Issues?

If you need clarification on any findings:
1. Check the detailed DEPENDENCY_ANALYSIS_v0.4.0.md report
2. Search HPD-Agent-Framework-Dependencies.csv for specific packages
3. Review the affected projects list for similar cases
4. Consult the update strategy phases for implementation guidance

---

**Report Generated:** March 22, 2026
**Analysis Type:** Comprehensive NuGet Dependency Review
**Scope:** 187 .csproj files, 113 unique packages, 36 files needing updates
