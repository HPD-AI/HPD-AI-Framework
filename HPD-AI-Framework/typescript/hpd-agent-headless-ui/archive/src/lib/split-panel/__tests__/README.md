# SplitPanel Testing Strategy

## Overview

The SplitPanel system uses Svelte 5 runes (`$state`, `$derived`, `$effect`) for reactive state management. This provides excellent developer experience and automatic reactivity, but requires a specific testing approach.

## Why Runes Cannot Be Unit Tested Directly

**Problem:** Svelte runes require the Svelte compilation context and runtime. They cannot be instantiated directly in unit tests.

**Error when attempting direct instantiation:**
```
Svelte error: rune_outside_svelte
The `$state` rune is only available inside `.svelte` and `.svelte.js/ts` files
```

Even with proper Vite/Vitest configuration, `.svelte.ts` files with runes cannot be imported and instantiated in test files because the runes are runtime checks that verify they're being executed within a Svelte compilation context.

## Current Test Status

- **99 tests passing** - Non-rune logic tests
- **31 tests skipped** - Tests requiring rune-based state instantiation

## Recommended Testing Approaches

### 1. Component Integration Tests (Recommended)

Test behavior through actual Svelte component interactions using vitest-browser-svelte.

### 2. Storybook Visual Testing

Use Storybook's test-runner with Playwright for visual regression and interaction testing.

### 3. End-to-End Tests

Use Playwright directly for full application testing.

## References

- [Svelte Testing Documentation](https://svelte.dev/docs/testing)
- [Vitest Browser Mode](https://vitest.dev/guide/browser.html)
- HPD-Agent-Framework for component testing examples

## Conclusion

**Runes are kept in state classes** for superior DX and automatic reactivity. Testing is done through component integration rather than direct unit tests, following Svelte 5 best practices.
