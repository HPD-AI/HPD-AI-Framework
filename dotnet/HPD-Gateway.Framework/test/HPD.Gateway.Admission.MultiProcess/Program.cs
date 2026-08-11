using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using HPD.Gateway;
using HPD.Gateway.Admission.Redis;

return args.FirstOrDefault() switch
{
    "worker" => await RunWorkerAsync(WorkerOptions.Parse(args[1..])),
    "controller" => await RunControllerAsync(ControllerOptions.Parse(args[1..])),
    _ => throw new ArgumentException("Expected worker or controller mode.")
};

static async Task<int> RunWorkerAsync(WorkerOptions options)
{
    var timeoutProvider = new HangingBenchmarkProvider();
    WebApplication backend = WebApplication.CreateSlimBuilder().Build();
    backend.Urls.Add($"http://127.0.0.1:{options.BackendPort}");
    backend.MapGet("/{**path}", async (HttpContext context) =>
    {
        if (context.Request.Query.TryGetValue("delay", out var delay) &&
            int.TryParse(delay, out int milliseconds) && milliseconds is > 0 and <= 10_000)
            await Task.Delay(milliseconds, context.RequestAborted);
        return Results.Text(options.ReplicaId);
    });
    await backend.StartAsync();

    GatewayConfiguration configuration = CreateConfiguration(options);
    GatewayCanonicalDocument document = configuration.ToCanonicalDocument();
    WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
    builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");
    builder.Services.AddHpdGateway(gateway => gateway
        .EnableCoreDeclarations()
        .AddTrafficAdmission(admission =>
        {
            admission.UseRedis("redis", redis =>
            {
                redis.AuthorityId = options.AuthorityId;
                redis.Configuration = options.RedisConfiguration;
                redis.KeyPrefix = options.KeyPrefix;
                redis.OperationTimeout = TimeSpan.FromMilliseconds(options.OperationTimeoutMilliseconds);
                redis.MaximumConcurrentInvocations = options.MaximumProviderConcurrency;
            });
            admission.AddLocalConcurrency("node-concurrency", local =>
            {
                local.MinimumLimit = 1;
                local.MaximumLimit = 1_024;
                local.MinimumQueue = 0;
                local.MaximumQueue = 1_024;
            });
            admission.AddLocalFixedWindow("local-benchmark", local =>
            {
                local.MinimumLimit = 1;
                local.MaximumLimit = 100_000_000;
                local.MinimumPeriod = TimeSpan.FromSeconds(1);
                local.MaximumPeriod = TimeSpan.FromHours(1);
            });
            admission.AddLocalFixedWindow("projection-benchmark", local =>
            {
                local.Partition = TrafficAdmissionPartitionKind.SourceIp;
                local.MinimumLimit = 1;
                local.MaximumLimit = 100_000_000;
                local.MinimumPeriod = TimeSpan.FromSeconds(1);
                local.MaximumPeriod = TimeSpan.FromHours(1);
            });
            admission.AddSharedProvider("timeout-benchmark", timeoutProvider, shared =>
            {
                shared.AuthorityId = "benchmark-timeout";
                shared.BehaviorIdentity = new ContentHash("sha-256", new string('e', 64));
                shared.OperationTimeout = TimeSpan.FromMilliseconds(50);
                shared.MaximumConcurrentInvocations = 2;
            });
            admission.AddSharedFixedWindow("timeout-benchmark", "timeout-benchmark");
            if (options.FailureDisposition == "fallback")
                admission.AddLocalFixedWindow("node-fallback");
            void ConfigureShared(GatewaySharedAdmissionProfileOptions shared)
            {
                shared.FailureDisposition = options.FailureDisposition switch
                {
                    "reject" => TrafficAdmissionFailureDisposition.Reject,
                    "bypass" => TrafficAdmissionFailureDisposition.Bypass,
                    "fallback" => TrafficAdmissionFailureDisposition.LocalFallback,
                    _ => throw new ArgumentException("Unsupported failure disposition.")
                };
                shared.LocalFallbackProfile = options.FailureDisposition == "fallback" ? "node-fallback" : null;
            }
            switch (options.Algorithm)
            {
                case "fixed": admission.AddSharedFixedWindow("fleet-rate", "redis", ConfigureShared); break;
                case "sliding": admission.AddSharedSlidingWindow("fleet-rate", "redis", ConfigureShared); break;
                case "token": admission.AddSharedTokenBucket("fleet-rate", "redis", ConfigureShared); break;
                default: throw new ArgumentException("Unsupported algorithm.");
            }
        })
        .UseInitialCandidate(new GatewayNodeActivationRequest(
            "evidence", options.ReplicaId, new CandidateId($"candidate-{options.Generation}"),
            options.AuthorityId, "epoch-1", options.Generation, document.Utf8Json)));

    WebApplication app = builder.Build();
    app.MapGet("/worker/live", () => Results.Text($"{options.ReplicaId}:{options.Generation}"));
    app.MapGet("/worker/redis-health", (IGatewayRedisAdmissionHealth health) =>
    {
        GatewayRedisAdmissionHealthSnapshot snapshot = health.GetSnapshot();
        return Results.Text($"connected={snapshot.IsConnected};acquired={snapshot.Acquired};rejected={snapshot.Rejected};unavailable={snapshot.Unavailable};indeterminate={snapshot.Indeterminate}");
    });
    app.MapGet("/worker/runtime-metrics", () => Results.Text(string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"allocated={GC.GetTotalAllocatedBytes(false)};gen0={GC.CollectionCount(0)};gen1={GC.CollectionCount(1)};gen2={GC.CollectionCount(2)};timeout-active={timeoutProvider.Active};timeout-max={timeoutProvider.MaximumObserved}")));
    app.MapPost("/worker/release-timeouts", () => { timeoutProvider.Release(); return Results.Ok(); });
    app.MapHpdGateway();
    try { await app.RunAsync(); }
    finally { await backend.DisposeAsync(); }
    return 0;
}

