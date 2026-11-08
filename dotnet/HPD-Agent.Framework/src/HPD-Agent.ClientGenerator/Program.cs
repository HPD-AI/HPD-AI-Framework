using HPD.Agent.ClientGenerator;

var options = GeneratorOptions.Parse(args);
var generator = new CSharpSdkGenerator();
var manifest = await generator.GenerateAsync(options, CancellationToken.None);
Console.WriteLine(
    $"Generated {manifest.OperationCount} operations, {manifest.ModelCount} models, " +
    $"{manifest.EventCount} events, {manifest.InputCount} public input variants, and " +
    $"{manifest.RuntimeSchemaCount} typed runtime schemas.");
Console.WriteLine($"OpenAPI SHA-256: {manifest.OpenApiSha256}");
Console.WriteLine($"Output: {Path.GetFullPath(options.OutputDirectory)}");
