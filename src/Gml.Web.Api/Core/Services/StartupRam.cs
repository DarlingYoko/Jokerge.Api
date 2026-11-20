namespace Gml.Web.Api.Core.Services;

public static class StartupRam
{
    public const int DefaultMaximumMb = 1024;
    public const int DefaultMinimumMb = 512;

    /// <summary>
    ///     Heap bounds for the game process. Launchers send RamSize = 0 when they only need profile
    ///     info (the mods page does), and CmlLib rejects a minimum above the maximum.
    /// </summary>
    public static (int MinimumMb, int MaximumMb) Resolve(int requestedMb)
    {
        var maximum = requestedMb > 0 ? requestedMb : DefaultMaximumMb;
        return (Math.Min(DefaultMinimumMb, maximum), maximum);
    }
}
