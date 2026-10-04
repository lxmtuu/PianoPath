using System.Text.Json;

namespace PianoPath;

/// <summary>Why four clicked points do not make a key area the overlay can work with; <see cref="None"/> when they do.</summary>
internal enum CalibrationProblem
{
    None,
    /// <summary>Fewer or more than four points.</summary>
    NotFourPoints,
    /// <summary>A coordinate is not a number, or the picture has no size.</summary>
    NotFinite,
    /// <summary>A point lies far outside the picture.</summary>
    OutsideImage,
    /// <summary>The points go round the wrong way (or the picture was mirrored): they should run back-left, back-right, front-right, front-left.</summary>
    WrongOrder,
    /// <summary>The outline folds over itself, or a corner is a reflex angle.</summary>
    NotConvex,
    /// <summary>Three points on one line, or two on top of each other.</summary>
    Degenerate,
    /// <summary>The area is a sliver of the picture, or too short to be a keyboard.</summary>
    TooSmall,
    /// <summary>The keys would be a hair's breadth deep.</summary>
    TooThin,
    /// <summary>One long edge is many times longer than the other: not a keyboard seen by a camera.</summary>
    TooSkewed,
}

/// <summary>
/// Judging and tidying the four corners a person clicks on the camera picture. Pure arithmetic, so the wizard's
/// refusals are stated by tests instead of discovered on a camera.
///
/// <para>
/// The corners are the key area's: back-left, back-right, front-right, front-left, where the back is the end of the
/// keys the black keys stand at and the front is the end nearest the player. In picture coordinates (y grows
/// downward) that is clockwise for a camera above the keys looking down.
/// </para>
/// </summary>
internal static class CalibrationQuad
{
    /// <summary>The smallest share of the picture's area the key area may cover (a real one covers a fifth or more).</summary>
    internal const double MinAreaShare = 0.01;

    /// <summary>The key area's longer end edge must reach at least this share of the picture's width.</summary>
    internal const double MinWidthShare = 0.15;

    /// <summary>Both side edges must reach at least this share of the picture's height, or the keys have no depth.</summary>
    internal const double MinDepthShare = 0.02;

    /// <summary>The longer end edge may be at most this many times the shorter (strong perspective is 2 or 3).</summary>
    internal const double MaxEdgeRatio = 6;

    /// <summary>A point may lie this share of the picture's size beyond its edge: a keyboard can run off the frame.</summary>
    internal const double OutsideShare = 0.25;

    /// <summary>The first problem with these corners in this picture, or <see cref="CalibrationProblem.None"/>.</summary>
    internal static CalibrationProblem Check(IReadOnlyList<(double X, double Y)> corners, int imageWidth, int imageHeight)
    {
        if (corners.Count != 4) return CalibrationProblem.NotFourPoints;
        if (imageWidth <= 0 || imageHeight <= 0) return CalibrationProblem.NotFinite;
        foreach (var (x, y) in corners)
            if (!double.IsFinite(x) || !double.IsFinite(y)) return CalibrationProblem.NotFinite;

        foreach (var (x, y) in corners)
            if (x < -OutsideShare * imageWidth || x > (1 + OutsideShare) * imageWidth ||
                y < -OutsideShare * imageHeight || y > (1 + OutsideShare) * imageHeight)
                return CalibrationProblem.OutsideImage;

        // Every corner must turn the same way. A zero turn is three points on a line (or a repeated point).
        int positive = 0, negative = 0;
        for (var i = 0; i < 4; i++)
        {
            var a = corners[i]; var b = corners[(i + 1) % 4]; var c = corners[(i + 2) % 4];
            double abx = b.X - a.X, aby = b.Y - a.Y, bcx = c.X - b.X, bcy = c.Y - b.Y;
            var cross = abx * bcy - aby * bcx;
            var scale = Math.Sqrt(abx * abx + aby * aby) * Math.Sqrt(bcx * bcx + bcy * bcy);
            if (!(Math.Abs(cross) > 1e-6 * scale)) return CalibrationProblem.Degenerate;
            if (cross > 0) positive++; else negative++;
        }
        if (positive == 0) return CalibrationProblem.WrongOrder;   // all four turn the other way: the same shape, named backwards
        if (negative > 0) return CalibrationProblem.NotConvex;

        var area = Area(corners);
        if (area < MinAreaShare * imageWidth * imageHeight) return CalibrationProblem.TooSmall;
        var back = Distance(corners[0], corners[1]); var front = Distance(corners[3], corners[2]);
        var left = Distance(corners[0], corners[3]); var right = Distance(corners[1], corners[2]);
        if (Math.Max(back, front) < MinWidthShare * imageWidth) return CalibrationProblem.TooSmall;
        if (Math.Min(left, right) < MinDepthShare * imageHeight) return CalibrationProblem.TooThin;
        if (Math.Max(back, front) > MaxEdgeRatio * Math.Min(back, front)) return CalibrationProblem.TooSkewed;
        if (Math.Max(left, right) > MaxEdgeRatio * Math.Min(left, right)) return CalibrationProblem.TooSkewed;
        return CalibrationProblem.None;
    }

