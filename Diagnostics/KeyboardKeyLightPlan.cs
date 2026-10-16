namespace PianoPath;

/// <summary>Reference contract for the GPU lamp's low-end response, not a second renderer.</summary>
internal static class KeyboardKeyLightPlan
{
    internal static readonly int[] Levels=[0,1,10,25,50,70,92,100,200];
    internal static double ExpectedGain(double percent)
    {
        if(!double.IsFinite(percent))return 0;
        var t=Math.Clamp(percent/100,0,2);
        return t<.25?t*(4.05-5.6*t):.35+1.25*t;
    }
}
