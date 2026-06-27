# HPD-Agent Replicate Provider

Replicate provider for HPD Agent image generation.

Provider key: `replicate`

Environment aliases:

- `REPLICATE_API_KEY`
- `REPLICATE_API_TOKEN`

The HPD provider intentionally registers only the image generation family. Replicate's broader prediction, training, deployment, and file APIs are not exposed as HPD provider families by this package.

```csharp
using HPD.Agent;
using HPD.Agent.Providers.Replicate;

var agent = await new AgentBuilder()
    .WithReplicateImageGeneration(
        model: "black-forest-labs/flux-schnell",
        configure: options =>
        {
            options.Input = new()
            {
                ["aspect_ratio"] = "16:9",
                ["num_inference_steps"] = 4
            };
        })
    .BuildAsync();
```

`ModelName` should use Replicate `owner/model` format, or set `ReplicateProviderConfig.ModelOwner` and pass the model name alone.