    /// <summary>
    /// The four points put into the order the overlay wants — back-left, back-right, front-right, front-left —
    /// judged by where they are in the picture (top-left is the one with the smallest x + y, and so on). Null when
    /// that does not name four different points, as when the keyboard is turned a quarter round.
    /// </summary>
    internal static (double X, double Y)[]? Order(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count != 4) return null;
        int topLeft = 0, bottomRight = 0, topRight = 0, bottomLeft = 0;
        for (var i = 0; i < 4; i++)
        {
            if (!double.IsFinite(points[i].X) || !double.IsFinite(points[i].Y)) return null;
            if (points[i].X + points[i].Y < points[topLeft].X + points[topLeft].Y) topLeft = i;
            if (points[i].X + points[i].Y > points[bottomRight].X + points[bottomRight].Y) bottomRight = i;
            if (points[i].X - points[i].Y > points[topRight].X - points[topRight].Y) topRight = i;
            if (points[i].X - points[i].Y < points[bottomLeft].X - points[bottomLeft].Y) bottomLeft = i;
        }
        if (new HashSet<int> { topLeft, topRight, bottomRight, bottomLeft }.Count != 4) return null;
        return [points[topLeft], points[topRight], points[bottomRight], points[bottomLeft]];
    }

    /// <summary>The polygon's area by the shoelace rule, positive when the points run clockwise in picture coordinates.</summary>
    internal static double Area(IReadOnlyList<(double X, double Y)> polygon)
    {
        double sum = 0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum / 2;
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}

/// <summary>
/// A keyboard found in a camera picture: the key area's four corners, the map between keyboard coordinates and
/// picture pixels that they define, and everything the overlay needs to know about keys through it — where a key's
/// top is, the polygon it covers, which key is under a pixel.
///
/// <para>
/// Keyboard coordinates: <c>u</c> runs across the keys 0..1 over the layout's key area, <c>v</c> from the back of the
/// keys 0 to their front 1 — the same ones <c>tools/make_ar_sample.py</c> draws with.
/// </para>
/// </summary>
internal sealed class ArCalibration
{
    /// <summary>How far down the key area the black keys reach: 95 mm of a 150 mm white key.</summary>
    internal const double BlackLengthShare = 95.0 / 150.0;

    private ArCalibration(KeyboardLayout layout, (double X, double Y)[] corners, int width, int height, Homography toImage, Homography toKeyboard)
    {
        Layout = layout; Corners = corners; ImageWidth = width; ImageHeight = height; ToImage = toImage; ToKeyboard = toKeyboard;
    }

    internal KeyboardLayout Layout { get; }

    /// <summary>Back-left, back-right, front-right, front-left, in picture pixels.</summary>
    internal (double X, double Y)[] Corners { get; }

    internal int ImageWidth { get; }

    internal int ImageHeight { get; }

    /// <summary>Keyboard coordinates to picture pixels.</summary>
    internal Homography ToImage { get; }

    /// <summary>Picture pixels to keyboard coordinates.</summary>
    internal Homography ToKeyboard { get; }

    /// <summary>
    /// Builds the calibration for <paramref name="layout"/> from four corners, or null with the reason in
    /// <paramref name="problem"/>: the corners may not be used when <see cref="CalibrationQuad.Check"/> finds
    /// anything wrong or when they define no usable map.
    /// </summary>
    internal static ArCalibration? Create(KeyboardLayout layout, IReadOnlyList<(double X, double Y)> corners, int imageWidth, int imageHeight, out CalibrationProblem problem)
    {
        problem = CalibrationQuad.Check(corners, imageWidth, imageHeight);
        if (problem != CalibrationProblem.None) return null;
        var copy = corners.ToArray();
        var toImage = Homography.FromPoints([(0, 0), (1, 0), (1, 1), (0, 1)], copy);
        var toKeyboard = toImage?.Inverse();
        if (toImage is null || toKeyboard is null) { problem = CalibrationProblem.Degenerate; return null; }
        return new ArCalibration(layout, copy, imageWidth, imageHeight, toImage.Value, toKeyboard.Value);
    }

