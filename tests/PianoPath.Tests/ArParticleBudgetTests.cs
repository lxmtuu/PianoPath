using Xunit;

namespace PianoPath.Tests;

public sealed class ArParticleBudgetTests
{
    private static ArParticleBudget.Candidate C(int pitch,byte origin)=>new(pitch,origin);
    [Fact]
    public void UnderCapIsTheExactIdentityAndInvalidInputsAreRejected()
    {
        var a=new[]{C(61,2),C(60,0),C(60,1)};
        Assert.Equal(new[]{0,1,2},ArParticleBudget.Select(a,192));Assert.Empty(ArParticleBudget.Select(a,0));
        Assert.Empty(ArParticleBudget.Select([],192));
        Assert.Throws<ArgumentOutOfRangeException>(()=>ArParticleBudget.Select(a,-1));
        Assert.Throws<ArgumentException>(()=>ArParticleBudget.Select([C(128,0)],1));
        Assert.Throws<ArgumentException>(()=>ArParticleBudget.Select([C(60,3)],1));
    }
    [Fact]
    public void DenseRolesAndPitchesReceiveSharesWithoutRaisingTheCapOrReordering()
    {
        var eligible=Enumerable.Range(0,3).SelectMany(o=>Enumerable.Range(48,8).SelectMany(p=>Enumerable.Repeat(C(p,(byte)o),50))).ToArray();
        var selected=ArParticleBudget.Select(eligible,192);
        Assert.Equal(192,selected.Length);Assert.Equal(selected.Order(),selected);Assert.Equal(192,selected.Distinct().Count());
        foreach(var role in new byte[]{0,1,2})
        {
            Assert.Equal(64,selected.Count(i=>eligible[i].Origin==role));
            foreach(var p in Enumerable.Range(48,8))Assert.Equal(8,selected.Count(i=>eligible[i]==C(p,role)));
        }
        Assert.Equal(selected,ArParticleBudget.Select(eligible,192));
    }
    [Fact]
    public void UnusedSharesAreBorrowedAndExistingBucketOrderIsRetained()
    {
        var a=new[]{C(60,1)}.Concat(Enumerable.Repeat(C(60,0),300)).Concat(Enumerable.Repeat(C(61,2),300)).ToArray();
        var s=ArParticleBudget.Select(a,192);Assert.Equal(192,s.Length);Assert.Contains(0,s);
        Assert.Equal(96,s.Count(i=>a[i].Origin==0));Assert.Equal(95,s.Count(i=>a[i].Origin==2));
        foreach(var group in s.GroupBy(i=>a[i]))
            Assert.Equal(Enumerable.Range(0,a.Length).Where(i=>a[i]==group.Key).Take(group.Count()),group);
    }
    [Fact]
    public void FuzzSelectionIsBoundedStableAndNeverSynthesizesOrMutatesCandidates()
    {
        var random=new Random(20261016);
        for(var run=0;run<150;run++)
        {
            var a=Enumerable.Range(0,random.Next(1500)).Select(_=>C(random.Next(21,109),(byte)random.Next(3))).ToArray();var copy=a.ToArray();
            var cap=random.Next(257);var s=ArParticleBudget.Select(a,cap);
            Assert.Equal(Math.Min(cap,a.Length),s.Length);Assert.Equal(s.Order(),s);Assert.Equal(s.Length,s.Distinct().Count());
            Assert.All(s,i=>Assert.InRange(i,0,a.Length-1));Assert.Equal(copy,a);Assert.Equal(s,ArParticleBudget.Select(a,cap));
        }
    }
}
