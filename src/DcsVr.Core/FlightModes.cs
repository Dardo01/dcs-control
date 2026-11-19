using System.Diagnostics;

namespace DcsVr.Core;

/// <summary>How DCS starts in a flight mode: on the monitor (2D) or in the headset (VR).</summary>
public enum FlightKind { Desktop, Vr }

/// <summary>A program started with DCS in a flight mode (TrackIR, SRS, Tacview…). With <see cref="CloseOnExit"/>, it is
/// closed when DCS exits, but only if this launch started it: a program already running is left alone.</summary>
public sealed record CompanionApp
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string? Arguments { get; init; }
    public bool Enabled { get; init; } = true;
    public bool CloseOnExit { get; init; } = true;
    /// <summary>Seconds to wait after starting it, before DCS starts (TrackIR must be up before DCS looks for it).</summary>
    public int DelaySeconds { get; init; } = 2;
}

/// <summary>The ReShade switch: ReShade's DLL next to DCS.exe is renamed to "&lt;name&gt;.off" to turn it off and back to
/// turn it on. <see cref="DllPath"/> empty: found by itself (dxgi.dll or d3d11.dll next to DCS.exe, with ReShade.ini beside it
/// or "ReShade" in its version information).</summary>
public sealed record ReShadeSettings
{
    public string DllPath { get; init; } = "";
}

/// <summary>Oculus Debug Tool commands run before DCS starts and after it exits (VR over Quest Link).</summary>
public sealed record OculusDebugSettings
{
    public bool Enabled { get; init; } = true;
    /// <summary>OculusDebugToolCLI.exe. Empty: looked up in the usual Meta Horizon and Oculus folders.</summary>
    public string CliPath { get; init; } = "";
    public IReadOnlyList<string> BeforeFlight { get; init; } = ["server:asw.Auto", "set-pixels-per-display-pixel-override 1.5"];
    /// <summary>Puts Link back as other games expect it.</summary>
    public IReadOnlyList<string> AfterFlight { get; init; } = ["server:asw.Auto", "set-pixels-per-display-pixel-override 0"];
}

/// <summary>A one-click way to fly: DCS started in 2D or VR with its own DCS settings and programs.</summary>
public sealed record FlightMode
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public FlightKind Kind { get; init; }
    /// <summary>options.lua values written before DCS starts, as Lua literals by dotted path ("graphics.SSAO" → "0",
    /// "graphics.visibRange" → "\"High\""). Only settings already in options.lua are changed.</summary>
    public IReadOnlyDictionary<string, string> DcsSettings { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<CompanionApp> Apps { get; init; } = [];
    public OculusDebugSettings? OculusDebug { get; init; }
    /// <summary>VREM (ReShade VR Enhancer Mod) in this mode: its ReShade add-on is renamed on or off in bin and bin-mt.
    /// Null: left as it is.</summary>
    public bool? Vrem { get; init; }
    public string LaunchArgument => Kind == FlightKind.Vr ? "--force_enable_VR" : "--force_disable_VR";
}

public sealed record FlightModeSet
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<FlightMode> Modes { get; init; } = [];
    public ReShadeSettings ReShade { get; init; } = new();
    /// <summary>The main page's ReShade switch, for 2D and VR. Null: ReShade is left as it is.</summary>
    public bool? ReShadeOn { get; init; }
    public string LastModeId { get; init; } = "2d";

    public FlightMode Get(string id) => Modes.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("No existe el modo " + id + ". Modos: " + string.Join(", ", Modes.Select(m => m.Id)));

    /// <summary>Starting points from the community research (docs/FLIGHT_MODES.md): edit them to suit the PC.</summary>
    public static FlightModeSet Defaults { get; } = new()
    {
        Modes =
        [
            new()
            {
                Id = "2d", Name = "2D", Kind = FlightKind.Desktop, Vrem = false,
                DcsSettings = new Dictionary<string, string> { ["VR.enable"] = "false" },
                Apps = [new() { Name = "TrackIR", Path = @"C:\Program Files (x86)\NaturalPoint\TrackIR5\TrackIR5.exe" }]
            },
            new()
            {
                Id = "vr", Name = "VR", Kind = FlightKind.Vr, Vrem = true,
                DcsSettings = new Dictionary<string, string>
                {
                    ["VR.enable"] = "true", ["VR.pixel_density"] = "1", ["graphics.SSAO"] = "0", ["graphics.SSLR"] = "false",
                    ["graphics.preloadRadius"] = "60000"
                },
                OculusDebug = new()
            }
        ]
    };
}