    /// <summary>The picture pixel of a point of the keyboard.</summary>
    internal (double X, double Y) ToPicture(double u, double v) => ToImage.Apply(u, v);

    /// <summary>The keyboard coordinates of a picture pixel; NaN for a pixel on the horizon of the map.</summary>
    internal (double U, double V) ToKeys(double x, double y) => ToKeyboard.Apply(x, y);

    /// <summary>
    /// Where the middle of a key's top edge is in the picture: a quarter of the way down a black key, four fifths down
    /// a white one (<c>tools/make_ar_sample.py</c> takes the same points).
    /// </summary>
    internal (double X, double Y) KeyTopCentre(int pitch) =>
        ToPicture(Layout.CenterX(pitch), KeyboardLayout.IsBlack(pitch) ? 0.25 : 0.8);

    /// <summary>
    /// The picture polygon a key covers, as four corners (back-left, back-right, front-right, front-left): a white
    /// key over the whole depth of the keys, a black one over the back <see cref="BlackLengthShare"/> of it. A black
    /// key is drawn over the white ones, which is why a white polygon is the whole key and not the part left visible.
    /// </summary>
    internal (double X, double Y)[] KeyPolygon(int pitch)
    {
        double left, right, depth;
        if (KeyboardLayout.IsBlack(pitch)) { (left, right) = Layout.Span(pitch); depth = BlackLengthShare; }
        else { var centre = Layout.CenterX(pitch); left = centre - Layout.WhiteWidth / 2; right = centre + Layout.WhiteWidth / 2; depth = 1; }
        return [ToPicture(left, 0), ToPicture(right, 0), ToPicture(right, depth), ToPicture(left, depth)];
    }

    /// <summary>True when the pixel is inside the key area.</summary>
    internal bool Contains(double x, double y)
    {
        var (u, v) = ToKeys(x, y);
        return u >= 0 && u <= 1 && v >= 0 && v <= 1;
    }

    /// <summary>The key under a picture pixel, or null when the pixel is outside the key area.</summary>
    internal int? PitchAt(double x, double y)
    {
        var (u, v) = ToKeys(x, y);
        if (!(u >= 0 && u <= 1 && v >= 0 && v <= 1)) return null;   // also false for NaN
        return Layout.PitchAt(u, inBlackRow: v < BlackLengthShare);
    }
}

/// <summary>
/// The model behind the four-point calibration wizard: the points placed so far, the one picked up, and whether
/// they are good enough to finish. The window draws it and feeds it clicks, drags and arrow keys; every decision —
/// which point a click picks, where a point may go, when Done is allowed — is made here.
/// </summary>
internal sealed class CalibrationEditor
{
    private readonly List<(double X, double Y)> _points = [];

    internal CalibrationEditor(int imageWidth, int imageHeight)
    {
        ImageWidth = imageWidth; ImageHeight = imageHeight;
    }

    internal int ImageWidth { get; }

    internal int ImageHeight { get; }

    /// <summary>The points placed so far, in the order they were placed.</summary>
    internal IReadOnlyList<(double X, double Y)> Points => _points;

    internal bool IsComplete => _points.Count == 4;

    /// <summary>The point being moved by the arrow keys, or -1.</summary>
    internal int Selected { get; private set; } = -1;

    /// <summary>The names of the corners in the order they are asked for.</summary>
    internal static readonly string[] CornerNames = ["back-left", "back-right", "front-right", "front-left"];

    /// <summary>
    /// What is wrong with the points as they stand. <see cref="CalibrationProblem.NotFourPoints"/> until all four are
    /// placed; after that the verdict of <see cref="CalibrationQuad.Check"/>, so a drag that makes the outline fold
    /// shows it at once, while the person is still dragging.
    /// </summary>
    internal CalibrationProblem Problem => CalibrationQuad.Check(_points, ImageWidth, ImageHeight);

