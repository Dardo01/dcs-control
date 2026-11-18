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

/// <summary>One file swapped in DCS's folder by a flight mode: the DLSS 5 DLL on or off. On: <see cref="ReplacementPath"/>
/// is copied over <see cref="TargetPath"/> (the original is backed up the first time). Off: the original goes back.</summary>
public sealed record DllSwap
{
    public string Name { get; init; } = "DLSS 5";
    /// <summary>The file DCS loads, e.g. DCS's bin-mt\nvngx_dlss.dll. Empty: the switch is not set up.</summary>
    public string TargetPath { get; init; } = "";
    /// <summary>The DLL to put in its place when the switch is on.</summary>
    public string ReplacementPath { get; init; } = "";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(TargetPath) && !string.IsNullOrWhiteSpace(ReplacementPath);
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
    public string LaunchArgument => Kind == FlightKind.Vr ? "--force_enable_VR" : "--force_disable_VR";
}

public sealed record FlightModeSet
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<FlightMode> Modes { get; init; } = [];
    /// <summary>The DLSS 5 DLL the switch on the main page turns on or off, in 2D and in VR.</summary>
    public DllSwap Dlss5 { get; init; } = new();
    /// <summary>The main page's DLSS 5 switch: the replacement DLL in place for the next flight, whichever the mode.</summary>
    public bool Dlss5On { get; init; }
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
                Id = "2d", Name = "2D", Kind = FlightKind.Desktop,
                DcsSettings = new Dictionary<string, string> { ["VR.enable"] = "false" },
                Apps = [new() { Name = "TrackIR", Path = @"C:\Program Files (x86)\NaturalPoint\TrackIR5\TrackIR5.exe" }]
            },
            new()
            {
                Id = "vr", Name = "VR", Kind = FlightKind.Vr,
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
/// profile: options.lua values, the DLSS 5 switch, Oculus Debug Tool and the companion programs.</summary>
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
    public static (string Text, IReadOnlyList<string> Changed, IReadOnlyList<string> Skipped) PatchOptions(string text, IReadOnlyDictionary<string, string> settings, IReadOnlyCollection<string> owned)
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
            text = lua.SetLiteral(keys, literal); changed.Add(path + " = " + literal);
        }
        _ = new LuaOptions(text);
        return (text, changed, skipped);
    }

    /// <summary>Puts the DLL switch in the wanted state. The original target is kept under <paramref name="backupFolder"/>
    /// the first time the replacement goes in (or a marker that there was none). Returns what changed, or null.</summary>
    public static string? ApplyDllSwap(DllSwap swap, bool on, string backupFolder)
    {
        if (!swap.IsConfigured) return on ? "DLSS 5 está activado, pero falta elegir su DLL en Ajustes." : null;
        Directory.CreateDirectory(backupFolder);
        var original = Path.Combine(backupFolder, "original-" + Path.GetFileName(swap.TargetPath));
        var absent = original + ".absent";
        bool Same(string a, string b) => File.Exists(a) && File.Exists(b) && Hashing.FileSha256(a) == Hashing.FileSha256(b);
        if (on)
        {
            if (!File.Exists(swap.ReplacementPath)) throw new FileNotFoundException("No se encuentra el DLL de DLSS 5: " + swap.ReplacementPath);
            if (Same(swap.TargetPath, swap.ReplacementPath)) return null;
            if (!File.Exists(original) && !File.Exists(absent))
            {
                if (File.Exists(swap.TargetPath)) File.Copy(swap.TargetPath, original);
                else File.WriteAllText(absent, "");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(swap.TargetPath)!);
            File.Copy(swap.ReplacementPath, swap.TargetPath, true);
            return "DLSS 5 activado.";
        }
        if (File.Exists(original))
        {
            if (Same(swap.TargetPath, original)) return null;
            File.Copy(original, swap.TargetPath, true);
            return "DLSS 5 desactivado (vuelve el DLL original).";
        }
        if (File.Exists(absent) && File.Exists(swap.TargetPath) && Same(swap.TargetPath, swap.ReplacementPath))
        {
            File.Delete(swap.TargetPath);
            return "DLSS 5 desactivado.";
        }
        return null;
    }

    /// <summary>Whether the replacement DLL is in place now.</summary>
    public static bool DllSwapOn(DllSwap swap) => swap.IsConfigured && File.Exists(swap.TargetPath) && File.Exists(swap.ReplacementPath)
        && Hashing.FileSha256(swap.TargetPath) == Hashing.FileSha256(swap.ReplacementPath);

    public static string OculusScript(IEnumerable<string> commands) =>
        string.Join("\r\n", commands.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Append("exit")) + "\r\n";

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
        using var process = Process.Start(new ProcessStartInfo(cli) { ArgumentList = { "-f", script }, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(cli)! });
        if (process is null) return "OculusDebugToolCLI.exe no arrancó.";
        if (!process.WaitForExit(TimeSpan.FromSeconds(15))) { try { process.Kill(); } catch (InvalidOperationException) { } return "OculusDebugToolCLI.exe no terminó en 15 segundos."; }
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
}

/// <summary>What a flight mode set up before DCS started, so <see cref="End"/> can undo the per-flight part.</summary>
public sealed class FlightSession(FlightMode mode, string scratchFolder)
{
    private readonly List<(CompanionApp App, Process Process)> _started = [];
    public FlightMode Mode { get; } = mode;
    public List<string> Notes { get; } = [];

    /// <summary>Everything before DCS starts: options.lua, the DLSS 5 switch, Oculus Debug Tool and the programs.</summary>
    public void Begin(FlightModeSet set, string? optionsPath, IReadOnlyCollection<string> ownedSettings, string backupFolder)
    {
        if (optionsPath is not null && File.Exists(optionsPath) && Mode.DcsSettings.Count > 0)
        {
            var (text, changed, skipped) = FlightModes.PatchOptions(File.ReadAllText(optionsPath), Mode.DcsSettings, ownedSettings);
            if (changed.Count > 0) { AtomicFile.WriteText(optionsPath, text); Notes.Add("Ajustes de DCS: " + string.Join(", ", changed) + "."); }
            if (skipped.Count > 0) Notes.Add("Sin cambiar: " + string.Join(", ", skipped) + ".");
        }
        if (FlightModes.ApplyDllSwap(set.Dlss5, set.Dlss5On, backupFolder) is { } dll) Notes.Add(dll);
        if (Mode.OculusDebug is { } odt && FlightModes.RunOculus(odt, odt.BeforeFlight, scratchFolder) is { } problem) Notes.Add("Oculus Debug Tool: " + problem);
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
        if (Mode.OculusDebug is { } odt && FlightModes.RunOculus(odt, odt.AfterFlight, scratchFolder) is { } problem) notes.Add("Oculus Debug Tool: " + problem);
        return notes;
    }
}
