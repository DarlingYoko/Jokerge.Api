using Gml.Web.Api.Core.Services;

namespace Gml.WebApi.Tests;

/// <summary>
///     The launcher's mods page asks for profile info with RamSize = 0. The server always set the
///     minimum heap to 512 MB, so the minimum exceeded the maximum and CmlLib threw while building
///     the game process.
/// </summary>
public class StartupRamTests
{
    [TestCase(0, 1024, 512)]
    [TestCase(-1, 1024, 512)]
    [TestCase(256, 256, 256)]
    [TestCase(512, 512, 512)]
    [TestCase(4096, 4096, 512)]
    public void Resolve_KeepsMinimumWithinMaximum(int requested, int expectedMax, int expectedMin)
    {
        var (min, max) = StartupRam.Resolve(requested);

        Assert.Multiple(() =>
        {
            Assert.That(max, Is.EqualTo(expectedMax));
            Assert.That(min, Is.EqualTo(expectedMin));
            Assert.That(min, Is.LessThanOrEqualTo(max));
        });
    }
}