/// <summary>Reads and writes the flight modes, and does the work around a launch that is not DCS Control's applied
/// profile: options.lua values, the ReShade switch, Oculus Debug Tool and the companion programs.</summary>
public static class FlightModes
{
    public const string FileName = "flight-modes.json";
    public static string PathIn(string stateRoot) => Path.Combine(stateRoot, FileName);

    public static FlightModeSet Load(string stateRoot)
    {
        var path = PathIn(stateRoot);
        if (!File.Exists(path)) return FlightModeSet.Defaults;
        try { return JsonData.Deserialize<FlightModeSet>(File.ReadAllText(path)); }
        catch (System.Text.Json.JsonException e) { throw new InvalidDataException("No se puede leer el archivo de modos: " + path + " (" + e.Message + ")", e); }
    }

    public static void Save(string stateRoot, FlightModeSet set) => AtomicFile.WriteText(PathIn(stateRoot), JsonData.Serialize(set));

    /// <summary>options.lua with the mode's values. Settings DCS Control's applied profile owns (<paramref name="owned"/>)
    /// and settings missing from the file are left as they are and listed in Skipped.</summary>
    public static (string Text, IReadOnlyList<string> Changed, IReadOnlyList<string> Skipped) PatchOptions(string text, IReadOnlyDictionary<string, string> settings, IReadOnlyCollection<string> owned, IDictionary<string, string>? originals = null)
    {
        var changed = new List<string>(); var skipped = new List<string>();
        foreach (var (path, literal) in settings)
        {
            var keys = path.Split('.');
            if (owned.Contains(path, StringComparer.Ordinal)) { skipped.Add(path + " (lo controla el perfil aplicado)"); continue; }
            var lua = new LuaOptions(text);
            var current = lua.Get(keys);
            if (current is null) { skipped.Add(path + " (no está en options.lua)"); continue; }
            if (LuaOptions.NormalizeLiteral(current) == LuaOptions.NormalizeLiteral(literal)) continue;
            originals?.TryAdd(path, current);
            text = lua.SetLiteral(keys, literal); changed.Add(path + " = " + literal);
        }
        _ = new LuaOptions(text);
        return (text, changed, skipped);
    }

    /// <summary>options.lua values as they were before a flight mode first changed them, so "Restablecer todo" can put
    /// them back. Kept in the flight-modes folder of the state root.</summary>
    public static string OriginalsPath(string stateRoot) => Path.Combine(stateRoot, "flight-modes", "options-originals.json");