static GatewayConfiguration CreateConfiguration(WorkerOptions options)
{
    TrafficAdmissionEntry rate = options.Algorithm switch
    {
        "fixed" => new FixedWindowAdmissionEntry
        {
            Profile = "fleet-rate", PermitLimit = options.Limit,
            Window = TimeSpan.FromMilliseconds(options.PeriodMilliseconds)
        },
        "sliding" => new SlidingWindowAdmissionEntry
        {
            Profile = "fleet-rate", PermitLimit = options.Limit,
            Window = TimeSpan.FromMilliseconds(options.PeriodMilliseconds),
            SegmentsPerWindow = options.Segments
        },
        "token" => new TokenBucketAdmissionEntry
        {
            Profile = "fleet-rate", TokenLimit = options.Limit,
            TokensPerPeriod = options.TokensPerPeriod,
            ReplenishmentPeriod = TimeSpan.FromMilliseconds(options.PeriodMilliseconds)
        },
        _ => throw new ArgumentException("Unsupported algorithm.")
    };
    return new GatewayConfiguration
    {
        SchemaVersion = new GatewaySchemaVersion(1, 0),
        CanonicalizationVersion = 1,
        Upstreams =
        [
            new UpstreamDeclaration
            {
                Id = new UpstreamId("backend"),
                Endpoints = new StaticEndpointSource
                {
                    Destinations =
                    [
                        new DestinationDeclaration
                        {
                            Id = new DestinationId("local"),
                            Address = new Uri($"http://127.0.0.1:{options.BackendPort}")
                        }
                    ]
                }
            }
        ],
        Routes =
        [
            new RouteDeclaration
            {
                Id = new RouteId("baseline"),
                Match = new HttpRouteMatch { Path = "/baseline/{**catch-all}" },
                Upstream = new UpstreamId("backend")
            },
            new RouteDeclaration
            {
                Id = new RouteId("local"),
                Match = new HttpRouteMatch { Path = "/local/{**catch-all}" },
                Upstream = new UpstreamId("backend"),
                Declarations = new RouteDeclarations
                {
                    TrafficAdmission = new DeclarationReference<TrafficAdmissionPlan>
                    {
                        Inline = new TrafficAdmissionPlan
                        {
                            Entries =
                            [
                                new FixedWindowAdmissionEntry
                                {
                                    Profile = "local-benchmark",
                                    PermitLimit = 100_000_000,
                                    Window = TimeSpan.FromHours(1)
                                }
                            ]
                        }
                    }
                }
            },
            new RouteDeclaration
            {
                Id = new RouteId("projection"),
                Match = new HttpRouteMatch { Path = "/projection/{**catch-all}" },
                Upstream = new UpstreamId("backend"),
                Declarations = new RouteDeclarations
                {
                    TrafficAdmission = new DeclarationReference<TrafficAdmissionPlan>
                    {
                        Inline = new TrafficAdmissionPlan
                        {
                            Entries = [new FixedWindowAdmissionEntry
                            {
                                Profile = "projection-benchmark", PermitLimit = 100_000_000,
                                Window = TimeSpan.FromHours(1)
                            }]
                        }
                    }
                }
            },
            new RouteDeclaration
            {
                Id = new RouteId("timeout"),
                Match = new HttpRouteMatch { Path = "/timeout/{**catch-all}" },
                Upstream = new UpstreamId("backend"),
                Declarations = new RouteDeclarations
                {
                    TrafficAdmission = new DeclarationReference<TrafficAdmissionPlan>
                    {
                        Inline = new TrafficAdmissionPlan
                        {
                            Entries = [new FixedWindowAdmissionEntry
                            {
                                Profile = "timeout-benchmark", PermitLimit = 100_000_000,
                                Window = TimeSpan.FromHours(1)
                            }]
                        }
                    }
                }
            },
            new RouteDeclaration
            {
                Id = new RouteId("quota"),
                Match = new HttpRouteMatch { Path = "/quota/{**catch-all}" },
                Upstream = new UpstreamId("backend"),
                Declarations = new RouteDeclarations
                {
                    TrafficAdmission = new DeclarationReference<TrafficAdmissionPlan>
                    {
                        Inline = new TrafficAdmissionPlan
                        {
                            Entries =
                            [
                                new ConcurrencyAdmissionEntry
                                {
                                    Profile = "node-concurrency",
                                    PermitLimit = options.LocalConcurrency,
                                    QueueLimit = 0
                                },
                                rate
                            ]
                        }
                    }
                }
            }
        ]
    };
}

