using Xunit;

namespace PianoPath.Tests;

public sealed class HeldLightIsolationPlanTests
{
    [Fact]
    public void PlanFullyCrossesThreeFactorsAndKeepsOnlyWispFixtureAmountLow()
    {
        var entries=HeldLightIsolationPlan.Entries.ToArray();
        Assert.Equal(24,entries.Length); Assert.Equal(24,entries.Select(e=>e.Id).Distinct().Count());
        Assert.Equal(192,entries.Length*HeldLightIsolationPlan.Variants.Count);
        Assert.Equal(8,HeldLightIsolationPlan.Variants.Select(v=>(v.Particles,v.Tint,v.Spill)).Distinct().Count());
        Assert.Equal(8,entries.Count(e=>e.Load=="low"));
        Assert.All(entries.Where(e=>e.Load=="low"),e=>{Assert.Equal("wisps",e.Scene.Family);Assert.Equal(8,e.WispAmount);});
        Assert.All(entries.Where(e=>e.Load=="normal"),e=>Assert.Equal(32,e.WispAmount));
        Assert.All(entries,e=>Assert.Equal(new[] {60,61,64,67},e.Scene.Pattern.Pitches));
    }

    [Fact]
    public void SummaryRetainsEmptyPitchesAndPartitionsBoundaryAgesExactly()
    {
        var rows=HeldLightIsolationPlan.Summarize([60,61],[
            new(60,0,1),new(60,.199f,1),new(60,.2f,1),new(60,.399f,1),new(60,.4f,1),new(60,.6f,1)]);
        Assert.Equal(6,rows[0].Count);Assert.Equal(2,rows[0].Young);Assert.Equal(2,rows[0].Middle);Assert.Equal(2,rows[0].Older);
        Assert.Equal(0d,rows[0].MinAge);Assert.InRange(rows[0].MaxAge!.Value,.599,.601);
        Assert.Equal(rows[0].MeanAge,rows[0].MeanProgress);
        Assert.Equal(0,rows[1].Count);Assert.Null(rows[1].MinAge);Assert.Null(rows[1].MaxAge);
        Assert.Null(rows[1].MeanAge);Assert.Null(rows[1].MeanProgress);
    }

    [Fact]
    public void SummaryIsPermutationInvariantAndDoesNotInventCoverage()
    {
        HeldLightIsolationPlan.Sample[] samples=[new(60,.1f,1),new(61,.3f,.8f),new(61,.5f,.8f)];
        Assert.Equal(HeldLightIsolationPlan.Summarize([60,61],samples),HeldLightIsolationPlan.Summarize([60,61],samples.Reverse().ToArray()));
        Assert.Equal(3,HeldLightIsolationPlan.Summarize([60,61],samples).Sum(r=>r.Count));
        Assert.All(HeldLightIsolationPlan.Summarize([60,61],[]),r=>Assert.Equal(0,r.Count));
    }

    [Fact]
    public void MeanProgressIsCanonicalAcrossOppositePackingTraversal()
    {
        var samples=Enumerable.Range(1,150).Select(i=>new HeldLightIsolationPlan.Sample(60,i/300f,1+(i%17)/19f)).ToArray();
        Assert.Equal(HeldLightIsolationPlan.Summarize([60],samples),
            HeldLightIsolationPlan.Summarize([60],samples.Reverse().ToArray()));
    }

    [Theory]
    [InlineData(float.NaN,1f)] [InlineData(-.1f,1f)] [InlineData(1f,1f)]
    [InlineData(0f,0f)] [InlineData(.1f,float.PositiveInfinity)]
    public void InvalidLiveSamplesFail(float age,float life) =>
        Assert.Throws<ArgumentException>(()=>HeldLightIsolationPlan.Summarize([60],[new(60,age,life)]));

    [Fact]
    public void UnknownOrDuplicatePitchDomainsFail()
    {
        Assert.Throws<ArgumentException>(()=>HeldLightIsolationPlan.Summarize([60],[new(61,.1f,1)]));
        Assert.Throws<ArgumentException>(()=>HeldLightIsolationPlan.Summarize([60,60],[]));
    }
}
