// @module: Foundation.Launcher.RootLauncherOptions
// @purpose: Additive caller-supplied CLI flag extension contracts for RootLauncherHost sync-wait, value-taking, and path-rebase options.
// @depends-on: System.Collections.Generic
// @exports: RootLauncherOptions

using System.Collections.Generic;

namespace Foundation.Launcher;

public sealed record RootLauncherOptions(
    IReadOnlyCollection<string>? ExtraSyncWaitFlags = null,
    IReadOnlyCollection<string>? ExtraValueOptions = null,
    IReadOnlyCollection<string>? ExtraPathValueOptions = null)
{
    public static RootLauncherOptions Default { get; } = new();
}