static async Task<int> RunControllerAsync(ControllerOptions options)
{
    var workers = new List<WorkerProcess>();
    try
    {
        int initialReplicas = options.Scenario == "scale" ? Math.Min(2, options.Replicas) : options.Replicas;
        for (int index = 0; index < initialReplicas; index++)
        {
            var worker = WorkerProcess.Start(options, index);
            workers.Add(worker);
            await worker.WaitUntilReadyAsync();
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        if (options.Scenario == "benchmark")
        {
            BenchmarkSummary baseline = await MeasureAsync(client, workers[0], "baseline");
            BenchmarkSummary local = await MeasureAsync(client, workers[0], "local");
            BenchmarkSummary projection = await MeasureAsync(client, workers[0], "projection");
            BenchmarkSummary shared = await MeasureAsync(client, workers[0], "quota");
            RuntimeMetrics beforeTimeout = await ReadMetricsAsync(client, workers[0]);
            Task<HttpResponseMessage>[] hanging = Enumerable.Range(0, 2)
                .Select(_ => client.GetAsync($"http://127.0.0.1:{workers[0].Port}/timeout/measured")).ToArray();
            await Task.Delay(10);
            long saturatedStarted = Stopwatch.GetTimestamp();
            using HttpResponseMessage saturated = await client.GetAsync($"http://127.0.0.1:{workers[0].Port}/timeout/saturated");
            double saturationMicroseconds = (Stopwatch.GetTimestamp() - saturatedStarted) * 1_000_000d / Stopwatch.Frequency;
            HttpResponseMessage[] timedOut = await Task.WhenAll(hanging);
            try
            {
                if (saturated.StatusCode != HttpStatusCode.ServiceUnavailable ||
                    timedOut.Any(static response => response.StatusCode != HttpStatusCode.ServiceUnavailable))
                    throw new InvalidOperationException("Provider timeout/capacity evidence did not fail closed.");
            }
            finally
            {
                foreach (HttpResponseMessage response in timedOut) response.Dispose();
            }
            RuntimeMetrics afterTimeout = await ReadMetricsAsync(client, workers[0]);
            if (afterTimeout.TimeoutMaximumObserved != 2 || afterTimeout.TimeoutActive != 2)
                throw new InvalidOperationException("Provider invocation capacity was not retained through timeout.");
            using (await client.PostAsync($"http://127.0.0.1:{workers[0].Port}/worker/release-timeouts", null)) { }

            int underLoadSuccess = 0;
            using var loadCancellation = new CancellationTokenSource();
            Task load = Task.Run(async () =>
            {
                while (!loadCancellation.IsCancellationRequested)
                {
                    using HttpResponseMessage response = await client.GetAsync(
                        $"http://127.0.0.1:{workers[0].Port}/baseline/load", loadCancellation.Token);
                    if (response.IsSuccessStatusCode) Interlocked.Increment(ref underLoadSuccess);
                }
            });
            WorkerProcess replacement = WorkerProcess.Start(options, 1, generation: 2);
            workers.Add(replacement);
            await replacement.WaitUntilReadyAsync();
            using (HttpResponseMessage replacementResponse = await client.GetAsync(
                $"http://127.0.0.1:{replacement.Port}/quota/replacement"))
                replacementResponse.EnsureSuccessStatusCode();
            loadCancellation.Cancel();
            try { await load; } catch (OperationCanceledException) { }
            if (underLoadSuccess == 0)
                throw new InvalidOperationException("No request completed while the replacement generation activated.");
            Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"benchmark schema=hpd.gateway.admission.benchmark/v2 runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} os={System.Runtime.InteropServices.RuntimeInformation.OSDescription} architecture={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} transport=loopback-http1-sequential-keepalive warmup=250 measured=2000 baseline={baseline} process-local={local} partition-projection={projection} redis-shared={shared} provider-capacity=2 provider-max-observed={afterTimeout.TimeoutMaximumObserved} timeout-results=2x503 saturation-result={(int)saturated.StatusCode} saturation-latency={saturationMicroseconds:F3}us activation-under-load-success={underLoadSuccess} timeout-allocation-delta={afterTimeout.AllocatedBytes - beforeTimeout.AllocatedBytes} timeout-gc-delta={afterTimeout.Gen0 - beforeTimeout.Gen0}/{afterTimeout.Gen1 - beforeTimeout.Gen1}/{afterTimeout.Gen2 - beforeTimeout.Gen2}"));
            return 0;
        }
        if (options.Scenario == "recovery")
        {
            if (options.ControlDirectory is null)
                throw new InvalidOperationException("Recovery requires a control directory.");
            Directory.CreateDirectory(options.ControlDirectory);
            await ExpectStatusAsync(client, workers[0], HttpStatusCode.OK);
            File.WriteAllText(Path.Combine(options.ControlDirectory, "ready"), string.Empty);
            await WaitForFileAsync(Path.Combine(options.ControlDirectory, "outage"));
            await ExpectStatusAsync(client, workers[0], HttpStatusCode.ServiceUnavailable);
            File.WriteAllText(Path.Combine(options.ControlDirectory, "outage-observed"), string.Empty);
            await WaitForFileAsync(Path.Combine(options.ControlDirectory, "recovered"));
            long deadline = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
            while (true)
            {
                using HttpResponseMessage response = await client.GetAsync(
                    $"http://127.0.0.1:{workers[0].Port}/quota/accepted");
                if (response.StatusCode == HttpStatusCode.OK) break;
                if (Stopwatch.GetTimestamp() >= deadline)
                    throw new InvalidOperationException("The same Gateway worker did not recover its Redis authority.");
                await Task.Delay(100);
            }
            Console.WriteLine("HPD Gateway same-process Redis connection loss and recovery passed.");
            return 0;
        }
        if (options.Scenario == "unavailable")
        {
            HttpStatusCode expected = options.FailureDisposition == "reject"
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK;
            foreach (WorkerProcess worker in workers)
            {
                using HttpResponseMessage response = await client.GetAsync(
                    $"http://127.0.0.1:{worker.Port}/quota/accepted");
                if (response.StatusCode != expected)
                    throw new InvalidOperationException($"Unavailable provider returned {(int)response.StatusCode} instead of {(int)expected}.");
            }
            Console.WriteLine($"HPD Gateway unavailable-before-commit disposition '{options.FailureDisposition}' passed on {workers.Count} replicas.");
            return 0;
        }
        if (options.Scenario == "exhausted")
        {
            await ExpectStatusAsync(client, workers[0], HttpStatusCode.TooManyRequests);
            Console.WriteLine("HPD Gateway retained deployment quota remained exhausted after topology movement.");
            return 0;
        }
        if (options.Scenario == "concurrency")
        {
            int acquired = 0;
            int concurrencyRejected = 0;
            foreach (WorkerProcess worker in workers)
            {
                HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
                    client.GetAsync($"http://127.0.0.1:{worker.Port}/quota/accepted?delay=500")));
                acquired += responses.Count(static response => response.StatusCode == HttpStatusCode.OK);
                concurrencyRejected += responses.Count(static response => response.StatusCode == HttpStatusCode.TooManyRequests);
                foreach (HttpResponseMessage response in responses) response.Dispose();
            }
            if (acquired != workers.Count * 2 || concurrencyRejected != workers.Count)
                throw new InvalidOperationException($"Local concurrency mismatch: acquired={acquired}, rejected={concurrencyRejected}.");
            Console.WriteLine($"HPD Gateway local concurrency passed independently on {workers.Count} replicas.");
            return 0;
        }

        int admitted = 0;
        int rejected = 0;
        if (options.Scenario == "race")
        {
            HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, options.Limit * 2).Select(index =>
                client.GetAsync($"http://127.0.0.1:{workers[index % workers.Count].Port}/quota/accepted")));
            admitted = responses.Count(static response => response.StatusCode == HttpStatusCode.OK);
            rejected = responses.Count(static response => response.StatusCode == HttpStatusCode.TooManyRequests);
            foreach (HttpResponseMessage response in responses) response.Dispose();
            if (admitted != options.Limit || rejected != options.Limit)
                throw new InvalidOperationException($"Concurrent fleet boundary mismatch: admitted={admitted}, rejected={rejected}.");
            Console.WriteLine($"HPD Gateway concurrent fleet boundary passed with zero overshoot: admitted={admitted}, rejected={rejected}.");
            return 0;
        }
        for (int index = 0; index <= options.Limit; index++)
        {
            if (options.Scenario == "scale" && index == options.Limit / 2)
            {
                while (workers.Count < options.Replicas)
                {
                    var added = WorkerProcess.Start(options, workers.Count);
                    workers.Add(added);
                    await added.WaitUntilReadyAsync();
                }
            }
            if (options.Scenario == "scale-in" && index == options.Limit / 2)
            {
                int retained = Math.Max(1, workers.Count / 2);
                for (int remove = workers.Count - 1; remove >= retained; remove--)
                {
                    await workers[remove].DisposeAsync();
                    workers.RemoveAt(remove);
                }
            }
            if (options.Scenario == "restart" && index == options.Limit / 2)
            {
                await workers[0].DisposeAsync();
                workers[0] = WorkerProcess.Start(options, 0, generation: 2);
                await workers[0].WaitUntilReadyAsync();
            }
            int selected = options.Distribution switch
            {
                "sticky" => 0,
                "uneven" => index % 10 == 0 ? index % workers.Count : 0,
                _ => index % workers.Count
            };
            using HttpResponseMessage response = await client.GetAsync(
                $"http://127.0.0.1:{workers[selected].Port}/quota/accepted");
            if (response.StatusCode == HttpStatusCode.OK) admitted++;
            else if (response.StatusCode == HttpStatusCode.TooManyRequests) rejected++;
            else
            {
                string health = await client.GetStringAsync($"http://127.0.0.1:{workers[selected].Port}/worker/redis-health");
                throw new InvalidOperationException($"Unexpected worker response {(int)response.StatusCode}; {health}.");
            }
        }
        if (admitted != options.Limit || rejected != 1)
            throw new InvalidOperationException($"Fleet quota mismatch: admitted={admitted}, rejected={rejected}.");

        Console.WriteLine($"HPD Gateway multi-process admission passed: scenario={options.Scenario}, replicas={workers.Count}, distribution={options.Distribution}, admitted={admitted}, rejected={rejected}.");
        return 0;
    }
    finally
    {
        foreach (WorkerProcess worker in workers) await worker.DisposeAsync();
    }
}

