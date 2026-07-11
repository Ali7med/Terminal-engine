using Terminal.Core.Vt;

namespace Terminal.Core.Tests.Vt;

public class VtParamsTests
{
    [Fact]
    public void Empty_string_yields_no_params_and_defaults()
    {
        var p = VtParams.Parse("");
        Assert.Equal(0, p.Count);
        Assert.Equal(1, p.Get(0, 1));   // absent → default
        Assert.Equal(7, p.Get(3, 7));
    }

    [Fact]
    public void Semicolon_separated_values_parse()
    {
        var p = VtParams.Parse("5;10;0");
        Assert.Equal(3, p.Count);
        Assert.Equal(5, p.Get(0, -1));
        Assert.Equal(10, p.Get(1, -1));
        Assert.Equal(0, p.Get(2, -1));
    }

    [Fact]
    public void Empty_leading_parameter_reads_as_default()
    {
        // "CSI ;5 H" — first parameter omitted, must fall back to the caller default.
        var p = VtParams.Parse(";5");
        Assert.Equal(2, p.Count);
        Assert.Equal(1, p.Get(0, 1));   // empty → default 1
        Assert.Equal(5, p.Get(1, 1));
    }

    [Fact]
    public void Colon_subparameters_are_addressable()
    {
        var p = VtParams.Parse("38:2:0:255:128");
        Assert.Equal(1, p.Count);
        Assert.Equal(5, p.SubCount(0));
        Assert.Equal(38, p.GetSub(0, 0, -1));
        Assert.Equal(2, p.GetSub(0, 1, -1));
        Assert.Equal(255, p.GetSub(0, 3, -1));
        Assert.Equal(128, p.GetSub(0, 4, -1));
        Assert.Equal(-1, p.GetSub(0, 9, -1)); // out of range → default
    }

    [Fact]
    public void Out_of_range_index_returns_default()
    {
        var p = VtParams.Parse("1");
        Assert.Equal(99, p.Get(5, 99));
        Assert.Equal(0, p.SubCount(5));
    }
}
