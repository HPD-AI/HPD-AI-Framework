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