static async Task<BenchmarkSummary> MeasureAsync(HttpClient client, WorkerProcess worker, string route)
{
    for (int index = 0; index < 250; index++)
    {
        using HttpResponseMessage warmup = await client.GetAsync($"http://127.0.0.1:{worker.Port}/{route}/warmup");
        warmup.EnsureSuccessStatusCode();
    }
    RuntimeMetrics before = await ReadMetricsAsync(client, worker);
    var samples = new long[2_000];
    for (int index = 0; index < samples.Length; index++)
    {
        long started = Stopwatch.GetTimestamp();
        using HttpResponseMessage response = await client.GetAsync($"http://127.0.0.1:{worker.Port}/{route}/measured");
        response.EnsureSuccessStatusCode();
        samples[index] = Stopwatch.GetTimestamp() - started;
    }
    Array.Sort(samples);
    RuntimeMetrics after = await ReadMetricsAsync(client, worker);
    double ToMicroseconds(long ticks) => ticks * 1_000_000d / Stopwatch.Frequency;
    return new(route, ToMicroseconds(samples[samples.Length / 2]),
        ToMicroseconds(samples[(int)Math.Ceiling(samples.Length * .95) - 1]),
        ToMicroseconds(samples[(int)Math.Ceiling(samples.Length * .99) - 1]),
        ToMicroseconds(samples[(int)Math.Ceiling(samples.Length * .999) - 1]),
        after.AllocatedBytes - before.AllocatedBytes,
        checked((int)(after.Gen0 - before.Gen0)), checked((int)(after.Gen1 - before.Gen1)),
        checked((int)(after.Gen2 - before.Gen2)));
}

