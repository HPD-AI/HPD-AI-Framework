using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HPD.Events.Core;
using HPD.RAG.Core.Context;
using HPD.RAG.Core.Events;
using HPD.RAG.Evaluation;
using HPD.RAG.Pipeline.Internal;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Orchestration;

namespace HPD.RAG.Pipeline;

/// <summary>
/// Options for <see cref="MragEvaluationPipeline.BackfillAsync"/>.
/// </summary>
public sealed class BackfillOptions
{
    /// <summary>
    /// Maximum number of partitions to evaluate in parallel.
    /// Default: 1 (sequential). Increase for independent, stateless evaluations.
    /// </summary>
    public int MaxParallelPartitions { get; set; } = 1;

    /// <summary>
    /// When <c>true</c>, partitions that already have stored results are skipped.
    /// Default: false — re-evaluate every partition.
    /// </summary>
    public bool SkipExisting { get; set; } = false;
}

/// <summary>
/// A compiled, ready-to-run evaluation pipeline produced by
/// <see cref="MragPipeline.BuildEvaluationAsync"/>.
///
/// <para>
/// The primary entry point is <see cref="BackfillAsync"/>, which runs the pipeline
/// once per <see cref="PartitionKey"/> in the supplied sequence, streaming
/// <see cref="MragEvent"/> instances for each partition as it executes.
/// </para>
///
/// <para>
/// Evaluation pipelines consist of one or more evaluator handler nodes
/// (<c>RelevanceEvalHandler</c>, <c>GroundednessEvalHandler</c>, etc.) terminated by
/// <c>WriteEvalResultHandler</c>. Partition key segments map to the conventional
/// input socket names: <c>scenario</c>, <c>iteration</c>, <c>execution</c>.
/// </para>
/// </summary>
public sealed class MragEvaluationPipeline
{
    private readonly Graph _graph;
    private readonly IServiceProvider _pipelineServices;

    /// <summary>Human-readable name of this pipeline.</summary>
    public string PipelineName { get; }

    internal MragEvaluationPipeline(string pipelineName, Graph graph, IServiceProvider pipelineServices)
    {
        PipelineName = pipelineName;
        _graph = graph;
        _pipelineServices = pipelineServices;
    }

    // ------------------------------------------------------------------ //
    // BackfillAsync                                                        //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Runs the evaluation pipeline once per <see cref="PartitionKey"/> in
    /// <paramref name="partitions"/>, streaming <see cref="MragEvent"/> instances.
    ///
    /// <para>
    /// Sequential by default. Set <see cref="BackfillOptions.MaxParallelPartitions"/> to &gt; 1
    /// for concurrent evaluation (only when handlers are stateless and thread-safe).
    /// </para>
    ///
    /// <para>
    /// Raw graph events are translated to strongly-typed MRAG evaluation events by
    /// <see cref="MragEventMapper.MapEvaluationEvent"/>. The score accumulator is shared
    /// across all partitions so that <see cref="EvalCompletedEvent.AverageScores"/> reflects
    /// the macro-average over every evaluated partition.
    /// </para>
    /// </summary>
    public async IAsyncEnumerable<MragEvent> BackfillAsync(
        IEnumerable<PartitionKey> partitions,
        BackfillOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        options ??= new BackfillOptions();

        var partitionList = partitions.ToList();

        // Score accumulator shared across all partition runs for macro-average computation.
        var scoreAccumulator = new List<IReadOnlyDictionary<string, double>>();

        if (options.MaxParallelPartitions <= 1)
        {
            // Sequential — simpler and safer
            foreach (var partition in partitionList)
            {
                await foreach (var evt in RunPartitionAsync(partition, scoreAccumulator, ct).ConfigureAwait(false))
                {
                    yield return evt;
                }
            }
        }
        else
        {
            // Parallel — bound concurrency with a semaphore; interleave events on an unbounded channel.
            // scoreAccumulator accesses are serialised through the channel write ordering; concurrent
            // List.Add calls from multiple tasks are safe because each task appends a distinct object
            // and List<T> is not read until all producer tasks complete.
            var semaphore = new SemaphoreSlim(options.MaxParallelPartitions);
            var channel = Channel.CreateUnbounded<MragEvent>();

            var producerTask = Task.Run(async () =>
            {
                try
                {
                    var tasks = partitionList.Select(async partition =>
                    {
                        await semaphore.WaitAsync(ct).ConfigureAwait(false);
                        try
                        {
                            await foreach (var evt in RunPartitionAsync(partition, scoreAccumulator, ct)
                                               .ConfigureAwait(false))
                            {
                                await channel.Writer.WriteAsync(evt, ct).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, ct);

            await foreach (var evt in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return evt;
            }

            await producerTask.ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------ //
    // Private: per-partition execution                                     //
    // ------------------------------------------------------------------ //

    private async IAsyncEnumerable<MragEvent> RunPartitionAsync(
        PartitionKey partition,
        List<IReadOnlyDictionary<string, double>> scoreAccumulator,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var eventCoordinator = new EventCoordinator();
        var context = BuildContext(partition, eventCoordinator);

        var orchestrator = new GraphOrchestrator<MragPipelineContext>(
            _pipelineServices,
            checkpointStore: null);

        var executionTask = Task.Run(async () =>
        {
            try
            {
                await orchestrator.ExecuteAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                eventCoordinator.Emit(new GraphDiagnosticEvent
                {
                    Level = LogLevel.Error,
                    Source = "MragEvaluationPipeline",
                    Message = ex.Message,
                    Exception = ex
                });
            }
        }, ct);

        await foreach (var evt in eventCoordinator.ReadAllAsync(ct).ConfigureAwait(false))
        {
            var mapped = MragEventMapper.MapEvaluationEvent(evt, PipelineName, scoreAccumulator);
            if (mapped != null)
                yield return mapped;

            if (context.IsComplete || context.IsCancelled)
                break;
        }

        await executionTask.ConfigureAwait(false);
    }

    private MragPipelineContext BuildContext(PartitionKey partition, EventCoordinator ec)
    {
        var ctx = new MragPipelineContext(
            executionId: Guid.NewGuid().ToString("N"),
            graph: _graph,
            services: _pipelineServices,
            pipelineName: PipelineName)
        {
            EventCoordinator = ec
        };

        // Map partition segments to conventional evaluation input socket names.
        // WriteEvalResultHandler expects: scenario, iteration, execution.
        var segs = partition.Segments;
        if (segs.Count > 0) ctx.Channels["scenario"].Set(segs[0]);
        if (segs.Count > 1) ctx.Channels["iteration"].Set(segs[1]);
        if (segs.Count > 2) ctx.Channels["execution"].Set(segs[2]);

        return ctx;
    }
}