    public static Dictionary<string, string> LoadOriginals(string stateRoot)
    {
        var path = OriginalsPath(stateRoot);
        try { return File.Exists(path) ? JsonData.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) : []; }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException) { return []; }
    }

    public static void SaveOriginals(string stateRoot, IReadOnlyDictionary<string, string> originals)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OriginalsPath(stateRoot))!);
        AtomicFile.WriteText(OriginalsPath(stateRoot), JsonData.Serialize(originals));
    }

    /// <summary>The modes with nothing of their own left: no options.lua values, no VREM or ReShade switching and no
    /// Oculus Debug Tool. Programs and launch arguments stay, so Volar only opens DCS (and the chosen programs).</summary>
    public static FlightModeSet Neutral(FlightModeSet set) => set with
    {
        ReShadeOn = null,
        Modes = [.. set.Modes.Select(m => m with
        {
            DcsSettings = new Dictionary<string, string>(),
            Vrem = null,
            OculusDebug = m.OculusDebug is null ? null : m.OculusDebug with { Enabled = false }
        })]
    };

    /// <summary>"Restablecer todo": options.lua values back to what they were before the flight modes changed them,
    /// ReShade and VREM back on as installed, and the modes made neutral. Returns what was done, in Spanish.</summary>
    public static List<string> ResetAll(string stateRoot, string? optionsPath, string? dcsExecutable)
    {
        var notes = new List<string>();
        var set = Load(stateRoot);
        var originals = LoadOriginals(stateRoot);
        var touched = set.Modes.SelectMany(m => m.DcsSettings.Keys).Concat(FlightModeSet.Defaults.Modes.SelectMany(m => m.DcsSettings.Keys)).Distinct(StringComparer.Ordinal).ToList();
        if (optionsPath is not null && File.Exists(optionsPath) && originals.Count > 0)
        {
            var (text, changed, _) = PatchOptions(File.ReadAllText(optionsPath), originals, []);
            if (changed.Count > 0) { AtomicFile.WriteText(optionsPath, text); notes.Add("options.lua vuelve a como estaba: " + string.Join(", ", changed) + "."); }
            else notes.Add("options.lua ya estaba como antes.");
        }
        var unknown = touched.Where(k => !originals.ContainsKey(k)).ToList();
        if (unknown.Count > 0) notes.Add("Sin valor original guardado (revísalos en las opciones de DCS si los cambiaste): " + string.Join(", ", unknown) + ".");
        if (FindReShade(set.ReShade, dcsExecutable) is { } reShade && ApplyReShade(reShade, true) is { } r) notes.Add(r);
        if (FindVrem(dcsExecutable) is { Count: > 0 } vrem && ApplyVrem(vrem, true) is { } v) notes.Add(v);
        Save(stateRoot, Neutral(set));
        if (File.Exists(OriginalsPath(stateRoot))) File.Delete(OriginalsPath(stateRoot));
        notes.Add("Los modos ya no cambian nada: sin ajustes de DCS, sin interruptor de ReShade ni VREM y sin Oculus Debug Tool.");
        return notes;
    }

    public const string OffSuffix = ".off";
    private static readonly string[] ReShadeNames = ["dxgi.dll", "d3d11.dll"];

    /// <summary>ReShade's DLL for this DCS: the configured path, or dxgi.dll / d3d11.dll next to DCS.exe (on or turned
    /// off) that is ReShade's. Null when there is none.</summary>
    public static string? FindReShade(ReShadeSettings settings, string? dcsExecutable)
    {
        if (!string.IsNullOrWhiteSpace(settings.DllPath)) return settings.DllPath.Trim();
        if (dcsExecutable is null || Path.GetDirectoryName(dcsExecutable) is not { } bin) return null;
        foreach (var name in ReShadeNames)
        {
            var path = Path.Combine(bin, name);
            if (File.Exists(path) ? IsReShade(path) : File.Exists(path + OffSuffix) && IsReShade(path + OffSuffix)) return path;
        }
        return null;
    }

    /// <summary>ReShade.ini beside it, or "ReShade" in its version information.</summary>
    public static bool IsReShade(string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        if (File.Exists(Path.Combine(folder, "ReShade.ini"))) return true;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return (info.ProductName + " " + info.FileDescription).Contains("ReShade", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    /// <summary>"on", "off", or null when ReShade is not found.</summary>
    public static string? ReShadeState(string? dll) => dll is null ? null : File.Exists(dll) ? "on" : File.Exists(dll + OffSuffix) ? "off" : null;

    /// <summary>Turns ReShade on or off by renaming its DLL. Returns what changed, or null.</summary>
    public static string? ApplyReShade(string? dll, bool on)
    {
        if (dll is null) return null;
        var off = dll + OffSuffix;
        if (on)
        {
            if (File.Exists(dll) || !File.Exists(off)) return null;
            File.Move(off, dll);
            return "ReShade activado.";
        }
        if (!File.Exists(dll)) return null;
        if (File.Exists(off)) File.Delete(off);
        File.Move(dll, off);
        return "ReShade desactivado.";
    }

    /// <summary>VREM's add-on files ("…VREM….addon64", on, or the same name + ".off") in DCS's bin and bin-mt folders,
    /// as their "on" paths. Empty when VREM is not installed.</summary>
    public static IReadOnlyList<string> FindVrem(string? dcsExecutable)
    {
        if (dcsExecutable is null || Path.GetDirectoryName(dcsExecutable) is not { } bin) return [];
        var root = Path.GetDirectoryName(bin);
        var folders = new[] { bin, root is null ? null : Path.Combine(root, "bin"), root is null ? null : Path.Combine(root, "bin-mt") }
            .Where(f => f is not null && Directory.Exists(f)).Select(f => Path.GetFullPath(f!)).Distinct(StringComparer.OrdinalIgnoreCase);
        var found = new List<string>();
        foreach (var folder in folders)
            foreach (var file in Directory.EnumerateFiles(folder, "*VREM*.addon*"))
            {
                var on = file.EndsWith(OffSuffix, StringComparison.OrdinalIgnoreCase) ? file[..^OffSuffix.Length] : file;
                if ((on.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase) || on.EndsWith(".addon", StringComparison.OrdinalIgnoreCase)) && !found.Contains(on, StringComparer.OrdinalIgnoreCase)) found.Add(on);
            }
        return found;
    }

    /// <summary>"on" when any VREM add-on is active, "off" when all are turned off, null when there is none.</summary>
    public static string? VremState(IReadOnlyList<string> addons) => addons.Count == 0 ? null : addons.Any(File.Exists) ? "on" : "off";

    /// <summary>Turns VREM on or off by renaming its add-on files. Returns what changed, or null.</summary>
    public static string? ApplyVrem(IReadOnlyList<string> addons, bool on)
    {
        var changed = false;
        foreach (var addon in addons)
        {
            var off = addon + OffSuffix;
            if (on && !File.Exists(addon) && File.Exists(off)) { File.Move(off, addon); changed = true; }
            if (!on && File.Exists(addon)) { if (File.Exists(off)) File.Delete(off); File.Move(addon, off); changed = true; }
        }
        return changed ? on ? "VREM activado." : "VREM desactivado." : null;
    }

    public static string OculusScript(IEnumerable<string> commands, bool exit = true)
    {
        var lines = commands.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim());
        return string.Join("\r\n", exit ? lines.Append("exit") : lines) + "\r\n";
    }

    /// <summary>Starts OculusDebugToolCLI with the before-flight commands and leaves it open for the whole flight,
    /// because Link keeps some of its values only while the tool runs. <see cref="StopOculus"/> sends the after-flight
    /// commands and closes it.</summary>
    public static (Process? Process, string? Problem) StartOculus(OculusDebugSettings settings, IReadOnlyList<string> commands, string scratchFolder)
    {
        if (!settings.Enabled) return (null, null);
        var cli = FindOculusCli(settings);
        if (cli is null) return (null, "no se encuentra OculusDebugToolCLI.exe; indica su ruta en Ajustes.");
        Directory.CreateDirectory(scratchFolder);
        var script = Path.Combine(scratchFolder, "oculus-debug-commands.txt");
        AtomicFile.WriteText(script, OculusScript(commands, exit: false));
        var start = new ProcessStartInfo(cli)
        {
            ArgumentList = { "-f", script }, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(cli)!,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        var process = Process.Start(start);
        if (process is null) return (null, "OculusDebugToolCLI.exe no arrancó.");
        var output = new System.Collections.Concurrent.ConcurrentQueue<string>();
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) output.Enqueue(e.Data.Trim()); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) output.Enqueue(e.Data.Trim()); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (process.WaitForExit(TimeSpan.FromSeconds(3)))
        {
            var last = output.LastOrDefault();
            process.Dispose();
            return (null, "OculusDebugToolCLI.exe se cerró solo (¿está conectado Quest Link?)." + (last is null ? "" : " Respondió: " + last));
        }
        return (process, null);
    }

    /// <summary>Sends the after-flight commands to the tool left open by <see cref="StartOculus"/> and closes it.</summary>
    public static string? StopOculus(Process process, IReadOnlyList<string> commands)
    {
        using (process)
        {
            try
            {
                if (process.HasExited) return "OculusDebugToolCLI.exe se había cerrado durante el vuelo.";
                process.StandardInput.Write(OculusScript(commands));
                process.StandardInput.Close();
            }
            catch (Exception e) when (e is IOException or InvalidOperationException) { }
            if (process.WaitForExit(TimeSpan.FromSeconds(30))) return null;
            try { process.Kill(); } catch (InvalidOperationException) { }
            return "OculusDebugToolCLI.exe no se cerró al salir; se ha cerrado a la fuerza.";
        }
    }

    public static IReadOnlyList<string> OculusCliCandidates { get; set; } =
    [
        @"C:\Program Files\Meta Horizon\Support\oculus-diagnostics\OculusDebugToolCLI.exe",
        @"C:\Program Files\Oculus\Support\oculus-diagnostics\OculusDebugToolCLI.exe"
    ];

    public static string? FindOculusCli(OculusDebugSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.CliPath) ? (File.Exists(settings.CliPath) ? settings.CliPath : null) : OculusCliCandidates.FirstOrDefault(File.Exists);

    /// <summary>Runs the commands through OculusDebugToolCLI -f. Returns a problem, or null.</summary>
    public static string? RunOculus(OculusDebugSettings settings, IReadOnlyList<string> commands, string scratchFolder)
    {
        if (!settings.Enabled || commands.Count == 0) return null;
        var cli = FindOculusCli(settings);
        if (cli is null) return "no se encuentra OculusDebugToolCLI.exe; indica su ruta en Ajustes.";
        Directory.CreateDirectory(scratchFolder);
        var script = Path.Combine(scratchFolder, "oculus-debug-commands.txt");
        AtomicFile.WriteText(script, OculusScript(commands));
        // Stdin is redirected and closed after "exit": without it the CLI can sit waiting for typed commands.
        var start = new ProcessStartInfo(cli)
        {
            ArgumentList = { "-f", script }, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(cli)!,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var process = Process.Start(start);
        if (process is null) return "OculusDebugToolCLI.exe no arrancó.";
        var output = new System.Collections.Concurrent.ConcurrentQueue<string>();
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) output.Enqueue(e.Data.Trim()); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) output.Enqueue(e.Data.Trim()); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try { process.StandardInput.Write("exit\r\n"); process.StandardInput.Close(); } catch (IOException) { }
        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            var last = output.LastOrDefault();
            return "OculusDebugToolCLI.exe no respondió en 30 segundos (¿está conectado Quest Link?)." + (last is null ? "" : " Última línea: " + last);
        }
        process.WaitForExit();
        var error = output.FirstOrDefault(line => line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("unknown", StringComparison.OrdinalIgnoreCase) || line.Contains("invalid", StringComparison.OrdinalIgnoreCase));
        if (error is not null) return "respondió: " + error;
        return null;
    }

    /// <summary>Process names already running, to leave those programs alone. Replaceable in tests.</summary>
    public static Func<string, bool> IsRunning { get; set; } = image =>
    {
        var name = Path.GetFileNameWithoutExtension(image);
        var running = Process.GetProcessesByName(name);
        foreach (var p in running) p.Dispose();
        return running.Length > 0;
    };

    public static Func<CompanionApp, Process?> StartApp { get; set; } = app =>
        Process.Start(new ProcessStartInfo(app.Path) { Arguments = app.Arguments ?? "", UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(app.Path) ?? "" });

    public static Action<TimeSpan> Delay { get; set; } = Thread.Sleep;

    /// <summary>Waits until DCS has really closed. The process the app starts can hand over to another DCS.exe and exit
    /// within seconds (Steam's restart or DCS's own updater check), so a quick exit is followed by watching for DCS.exe by
    /// name until it is gone, instead of ending the flight there and closing its programs.</summary>
    public static void WaitForDcsExit(Process launched, string? dcsExecutable)
    {
        var name = Path.GetFileNameWithoutExtension(string.IsNullOrWhiteSpace(dcsExecutable) ? "DCS.exe" : dcsExecutable);
        var started = DateTime.UtcNow;
        launched.WaitForExit();
        var handOver = DateTime.UtcNow - started < TimeSpan.FromMinutes(2);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        var seen = false;
        while (true)
        {
            if (IsRunning(name + ".exe")) seen = true;
            else if (!handOver || seen || DateTime.UtcNow > deadline)
            {
                Delay(TimeSpan.FromSeconds(5));
                if (!IsRunning(name + ".exe")) return;
                seen = true;
            }
            Delay(TimeSpan.FromSeconds(5));
        }
    }
}