static async Task<RuntimeMetrics> ReadMetricsAsync(HttpClient client, WorkerProcess worker)
{
    string value = await client.GetStringAsync($"http://127.0.0.1:{worker.Port}/worker/runtime-metrics");
    Dictionary<string, long> fields = value.Split(';').Select(static field => field.Split('='))
        .ToDictionary(static field => field[0], static field => long.Parse(field[1], System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal);
    return new(fields["allocated"], fields["gen0"], fields["gen1"], fields["gen2"],
        checked((int)fields["timeout-active"]), checked((int)fields["timeout-max"]));
}

static async Task ExpectStatusAsync(HttpClient client, WorkerProcess worker, HttpStatusCode expected)
{
    using HttpResponseMessage response = await client.GetAsync(
        $"http://127.0.0.1:{worker.Port}/quota/accepted");
    if (response.StatusCode != expected)
        throw new InvalidOperationException($"Expected {(int)expected}, received {(int)response.StatusCode}.");
}

static async Task WaitForFileAsync(string path)
{
    long deadline = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
    while (!File.Exists(path))
    {
        if (Stopwatch.GetTimestamp() >= deadline) throw new TimeoutException($"Timed out waiting for {path}.");
        await Task.Delay(50);
    }
}

sealed record BenchmarkSummary(
    string Path,
    double P50Microseconds,
    double P95Microseconds,
    double P99Microseconds,
    double P999Microseconds,
    long AllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections)
{
    public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"{Path}:p50={P50Microseconds:F3}us,p95={P95Microseconds:F3}us,p99={P99Microseconds:F3}us,p99.9={P999Microseconds:F3}us,allocated={AllocatedBytes},gc={Gen0Collections}/{Gen1Collections}/{Gen2Collections}");
}

