using HPD.RAG.VectorStores.InMemory;

namespace HPD.RAG.IntegrationTests.Infrastructure;

/// <summary>
/// Assembly-level fixture that ensures all MRAG provider modules are initialized
/// before any test runs.
///
/// The InMemory vector store provider registers itself via [ModuleInitializer], which
/// fires when the assembly is first loaded. In some xUnit test execution paths the
/// assembly may not be loaded until it is explicitly referenced in code. This fixture
/// forces the load by calling the public Initialize() method directly.
/// </summary>
public sealed class MragTestAssemblyFixture : IDisposable
{
    public MragTestAssemblyFixture()
    {
        // Force the InMemory vector store [ModuleInitializer] to run.
        // Calling Initialize() is idempotent — ConcurrentDictionary.TryAdd is safe to call twice.
        InMemoryVectorStoreModule.Initialize();
    }

    public void Dispose() { }
}
