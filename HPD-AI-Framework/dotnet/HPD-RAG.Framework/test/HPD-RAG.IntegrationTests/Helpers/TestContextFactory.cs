using HPD.RAG.Core.Context;

namespace HPD.RAG.IntegrationTests.Helpers;

internal static class TestContextFactory
{
    public static Dictionary<string, object> CreateInputs(
        string[]? filePaths = null,
        Dictionary<string, string>? runTags = null,
        string? collection = null)
    {
        var inputs = new Dictionary<string, object>();

        if (filePaths != null)
            inputs["file_paths"] = filePaths;

        if (runTags != null)
            inputs["tags"] = runTags;

        if (collection != null)
            inputs["collection"] = collection;

        return inputs;
    }
}