/// <summary>What a flight mode set up before DCS started, so <see cref="End"/> can undo the per-flight part.</summary>
public sealed class FlightSession(FlightMode mode, string scratchFolder)
{
    private readonly List<(CompanionApp App, Process Process)> _started = [];
    private Process? _oculus;
    /// <summary>DCS.exe this flight started, set by <see cref="Begin"/>.</summary>
    public string? DcsExecutable { get; private set; }
    public FlightMode Mode { get; } = mode;
    public List<string> Notes { get; } = [];

    /// <summary>Everything before DCS starts: options.lua, the DLSS 5 switch, Oculus Debug Tool and the programs.</summary>
    public void Begin(FlightModeSet set, string? optionsPath, IReadOnlyCollection<string> ownedSettings, string? dcsExecutable)
    {
        DcsExecutable = dcsExecutable;
        if (optionsPath is not null && File.Exists(optionsPath) && Mode.DcsSettings.Count > 0)
        {
            var stateRoot = Path.GetDirectoryName(Path.GetFullPath(scratchFolder))!;
            var originals = FlightModes.LoadOriginals(stateRoot);
            var known = originals.Count;
            var (text, changed, skipped) = FlightModes.PatchOptions(File.ReadAllText(optionsPath), Mode.DcsSettings, ownedSettings, originals);
            if (originals.Count > known) FlightModes.SaveOriginals(stateRoot, originals);
            if (changed.Count > 0) { AtomicFile.WriteText(optionsPath, text); Notes.Add("Ajustes de DCS: " + string.Join(", ", changed) + "."); }
            if (skipped.Count > 0) Notes.Add("Sin cambiar: " + string.Join(", ", skipped) + ".");
        }
        if (set.ReShadeOn is { } reShadeOn)
        {
            var reShade = FlightModes.FindReShade(set.ReShade, dcsExecutable);
            if (reShade is null) { if (reShadeOn) Notes.Add("No se encuentra ReShade junto a DCS.exe; indica su DLL en Ajustes."); }
            else if (FlightModes.ApplyReShade(reShade, reShadeOn) is { } changed) Notes.Add(changed);
        }
        if (Mode.Vrem is { } vremOn)
        {
            var vrem = FlightModes.FindVrem(dcsExecutable);
            if (vrem.Count == 0) { if (vremOn) Notes.Add("VREM está activado en este modo, pero no está instalado junto a DCS."); }
            else
            {
                if (FlightModes.ApplyVrem(vrem, vremOn) is { } changed) Notes.Add(changed);
                if (vremOn && set.ReShadeOn == false) Notes.Add("VREM necesita ReShade activado para verse.");
            }
        }
        if (Mode.OculusDebug is { Enabled: true } odt)
        {
            var (oculus, problem) = FlightModes.StartOculus(odt, odt.BeforeFlight, scratchFolder);
            _oculus = oculus;
            Notes.Add("Oculus Debug Tool: " + (problem ?? "abierto durante el vuelo."));
        }
        foreach (var app in Mode.Apps.Where(a => a.Enabled && !string.IsNullOrWhiteSpace(a.Path)))
        {
            if (!File.Exists(app.Path)) { Notes.Add("No se encuentra " + app.Name + " en " + app.Path + "."); continue; }
            if (FlightModes.IsRunning(app.Path)) { Notes.Add(app.Name + " ya estaba abierto."); continue; }
            try
            {
                var process = FlightModes.StartApp(app);
                if (process is not null) _started.Add((app, process));
                Notes.Add(app.Name + " abierto.");
                if (app.DelaySeconds > 0) FlightModes.Delay(TimeSpan.FromSeconds(app.DelaySeconds));
            }
            catch (System.ComponentModel.Win32Exception e) { Notes.Add(app.Name + " no se pudo abrir: " + e.Message); }
        }
    }

