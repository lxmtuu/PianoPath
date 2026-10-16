using Xunit;
namespace PianoPath.Tests;
public sealed class KeyboardKeyLightTests
{
    [Fact] public void ZeroHasNoHiddenFloorAndEveryValidIncrementAddsLight()
    {
        Assert.Equal(0,KeyboardKeyLightPlan.ExpectedGain(0));
        double last=0;
        for(var n=1;n<=20000;n++)
        {var value=KeyboardKeyLightPlan.ExpectedGain(n/100.0);Assert.True(value>last);last=value;}
        Assert.Equal(2.85,last,10);
    }
    [Theory] [InlineData(25)] [InlineData(50)] [InlineData(70)] [InlineData(92)] [InlineData(100)] [InlineData(200)]
    public void ExistingUsualIlluminationIsPreserved(double percent)
        =>Assert.Equal(.35+1.25*(percent/100),KeyboardKeyLightPlan.ExpectedGain(percent),12);
    [Fact] public void KneeIsContinuousInValueAndSlope()
    {
        const double h=.00001;var a=KeyboardKeyLightPlan.ExpectedGain(25);
        Assert.InRange(a-KeyboardKeyLightPlan.ExpectedGain(25-h),0,.000001);
        Assert.InRange(KeyboardKeyLightPlan.ExpectedGain(25+h)-a,0,.000001);
        var left=(a-KeyboardKeyLightPlan.ExpectedGain(25-h))/h;
        var right=(KeyboardKeyLightPlan.ExpectedGain(25+h)-a)/h;
        Assert.Equal(left,right,7);
    }
    [Fact] public void InvalidInputsCannotCreateNegativeOrUnboundedLampEnergy()
    {
        foreach(var p in new[]{double.NaN,double.NegativeInfinity,double.PositiveInfinity,-100})Assert.Equal(0,KeyboardKeyLightPlan.ExpectedGain(p));
        Assert.Equal(2.85,KeyboardKeyLightPlan.ExpectedGain(1000),10);
    }
}
