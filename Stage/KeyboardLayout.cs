namespace PianoPath;

/// <summary>
/// Where every key of a piano keyboard sits, as shares of the keyboard's width (0 to 1): the one description of
/// the keyboard's geometry that the stage, the GPU engine, the shaded keys and — later — the AR overlay can agree on.
///
/// <para>
/// The geometry is the real one. White keys are equally wide and tile the whole width; a black key is
/// <see cref="BlackWidthShare"/> of a white key wide and sits on the boundary between its two white neighbours,
/// nudged the way a real keyboard nudges it (C♯ and F♯ toward the left, D♯ and A♯ toward the right, G♯ in the
/// middle), so a falling note, a spark and a lit key all land on the same pixels. The numbers are exactly the ones
/// the stage has always drawn with; the layout only gives them one home and makes the key range a value instead of
/// the constants 21 and 88, so a 61-key controller can have a keyboard of its own.
/// </para>
///
/// <para>
/// Pure arithmetic with no WPF in it, so <c>tests/PianoPath.Tests</c> compiles this very file and checks the
/// geometry on any machine.
/// </para>
/// </summary>
internal sealed class KeyboardLayout
{
    /// <summary>A black key's width as a share of a white key's; the shaded keyboard and the GPU keys use the same share.</summary>
    internal const double BlackWidthShare = .52;

    /// <summary>The full keyboard, A0 to C8: the layout the stage has always had and the default of a fresh install.</summary>
    internal static readonly KeyboardLayout Standard88 = new(21, 88);

    /// <summary>Every keyboard size the app offers, widest first. The first pitch is the lowest key of the usual instrument of that size.</summary>
    internal static readonly KeyboardLayout[] All =
    [
        Standard88,
        new KeyboardLayout(28, 76), // E1 to G7
        new KeyboardLayout(36, 61), // C2 to C7
        new KeyboardLayout(36, 49), // C2 to C6
    ];

    private readonly int[] _whitesBelow = new int[128];
    private readonly double[] _centers = new double[128];
    private readonly int[] _whitePitches;

    private KeyboardLayout(int firstPitch, int keyCount)
    {
        FirstPitch = firstPitch;
        KeyCount = keyCount;
        var whites = new List<int>();
        for (var pitch = 0; pitch < 128; pitch++)
        {
            _whitesBelow[pitch] = whites.Count;
            if (pitch >= firstPitch && pitch < firstPitch + keyCount && !IsBlack(pitch)) whites.Add(pitch);
        }
        _whitePitches = [.. whites];
        var whiteWidth = 1.0 / _whitePitches.Length;
        for (var pitch = 0; pitch < 128; pitch++)
            _centers[pitch] = IsBlack(pitch)
                ? (_whitesBelow[pitch] + BlackKeyOffset(pitch)) * whiteWidth
                : (_whitesBelow[pitch] + .5) * whiteWidth;
    }

    /// <summary>MIDI pitch of the lowest key.</summary>
    internal int FirstPitch { get; }

    /// <summary>How many keys, black and white, the keyboard has.</summary>
    internal int KeyCount { get; }

    /// <summary>MIDI pitch of the highest key.</summary>
    internal int LastPitch => FirstPitch + KeyCount - 1;

    /// <summary>How many white keys the keyboard has; they are what the width is divided by.</summary>
    internal int WhiteCount => _whitePitches.Length;

    /// <summary>A white key's width as a share of the keyboard's.</summary>
    internal double WhiteWidth => 1.0 / _whitePitches.Length;

    /// <summary>The caption the size picker shows for this layout.</summary>
    internal string Name => $"{KeyCount} keys";

    /// <summary>True when the pitch is a key of this keyboard.</summary>
    internal bool Contains(int pitch) => pitch >= FirstPitch && pitch <= LastPitch;

    /// <summary>True for the five black keys of every octave.</summary>
    internal static bool IsBlack(int pitch) => pitch % 12 is 1 or 3 or 6 or 8 or 10;

    /// <summary>How far a black key sits from the boundary of its two white neighbours, in white-key widths.</summary>
    internal static double BlackKeyOffset(int pitch) => (pitch % 12) switch
    {
        1 => -0.08,
        3 => 0.08,
        6 => -0.10,
        8 => 0.00,
        10 => 0.10,
        _ => 0.0
    };

    /// <summary>MIDI pitch of the white key at an index, 0 being the lowest.</summary>
    internal int WhitePitchAt(int index) => _whitePitches[Math.Clamp(index, 0, _whitePitches.Length - 1)];

    /// <summary>How many white keys of this keyboard lie below a pitch; for a pitch past the end it is all of them.</summary>
    internal int WhitesBelow(int pitch) => _whitesBelow[Math.Clamp(pitch, 0, 127)];

    /// <summary>
    /// The horizontal centre of a key, 0 at the left edge of the keyboard and 1 at the right. A pitch the keyboard
    /// does not have is placed where it would be if the keyboard went on (or at its edge), never out of the table.
    /// </summary>
    internal double CenterX(int pitch) => _centers[Math.Clamp(pitch, 0, 127)];

    /// <summary>The left and right edge of a key at its top: a white key fills its slot, a black key is narrower and centred on its place.</summary>
    internal (double Left, double Right) Span(int pitch)
    {
        var white = WhiteWidth;
        if (!IsBlack(pitch))
        {
            var left = WhitesBelow(pitch) * white;
            return (left, left + white);
        }
        var half = white * BlackWidthShare / 2;
        var centre = CenterX(pitch);
        return (centre - half, centre + half);
    }

    /// <summary>
    /// The key under a horizontal position. <paramref name="inBlackRow"/> says whether the point is in the upper part
    /// of the keys, where the black keys stand: there a black key wins over the white one behind it; below them it
    /// is always a white key. A position off the keyboard is held at its ends.
    /// </summary>
    internal int PitchAt(double x, bool inBlackRow)
    {
        x = Math.Clamp(double.IsFinite(x) ? x : 0, 0, 1);
        var index = Math.Min(_whitePitches.Length - 1, (int)(x * _whitePitches.Length));
        var white = _whitePitches[index];
        if (!inBlackRow) return white;
        for (var neighbour = white - 1; neighbour <= white + 1; neighbour += 2)
        {
            if (!Contains(neighbour) || !IsBlack(neighbour)) continue;
            var (left, right) = Span(neighbour);
            if (x >= left && x < right) return neighbour;
        }
        return white;
    }

    /// <summary>The layout the app offers for a key count, or the full keyboard for a number it does not know.</summary>
    internal static KeyboardLayout ForKeyCount(int keyCount)
    {
        foreach (var layout in All)
            if (layout.KeyCount == keyCount) return layout;
        return Standard88;
    }

    /// <summary>
    /// The smallest offered keyboard that has both the lowest and the highest pitch of what was played, so a
    /// controller can be recognised from its notes. Nothing played (or a range no keyboard holds) gives the full one.
    /// </summary>
    internal static KeyboardLayout Fitting(int lowestPitch, int highestPitch)
    {
        KeyboardLayout best = Standard88;
        foreach (var layout in All)
            if (layout.Contains(lowestPitch) && layout.Contains(highestPitch) && layout.KeyCount < best.KeyCount) best = layout;
        return best;
    }
}