    /// <summary>
    /// The points as the overlay can use them: as they stand when they pass <see cref="CalibrationQuad.Check"/>,
    /// otherwise put in order by <see cref="CalibrationQuad.Order"/> when that makes them pass (clicked starting at the
    /// wrong corner, or round the wrong way); null when neither does.
    /// </summary>
    private (double X, double Y)[]? Usable()
    {
        var points = _points.ToArray();
        if (CalibrationQuad.Check(points, ImageWidth, ImageHeight) == CalibrationProblem.None) return points;
        return CalibrationQuad.Order(points) is { } ordered && CalibrationQuad.Check(ordered, ImageWidth, ImageHeight) == CalibrationProblem.None ? ordered : null;
    }

    /// <summary>Done is allowed when the points are fine, or only need putting in order.</summary>
    internal bool CanFinish => Usable() is not null;

    /// <summary>Places the next point. False when all four are already placed. The point is held inside the allowed margin of the picture.</summary>
    internal bool Place(double x, double y)
    {
        if (IsComplete || !double.IsFinite(x) || !double.IsFinite(y)) return false;
        _points.Add(Hold(x, y));
        Selected = _points.Count - 1;
        return true;
    }

    /// <summary>Moves a placed point to a position; false for an index that is not a placed point or a position that is not a number.</summary>
    internal bool Move(int index, double x, double y)
    {
        if (index < 0 || index >= _points.Count || !double.IsFinite(x) || !double.IsFinite(y)) return false;
        _points[index] = Hold(x, y);
        Selected = index;
        return true;
    }

    /// <summary>Moves a placed point by a number of pixels, as the arrow keys do.</summary>
    internal bool Nudge(int index, double dx, double dy) =>
        index >= 0 && index < _points.Count && Move(index, _points[index].X + dx, _points[index].Y + dy);

    /// <summary>
    /// The placed point nearest to a position and within <paramref name="radius"/> pixels of it, or -1: what a mouse press
    /// picks up. The nearest wins when two are in reach, and the later-placed one when they tie.
    /// </summary>
    internal int HitTest(double x, double y, double radius)
    {
        var best = -1; var bestDistance = double.MaxValue;
        for (var i = 0; i < _points.Count; i++)
        {
            var distance = Math.Sqrt((_points[i].X - x) * (_points[i].X - x) + (_points[i].Y - y) * (_points[i].Y - y));
            if (distance <= radius && distance <= bestDistance) { best = i; bestDistance = distance; }
        }
        return best;
    }

    /// <summary>Takes the last point back; false when there is none.</summary>
    internal bool Undo()
    {
        if (_points.Count == 0) return false;
        _points.RemoveAt(_points.Count - 1);
        Selected = _points.Count - 1;
        return true;
    }

    /// <summary>Starts again.</summary>
    internal void Clear() { _points.Clear(); Selected = -1; }

    /// <summary>
    /// Finishes: puts the points in order if they only need that, and builds the calibration for
    /// <paramref name="layout"/>. Null — with the reason — when the points are not good enough; the editor is not
    /// changed then.
    /// </summary>
    internal ArCalibration? Finish(KeyboardLayout layout, out CalibrationProblem problem)
    {
        var points = Usable() ?? _points.ToArray();
        var result = ArCalibration.Create(layout, points, ImageWidth, ImageHeight, out problem);
        if (result is not null) { _points.Clear(); _points.AddRange(points); }
        return result;
    }

    private (double X, double Y) Hold(double x, double y) => (
        Math.Clamp(x, -CalibrationQuad.OutsideShare * ImageWidth, (1 + CalibrationQuad.OutsideShare) * ImageWidth),
        Math.Clamp(y, -CalibrationQuad.OutsideShare * ImageHeight, (1 + CalibrationQuad.OutsideShare) * ImageHeight));
}

/// <summary>
/// The calibrations kept on this computer, one file in the settings folder. Each is for one camera at one picture
/// size and one keyboard size — moving the camera, changing its resolution or the keyboard size means the old four
/// corners no longer mean anything — and a settings preset or a share code never carries one.
///
/// <para>
/// A file that cannot be read, or an entry that would not pass <see cref="CalibrationQuad.Check"/>, is dropped
/// instead of trusted: the worst a damaged file can do is ask for a new calibration. Saving writes a temporary file
/// and moves it into place, so a crash cannot leave half a file.
/// </para>
/// </summary>
internal sealed class ArCalibrationStore
{
    /// <summary>The most calibrations kept; the least recently saved are forgotten beyond it.</summary>
    internal const int MaxEntries = 64;

    internal const string FileName = "ar-calibration.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly Lock _gate = new();
    private readonly string _path;
    private List<Entry> _entries = [];

