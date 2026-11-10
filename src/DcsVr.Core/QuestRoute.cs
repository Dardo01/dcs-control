namespace DcsVr.Core;

/// <summary>
/// The Meta Quest route (this fork): DCS runs on the Quest's own OpenXR runtime, Virtual Desktop's VDXR or Meta's
/// Quest Link runtime. No Pimax software, no Sboys driver and no eye tracking: Quad Views only with a fixed (centred)
/// focus area, the focus values from the profile, and the refresh rate set by hand (neither runtime reports it).
/// </summary>
public static class QuestRoute
{
    private static string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    /// <summary>Virtual Desktop Streamer's VDXR manifest; replaceable for tests.</summary>
    public static string VirtualDesktopManifest { get; set; } = Path.Combine(ProgramFiles, "Virtual Desktop Streamer", "OpenXR", "virtualdesktop-openxr.json");
    /// <summary>Meta Quest Link's OpenXR manifest; replaceable for tests.</summary>
    public static string MetaManifest { get; set; } = Path.Combine(ProgramFiles, "Oculus", "Support", "oculus-runtime", "oculus_openxr_64.json");

    public static bool IsVirtualDesktop(string? manifest) => manifest is not null
        && (Path.GetFileName(manifest).Contains("virtualdesktop", StringComparison.OrdinalIgnoreCase) || manifest.Contains("Virtual Desktop", StringComparison.OrdinalIgnoreCase));

    public static bool IsMeta(string? manifest) => manifest is not null
        && (Path.GetFileName(manifest).Contains("oculus", StringComparison.OrdinalIgnoreCase) || manifest.Contains(@"\Oculus\", StringComparison.OrdinalIgnoreCase));

    /// <summary>The installed Quest runtime: the registered one when it is VDXR or Meta's, otherwise VDXR, then Meta's.
    /// Null when neither is installed (the runtime override can still name SteamVR or another runtime).</summary>
    public static string? Find(string? activeRuntime)
    {
        if ((IsVirtualDesktop(activeRuntime) || IsMeta(activeRuntime)) && File.Exists(activeRuntime)) return activeRuntime;
        if (File.Exists(VirtualDesktopManifest)) return VirtualDesktopManifest;
        if (File.Exists(MetaManifest)) return MetaManifest;
        return null;
    }

    /// <summary>A short name for the runtime a Quest profile uses.</summary>
    public static string Name(string? manifest) =>
        IsVirtualDesktop(manifest) ? "Virtual Desktop (VDXR)" : IsMeta(manifest) ? "Meta Quest Link" : manifest is null ? "no Quest runtime found" : Path.GetFileName(manifest);
}

/// <summary>The OpenXR runtime each headset route launches DCS with.</summary>
public static class RouteRuntime
{
    /// <summary>The profile's runtime override, otherwise the route's own runtime.</summary>
    public static string? Manifest(VrProfile profile, InventorySnapshot inventory) => profile.RuntimeManifestPath ?? profile.Runtime switch
    {
        RuntimeKind.Pimax => inventory.PimaxRuntime,
        RuntimeKind.Quest => inventory.QuestRuntime,
        _ => inventory.SteamVrRuntime
    };

    /// <summary>The route's name in messages.</summary>
    public static string Describe(RuntimeKind runtime) => runtime switch
    {
        RuntimeKind.Pimax => "the Pimax runtime",
        RuntimeKind.Quest => "a Meta Quest (Virtual Desktop or Quest Link)",
        _ => "Sboys through SteamVR"
    };

    /// <summary>The help topic for the route's runtime.</summary>
    public static string HelpTopic(RuntimeKind runtime) => runtime switch { RuntimeKind.Pimax => "pimax", RuntimeKind.Quest => "quest", _ => "steamvr" };
}
