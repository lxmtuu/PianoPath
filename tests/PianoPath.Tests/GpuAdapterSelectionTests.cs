using Xunit;

namespace PianoPath.Tests;

public sealed class GpuAdapterSelectionTests
{
    private static readonly string[] HybridLaptop =
    [
        "AMD Radeon(TM) Graphics",
        "NVIDIA GeForce RTX 4050 Laptop GPU",
    ];

    [Fact]
    public void No_request_keeps_the_os_default_adapter()
    {
        Assert.Equal((-1, (string?)null), GpuAdapterSelection.Select(HybridLaptop, null));
        Assert.Equal((-1, (string?)null), GpuAdapterSelection.Select(HybridLaptop, "   "));
    }

    [Fact]
    public void A_unique_substring_selects_exactly_that_adapter_case_insensitively()
    {
        Assert.Equal((1, (string?)null), GpuAdapterSelection.Select(HybridLaptop, "rtx 4050"));
        Assert.Equal((0, (string?)null), GpuAdapterSelection.Select(HybridLaptop, " AMD RADeon "));
    }

    [Fact]
    public void A_request_matching_nothing_is_a_problem_naming_every_adapter()
    {
        var (index, problem) = GpuAdapterSelection.Select(HybridLaptop, "Intel Arc");
        Assert.Equal(-1, index);
        Assert.NotNull(problem);
        Assert.Contains("AMD Radeon(TM) Graphics", problem, StringComparison.Ordinal);
        Assert.Contains("NVIDIA GeForce RTX 4050 Laptop GPU", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ambiguous_request_is_a_problem_instead_of_a_coin_flip()
    {
        var (index, problem) = GpuAdapterSelection.Select(["NVIDIA GeForce RTX 4050", "NVIDIA GeForce RTX 4090"], "NVIDIA");
        Assert.Equal(-1, index);
        Assert.NotNull(problem);
        Assert.Contains("longer name", problem, StringComparison.Ordinal);
    }
}