    internal ArCalibrationStore(string directory)
    {
        _path = Path.Combine(directory, FileName);
        Reload();
    }

    internal string FilePath => _path;

    internal int Count { get { lock (_gate) return _entries.Count; } }

    /// <summary>The corners saved for this camera, picture size and keyboard size, or null when there are none.</summary>
    internal (double X, double Y)[]? Get(string cameraLink, int imageWidth, int imageHeight, int keyCount)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => Matches(e, cameraLink, imageWidth, imageHeight, keyCount));
            return entry is null ? null : Points(entry);
        }
    }

    /// <summary>
    /// True when this camera has a calibration at some other picture size or keyboard size but not at this one, which
    /// is what lets the app say "calibrate again" instead of "never calibrated".
    /// </summary>
    internal bool HasOtherSize(string cameraLink, int imageWidth, int imageHeight, int keyCount)
    {
        lock (_gate)
            return !_entries.Any(e => Matches(e, cameraLink, imageWidth, imageHeight, keyCount)) &&
                   _entries.Any(e => string.Equals(e.CameraLink, cameraLink, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Saves the corners for this camera, picture size and keyboard size, replacing the earlier ones, and writes the file. False when the corners are not good enough or the file cannot be written.</summary>
    internal bool Set(string cameraLink, int imageWidth, int imageHeight, int keyCount, IReadOnlyList<(double X, double Y)> corners)
    {
        if (CalibrationQuad.Check(corners, imageWidth, imageHeight) != CalibrationProblem.None) return false;
        lock (_gate)
        {
            _entries.RemoveAll(e => Matches(e, cameraLink, imageWidth, imageHeight, keyCount));
            _entries.Add(new Entry
            {
                CameraLink = cameraLink ?? "", ImageWidth = imageWidth, ImageHeight = imageHeight, KeyCount = keyCount,
                Corners = corners.SelectMany(p => new[] { p.X, p.Y }).ToArray(),
            });
            if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
            return Write();
        }
    }

    /// <summary>Forgets the calibration for this camera, picture size and keyboard size ("Reset calibration"); true when there was one.</summary>
    internal bool Remove(string cameraLink, int imageWidth, int imageHeight, int keyCount)
    {
        lock (_gate)
        {
            var removed = _entries.RemoveAll(e => Matches(e, cameraLink, imageWidth, imageHeight, keyCount)) > 0;
            if (removed) Write();
            return removed;
        }
    }

    /// <summary>Reads the file again, dropping whatever cannot be trusted.</summary>
    internal void Reload()
    {
        lock (_gate)
        {
            _entries = [];
            try
            {
                if (!File.Exists(_path)) return;
                var file = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(_path), Options);
                if (file?.Entries is null) return;
                foreach (var entry in file.Entries)
                {
                    if (entry is null || entry.Corners is not { Length: 8 } || entry.ImageWidth <= 0 || entry.ImageHeight <= 0 || entry.KeyCount <= 0) continue;
                    if (CalibrationQuad.Check(Points(entry), entry.ImageWidth, entry.ImageHeight) != CalibrationProblem.None) continue;
                    entry.CameraLink ??= "";
                    _entries.RemoveAll(e => Matches(e, entry.CameraLink, entry.ImageWidth, entry.ImageHeight, entry.KeyCount));
                    _entries.Add(entry);
                }
                if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
            }
            catch { _entries = []; }
        }
    }

    private bool Write()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new FileShape { Version = 1, Entries = _entries }, Options));
            File.Move(temp, _path, true);
            return true;
        }
        catch { return false; }
    }

    private static bool Matches(Entry e, string link, int width, int height, int keys) =>
        string.Equals(e.CameraLink ?? "", link ?? "", StringComparison.OrdinalIgnoreCase) && e.ImageWidth == width && e.ImageHeight == height && e.KeyCount == keys;

    private static (double X, double Y)[] Points(Entry e) =>
        [(e.Corners[0], e.Corners[1]), (e.Corners[2], e.Corners[3]), (e.Corners[4], e.Corners[5]), (e.Corners[6], e.Corners[7])];

    private sealed class FileShape
    {
        public int Version { get; set; }
        public List<Entry>? Entries { get; set; }
    }

    private sealed class Entry
    {
        public string? CameraLink { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public int KeyCount { get; set; }
        /// <summary>x0, y0, x1, y1, x2, y2, x3, y3: back-left, back-right, front-right, front-left.</summary>
        public double[] Corners { get; set; } = [];
    }
}