sealed record RuntimeMetrics(long AllocatedBytes, long Gen0, long Gen1, long Gen2, int TimeoutActive, int TimeoutMaximumObserved);

sealed class HangingBenchmarkProvider : IGatewaySharedAdmissionProvider
{
    private readonly TaskCompletionSource<GatewaySharedAdmissionDecision> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active;
    private int _maximum;
    internal int Active => Volatile.Read(ref _active);
    internal int MaximumObserved => Volatile.Read(ref _maximum);

    public async ValueTask<GatewaySharedAdmissionDecision> AcquireAsync(
        GatewaySharedAdmissionRequest request,
        CancellationToken cancellationToken)
    {
        int active = Interlocked.Increment(ref _active);
        int observed;
        while (active > (observed = Volatile.Read(ref _maximum)) &&
               Interlocked.CompareExchange(ref _maximum, active, observed) != observed) { }
        try { return await _completion.Task.ConfigureAwait(false); }
        finally { Interlocked.Decrement(ref _active); }
    }

    internal void Release() => _completion.TrySetResult(new GatewaySharedAdmissionDecision(
        GatewaySharedAdmissionDecisionKind.IndeterminateAfterPossibleCommit,
        null, null, null, null, "released"));
}

sealed record WorkerOptions(
    int Port, int BackendPort, string ReplicaId, string RedisConfiguration, string AuthorityId,
    string KeyPrefix, string Algorithm, long Limit, long PeriodMilliseconds, int Segments,
    long TokensPerPeriod, int LocalConcurrency, int OperationTimeoutMilliseconds,
    int MaximumProviderConcurrency, string FailureDisposition, ulong Generation)
{
    public static WorkerOptions Parse(string[] args) => new(
        Arguments.Value(args, "--port", int.Parse), Arguments.Value(args, "--backend-port", int.Parse), Arguments.Value(args, "--replica"),
        Arguments.Value(args, "--redis"), Arguments.Value(args, "--authority"), Arguments.Value(args, "--key-prefix"),
        Arguments.Value(args, "--algorithm"), Arguments.Value(args, "--limit", long.Parse),
        Arguments.Value(args, "--period-ms", long.Parse), Arguments.Value(args, "--segments", int.Parse),
        Arguments.Value(args, "--tokens", long.Parse), Arguments.Value(args, "--local-concurrency", int.Parse),
        Arguments.Value(args, "--operation-timeout-ms", int.Parse), Arguments.Value(args, "--provider-capacity", int.Parse),
        Arguments.Value(args, "--failure"),
        Arguments.Value(args, "--generation", ulong.Parse));
}

