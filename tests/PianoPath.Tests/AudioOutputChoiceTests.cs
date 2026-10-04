using PianoPath;
using Xunit;

namespace PianoPath.Tests;

public sealed class AudioOutputChoiceTests
{
    [Fact]
    public void Windows_endpoint_ids_round_trip_without_losing_path_characters()
    {
        const string endpointId = @"\\?\SWD#MMDEVAPI#{0.0.0.00000000}.{d1c3-voice-meeter}:render";

        var shared = AudioOutputChoice.ForWasapiEndpoint(endpointId);
        var exclusive = AudioOutputChoice.ForWasapiEndpoint(endpointId, exclusive: true);

        Assert.Equal(AudioBackendKind.WasapiShared, AudioOutputChoice.Parse(shared.Id).Kind);
        Assert.Equal(endpointId, AudioOutputChoice.Parse(shared.Id).EndpointId);
        Assert.Equal(shared.Id, AudioOutputChoice.Parse(shared.Id).Id);
        Assert.Equal(AudioBackendKind.WasapiExclusive, AudioOutputChoice.Parse(exclusive.Id).Kind);
        Assert.Equal(endpointId, AudioOutputChoice.Parse(exclusive.Id).EndpointId);
        Assert.Equal(exclusive.Id, AudioOutputChoice.Parse(exclusive.Id).Id);
    }

    [Fact]
    public void Existing_default_and_ASIO_ids_remain_compatible()
    {
        Assert.Equal(AudioOutputChoice.WasapiSharedId, AudioOutputChoice.Parse("wasapi").Id);
        Assert.Equal(AudioOutputChoice.WasapiExclusiveId, AudioOutputChoice.Parse("wasapi-exclusive").Id);
        Assert.Equal("asio:Voicemeeter Virtual ASIO", AudioOutputChoice.ForAsio("Voicemeeter Virtual ASIO").Id);
        Assert.Equal(AudioOutputChoice.AutoId, AudioOutputChoice.Parse("unknown-mode").Id);
    }
}
