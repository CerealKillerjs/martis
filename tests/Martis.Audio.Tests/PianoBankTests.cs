namespace Martis.Audio.Tests;

public sealed class PianoBankTests
{
    [Fact]
    public void TheBankCoversTheKeyboardTheMusicUsesInTwoLayers()
    {
        // A real grand piano, not a synthesised one (Daniel, 2026-11-17).
        var bank = PianoBank.Instance;
        Assert.Equal(44100, bank.SampleRate);
        Assert.Equal(2, bank.Layers.Count);
        Assert.True(bank.Notes[0] <= 34 && bank.Notes[^1] >= 96);
        for (var midi = 34; midi <= 96; midi++)
        {
            Assert.InRange(Math.Abs(bank.Nearest(midi) - midi), 0, 1);
        }
    }

    [Theory]
    [InlineData(48)]
    [InlineData(60)]
    [InlineData(72)]
    public void EachSampleSoundsAtItsPitch(int midi)
    {
        // Autocorrelation of 0.2 s after the attack: the strongest period is the fundamental's.
        var pcm = PianoBank.Instance.Sample(midi, PianoBank.Instance.Layers[1]);
        var start = 44100 / 2;
        var expected = 44100 / Pitch.Hertz(midi);
        var best = 0;
        var bestScore = double.MinValue;
        for (var lag = (int)(expected * 0.8); lag <= (int)(expected * 1.2); lag++)
        {
            var score = 0.0;
            for (var i = start; i < start + 8820; i++)
            {
                score += pcm[i] * (double)pcm[i + lag];
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = lag;
            }
        }

        Assert.InRange(best, expected * 0.98, expected * 1.02);
    }

    [Fact]
    public void SamplesStartWithTheHammerAndFadeToSilence()
    {
        foreach (var midi in PianoBank.Instance.Notes)
        {
            foreach (var layer in PianoBank.Instance.Layers)
            {
                var pcm = PianoBank.Instance.Sample(midi, layer);
                var attack = pcm.AsSpan(0, 4410).ToArray().Max(s => Math.Abs((int)s));
                Assert.True(attack > 3000, $"{midi}/{layer} starts quietly ({attack}).");
                Assert.InRange(Math.Abs((int)pcm[^1]), 0, 200);
            }
        }
    }

    [Fact]
    public void ReadRejectsAnythingThatIsNotABank()
    {
        using var stream = new MemoryStream("RIFF0000"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => PianoBank.Read(stream));
    }
}
