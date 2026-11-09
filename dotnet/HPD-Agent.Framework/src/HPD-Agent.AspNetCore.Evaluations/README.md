# HPD-Agent.AspNetCore.Evaluations

Optional ASP.NET Core endpoints for evaluating HPD Agent turns and exposing
evaluation results through the hosted agent API.

## Install

```bash
dotnet add package HPD-Agent.AspNetCore.Evaluations
```

## Use When

Use this package when an HPD-Agent ASP.NET host needs the evaluation endpoint
surface. The package targets `net8.0`, `net9.0`, and `net10.0`.

## Compatibility

Starting with `1.0.0`, the documented evaluation endpoint contracts follow
semantic versioning. Breaking changes require a new major version and a
documented migration path.
