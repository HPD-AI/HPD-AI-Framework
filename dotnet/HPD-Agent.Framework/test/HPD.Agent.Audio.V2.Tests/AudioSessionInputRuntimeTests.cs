using HPD.Agent.Audio.AgentIntegration.Middleware;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class AudioSessionInputRuntimeTests
{
    [Fact]
    public async Task AgentRunAsync_DelegatesAudioSessionCommandToConfiguredAuthority()
    {
        var authority = new RecordingSessionAuthority();
        await using var agent = await AgentBuilder.Create()
            .WithAudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
            {
                SessionControlAuthority = authority
            })
            .BuildAsync();

        await agent.StartAsync();
        var input = new AudioSessionInputEvent { Command = new AudioSessionCommand.Start() };

        var result = await agent.RunAsync(input);

        Assert.Equal(input.Command, authority.Seen?.Command);
        var audioResult = Assert.IsType<AgentInputResult.AudioSession>(result);
        Assert.Equal(new AudioSessionInputResult.Started("audio-1", 1), audioResult.Result);
    }

    [Fact]
    public async Task AgentStartAsync_ResolvesAudioSessionAuthorityFromServices()
    {
        var authority = new RecordingSessionAuthority();
        var services = new ServiceCollection()
            .AddSingleton<IAudioSessionControlAuthorityV1>(authority)
            .BuildServiceProvider();
        await using var agent = await AgentBuilder.Create()
            .WithServiceProvider(services)
            .WithAudioRuntimeAttachment()
            .BuildAsync();

        await agent.StartAsync();
        var input = new AudioSessionInputEvent
        {
            Command = new AudioSessionCommand.SetInputEnabled("audio-1", false)
        };

        var result = await agent.RunAsync(input);

        Assert.Equal(input.Command, authority.Seen?.Command);
        var audioResult = Assert.IsType<AgentInputResult.AudioSession>(result);
        Assert.Equal(new AudioSessionInputResult.Started("audio-1", 1), audioResult.Result);
    }

    private sealed class RecordingSessionAuthority : IAudioSessionControlAuthorityV1
    {
        public AudioSessionInputEvent? Seen { get; private set; }

        public ValueTask<AudioSessionInputResult> ExecuteAsync(
            AudioSessionInputEvent input,
            AgentClientSet? clientSet,
            CancellationToken cancellationToken = default)
        {
            Seen = input;
            return ValueTask.FromResult<AudioSessionInputResult>(
                new AudioSessionInputResult.Started("audio-1", 1));
        }
    }
}
