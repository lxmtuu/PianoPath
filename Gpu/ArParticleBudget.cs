namespace PianoPath;

/// <summary>Review-only selection over already projectable particles. Never births, shades or moves a particle.</summary>
internal static class ArParticleBudget
{
    internal readonly record struct Candidate(int Pitch,byte Origin);

    /// <summary>Equal round-robin opportunities for active origins and pitches, borrowing unused shares.
    /// Returned indices retain the input's legacy draw order. Under cap is the exact identity selection.</summary>
    internal static int[] Select(IReadOnlyList<Candidate> eligible,int cap)
    {
        ArgumentNullException.ThrowIfNull(eligible);
        if(cap<0)throw new ArgumentOutOfRangeException(nameof(cap));
        for(var i=0;i<eligible.Count;i++)
            if(eligible[i].Pitch is <0 or >127 || eligible[i].Origin>2)throw new ArgumentException("Invalid particle role/pitch.",nameof(eligible));
        if(eligible.Count<=cap)return Enumerable.Range(0,eligible.Count).ToArray();
        if(cap==0)return [];
        var groups=new Queue<int>?[3*128];
        for(var i=0;i<eligible.Count;i++)
        {
            var p=eligible[i];var bucket=p.Origin*128+p.Pitch;
            (groups[bucket]??=new Queue<int>()).Enqueue(i);
        }
        var selected=new List<int>(Math.Min(cap,eligible.Count));var nextPitch=new int[3];
        while(selected.Count<cap)
        {
            var added=false;
            for(var role=0;role<3 && selected.Count<cap;role++)
                for(var scan=0;scan<128;scan++)
                {
                    var pitch=(nextPitch[role]+scan)%128;var q=groups[role*128+pitch];
                    if(q is not {Count:>0})continue;
                    selected.Add(q.Dequeue());nextPitch[role]=(pitch+1)%128;added=true;break;
                }
            if(!added)break;
        }
        selected.Sort();return selected.ToArray();
    }
}
