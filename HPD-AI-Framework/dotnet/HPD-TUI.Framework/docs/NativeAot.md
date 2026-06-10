# Native AOT Guide

HPD.TUI avoids reflection, regex, dynamic code generation, and runtime parser dependencies in the render path.

Validation command:

```bash
dotnet publish src/HPD-TUI.csproj -c Release -f net8.0 -r osx-arm64 -p:PublishAot=true -p:PublishTrimmed=true
```

Guidelines for new components:

- Prefer `ReadOnlySpan<char>` and caller-owned buffers in render methods.
- Do not store spans after `Render` returns.
- Keep setup allocations outside the frame hot path.
- Avoid `Regex` and reflection-based serialization in framework code.
- Add tests for ANSI output, Unicode width, and input behavior.