sealed record ControllerOptions(
    string Assembly, string RedisConfiguration, int BasePort, int Replicas, string Distribution,
    string Scenario, string AuthorityId, string KeyPrefix, string Algorithm, int Limit,
    string FailureDisposition, string? ControlDirectory)
{
    public static ControllerOptions Parse(string[] args) => new(
        Arguments.Value(args, "--assembly"), Arguments.Value(args, "--redis"), Arguments.Value(args, "--base-port", int.Parse),
        Arguments.Value(args, "--replicas", int.Parse), Arguments.Value(args, "--distribution"), Arguments.Value(args, "--scenario"), Arguments.Value(args, "--authority"),
        Arguments.Value(args, "--key-prefix"), Arguments.Value(args, "--algorithm"), Arguments.Value(args, "--limit", int.Parse),
        Arguments.Optional(args, "--failure") ?? "reject",
        Arguments.Optional(args, "--control-dir"));
}

sealed class WorkerProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task _stdout;
    private readonly Task _stderr;
    private bool _disposed;
    public int Port { get; }

    private WorkerProcess(Process process, int port)
    {
        _process = process;
        Port = port;
        _stdout = DrainAsync(process.StandardOutput);
        _stderr = DrainAsync(process.StandardError);
    }

    public static WorkerProcess Start(ControllerOptions options, int index, ulong generation = 1)
    {
        int port = options.BasePort + index * 2;
        int backendPort = port + 1;
        bool managedAssembly = options.Assembly.EndsWith(".dll", StringComparison.Ordinal);
        var start = new ProcessStartInfo(managedAssembly ? "dotnet" : options.Assembly)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (managedAssembly) start.ArgumentList.Add(options.Assembly);
        foreach (string argument in new[]
        {
            "worker", "--port", port.ToString(), "--backend-port", backendPort.ToString(),
            "--replica", $"replica-{index}", "--redis", options.RedisConfiguration,
            "--authority", options.AuthorityId, "--key-prefix", options.KeyPrefix,
            "--algorithm", options.Algorithm, "--limit", options.Limit.ToString(), "--period-ms", "60000",
            "--segments", "4", "--tokens", "1", "--local-concurrency", options.Scenario == "concurrency" ? "2" : "64",
            "--operation-timeout-ms", "1000", "--provider-capacity", "1024",
            "--failure", options.FailureDisposition,
            "--generation", generation.ToString()
        }) start.ArgumentList.Add(argument);
        Process process = Process.Start(start) ?? throw new InvalidOperationException("Worker process could not start.");
        return new WorkerProcess(process, port);
    }

    public async Task WaitUntilReadyAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
        long deadline = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (_process.HasExited) throw new InvalidOperationException($"Worker exited with code {_process.ExitCode}.");
            try
            {
                using HttpResponseMessage response = await client.GetAsync($"http://127.0.0.1:{Port}/worker/live");
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(50);
        }
        throw new TimeoutException("Worker did not become ready.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }
        await Task.WhenAll(_stdout, _stderr);
        _process.Dispose();
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is not null) { }
    }
}

static class Arguments
{
    public static string Value(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException($"Missing {name}.");
        return args[index + 1];
    }

    public static T Value<T>(string[] args, string name, Func<string, T> parse) => parse(Value(args, name));

    public static string? Optional(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index < 0 ? null : index + 1 < args.Length ? args[index + 1] : throw new ArgumentException($"Missing {name} value.");
    }
}