    public void AddLaunchArgument(ProcessStartInfo start)
    {
        if (!start.ArgumentList.Contains(Mode.LaunchArgument)) start.ArgumentList.Add(Mode.LaunchArgument);
    }

    /// <summary>After DCS exits: closes the programs this launch started and runs Oculus Debug Tool's after-flight commands.</summary>
    public IReadOnlyList<string> End()
    {
        var notes = new List<string>();
        foreach (var (app, process) in _started)
        {
            using (process)
            {
                if (!app.CloseOnExit) continue;
                try
                {
                    if (process.HasExited) continue;
                    process.CloseMainWindow();
                    if (!process.WaitForExit(TimeSpan.FromSeconds(5))) process.Kill(true);
                    notes.Add(app.Name + " cerrado.");
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { notes.Add(app.Name + " no se pudo cerrar: " + e.Message); }
            }
        }
        _started.Clear();
        if (Mode.OculusDebug is { Enabled: true } odt)
        {
            var problem = _oculus is { } oculus ? FlightModes.StopOculus(oculus, odt.AfterFlight) : FlightModes.RunOculus(odt, odt.AfterFlight, scratchFolder);
            _oculus = null;
            notes.Add("Oculus Debug Tool: " + (problem ?? "cerrado."));
        }
        return notes;
    }
}
