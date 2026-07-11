using System.Linq;
using Terminal.Core.Pty;

namespace Terminal.Core.Tests;

/// <summary>
/// T-001.5 smoke test plus the T-001 acceptance check that the engine layer stays
/// free of any UI dependency.
/// </summary>
public class SmokeTests
{
    [Fact]
    public void Core_exposes_pty_session_through_interface()
    {
        Assert.NotNull(typeof(PtySession).Assembly);
        Assert.True(typeof(IPtySession).IsAssignableFrom(typeof(PtySession)));
    }

    [Fact]
    public void Core_assembly_references_no_ui_frameworks()
    {
        var referenced = typeof(PtySession).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(referenced, name =>
            name.Contains("PresentationFramework", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PresentationCore", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("WindowsBase", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("SkiaSharp", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Terminal.UI", StringComparison.OrdinalIgnoreCase));
    }
}
