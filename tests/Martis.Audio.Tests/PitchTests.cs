namespace Martis.Audio.Tests;

public sealed class PitchTests
{
    [Theory]
    [InlineData(69, 440.0)]
    [InlineData(81, 880.0)]
    [InlineData(57, 220.0)]
    [InlineData(60, 261.6256)]
    public void MidiNotesUseEqualTemperamentOnA440(int midi, double hertz)
    {
        Assert.Equal(hertz, Pitch.Hertz(midi), 3);
    }

    [Fact]
    public void ScaleDegreesWrapIntoHigherOctaves()
    {
        // Dorian on D (62): degrees 0..7 → D E F G A B C D'.
        int[] expected = [62, 64, 65, 67, 69, 71, 72, 74];
        for (var degree = 0; degree < expected.Length; degree++)
        {
            Assert.Equal(expected[degree], Pitch.Degree(62, Mode.Dorian, degree));
        }

        Assert.Equal(60, Pitch.Degree(62, Mode.Dorian, -1));
    }
}
