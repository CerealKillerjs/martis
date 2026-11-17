namespace Martis.Audio.Tests;

public sealed class AudioSettingsTests
{
    [Fact]
    public void DefaultsLeaveRoomForEffects()
    {
        var settings = new AudioSettings();
        Assert.Equal(0.9f, settings.Master);
        Assert.Equal(0.7f, settings.Music);
        Assert.Equal(0.8f, settings.Effects);
        Assert.Equal(0.6f, settings.Ambience);
        Assert.False(settings.Muted);
    }

    [Theory]
    [InlineData(1f, 0f)]
    [InlineData(0.5f, -6.0206f)]
    [InlineData(0f, -80f)]
    [InlineData(-1f, -80f)]
    [InlineData(0.00001f, -80f)]
    public void LinearVolumesBecomeDecibels(float linear, float db)
    {
        Assert.Equal(db, AudioSettings.ToDecibels(linear), 3);
    }

    [Fact]
    public void ValuesAreClamped()
    {
        var settings = new AudioSettings { Master = 3f, Music = -1f, Effects = float.NaN };
        Assert.Equal(1f, settings.Master);
        Assert.Equal(0f, settings.Music);
        Assert.Equal(0.8f, settings.Effects);
    }

    [Fact]
    public void SettingsRoundTripThroughText()
    {
        var settings = new AudioSettings { Master = 0.25f, Music = 0.5f, Effects = 0.75f, Ambience = 0.1f, Muted = true };
        var parsed = AudioSettings.Parse(settings.Serialize());
        Assert.Equal(settings, parsed);
    }

    [Fact]
    public void ParsingIgnoresGarbageAndKeepsDefaults()
    {
        var parsed = AudioSettings.Parse("master=0.3\nmusic=loud\n# comment\nunknown=1\neffects\n=4\nmuted=yes\nambience=0.5e0");
        Assert.Equal(0.3f, parsed.Master);
        Assert.Equal(0.7f, parsed.Music);
        Assert.Equal(0.8f, parsed.Effects);
        Assert.Equal(0.5f, parsed.Ambience);
        Assert.False(parsed.Muted);
        Assert.Equal(new AudioSettings(), AudioSettings.Parse(string.Empty));
    }

    [Fact]
    public void TheBusVolumeIncludesTheMuteSwitch()
    {
        var settings = new AudioSettings { Master = 0.5f };
        Assert.Equal(AudioSettings.ToDecibels(0.5f), settings.BusDecibels(AudioBus.Master), 4);
        Assert.Equal(AudioSettings.ToDecibels(0.7f), settings.BusDecibels(AudioBus.Music), 4);
        settings.Muted = true;
        Assert.Equal(-80f, settings.BusDecibels(AudioBus.Master));
        Assert.Equal(AudioSettings.ToDecibels(0.8f), settings.BusDecibels(AudioBus.Effects), 4);
    }
}
