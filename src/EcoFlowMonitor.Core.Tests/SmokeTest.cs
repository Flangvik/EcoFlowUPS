using FluentAssertions;
using Xunit;

namespace EcoFlowMonitor.Core.Tests;

/// <summary>
/// Placeholder test ensuring the test project builds and xUnit + FluentAssertions
/// are wired up. Safe to delete once real tests start landing in Phase 3+.
/// </summary>
public class SmokeTest
{
    [Fact]
    public void CoreTestsProjectBuildsAndRunsXunit()
    {
        "EcoFlowMonitor.Core.Tests".Should().NotBeNullOrEmpty();
    }
}
