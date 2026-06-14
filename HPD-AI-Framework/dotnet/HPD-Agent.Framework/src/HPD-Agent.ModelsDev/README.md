# HPD-Agent.ModelsDev

Reusable models.dev catalog access for HPD Agent applications.

## Install

```bash
dotnet add package HPD-Agent.ModelsDev
```

## Included

- Fetching and disk caching through `ModelsDevStore`
- Source-generated models.dev DTO serialization
- Provider identifier mappings
- Model ID parsing and alias resolution
- Optional HPD provider registration and authentication status

This package has no TUI dependency. Applications decide how models are displayed and selected. A TUI application can adapt `ModelsDevStore` to `IAgentTuiModelCatalog`; a web application can build its own page over the same store.
