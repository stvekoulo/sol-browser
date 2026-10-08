using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;

namespace Browser.App;

public sealed record AiModel(string Id, string Name, int? SizeMb, string License, bool Cached, string Device);
public sealed record AiCheck(string Id, string Text, long LoadMs, long ElapsedMs, long MemoryMb, string Device);
public sealed record AiOptimization(string Id, long CpuMs, long GpuMs, string Status);

/// <summary>Le moteur natif reste dans Sol.AI.exe ; préparation facultative du modèle installé.</summary>
public sealed class SolAiService : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static string Exe => Path.Combine(AppContext.BaseDirectory, "ai", "Sol.AI.exe");
    private static string DirectoryPath => Path.Combine(AppPaths.BaseDir, "ai");
    private static string CatalogPath => Path.Combine(DirectoryPath, "catalog.json");
    private static string OptimizationPath => Path.Combine(DirectoryPath, "optimization.json");
    // Le parcours Windows validé reste facultatif. Pas de dépendance ajoutée au navigateur x86/Windows 10.
    public bool Available => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100)
        && (RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            || RuntimeInformation.ProcessArchitecture == Architecture.X64 && System.Runtime.Intrinsics.X86.Avx2.IsSupported)
        && File.Exists(Exe) && Memory().Total >= SolAiProfiles.All[0].RamMb - 256 && Environment.ProcessorCount >= SolAiProfiles.All[0].Threads;
    public List<AiModel> Models { get; private set; } = [];
    public AiCheck? Check { get; private set; }
    public string? Result { get; private set; }
    public string? Partial { get; private set; }
    public AiOptimization? Optimization { get; private set; }
    public bool Incomplete { get; private set; }
    public string Phase { get; private set; } = "idle";
    public string? Error { get; private set; }
    public string? CurrentId { get; private set; }
    public int Percent { get; private set; }
    public bool Busy => _process != null || _generation != null;
    public bool Warm => _warm != null && _warmReady;
    public int KeepMinutes { get; private set; } = 45;
    public void SetKeepMinutes(int minutes)
    {
        if (minutes is < 0 or > 10080) return;
        KeepMinutes = minutes;
        try { if (_warm != null) { _warm.StandardInput.WriteLine("keep:" + minutes); _warm.StandardInput.Flush(); } }
        catch (Exception e) when (e is IOException or InvalidOperationException) { }
    }
    public long FreeMb
    {
        get { try { return new DriveInfo(Path.GetPathRoot(DirectoryPath)!).AvailableFreeSpace / (1024 * 1024); } catch (IOException) { return 0; } }
    }
    public string CacheDirectory => Path.Combine(DirectoryPath, "models");
    public event Action? Changed;
    private Process? _process;
    private Process? _warm;
    private string? _warmId;
    private TaskCompletionSource<JsonElement>? _generation;
    private CancellationTokenSource? _warmTimeout;
    private bool _disposed, _cancelled, _warmReady;

    public SolAiService()
    {
        try
        {
            if (File.Exists(CatalogPath) && new FileInfo(CatalogPath).Length < 2_000_000)
                Models = (JsonSerializer.Deserialize<List<AiModel>>(File.ReadAllText(CatalogPath), Json) ?? [])
                    .OfType<AiModel>().Where(m => !string.IsNullOrEmpty(m.Id))
                    .Select(m => (Model: m, Profile: SolAiProfiles.All.FirstOrDefault(p => m.Id.StartsWith(p.Alias + "-", StringComparison.OrdinalIgnoreCase))))
                    .Where(pair => pair.Profile != null && pair.Model.Id.StartsWith(pair.Profile.Alias + "-", StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Model with { Name = pair.Profile!.Name }).ToList();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        try { if(File.Exists(OptimizationPath) && new FileInfo(OptimizationPath).Length<4096) {
            var value=JsonSerializer.Deserialize<AiOptimization>(File.ReadAllText(OptimizationPath),Json);
            if(value!=null && Models.Any(m=>m.Id==value.Id && m.Cached))Optimization=value;
        }} catch(Exception e) when(e is JsonException or IOException or UnauthorizedAccessException) { }
    }

    private static (long Total, long Available, long Commit) Memory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? ((long)(status.TotalPhysical / (1024 * 1024)), (long)(status.AvailablePhysical / (1024 * 1024)), (long)(status.AvailablePageFile / (1024 * 1024))) : (0, 0, 0);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    private bool Fits(AiModel model)
    {
        var memory = Memory();
        if (_warm != null && _warmId == model.Id)
        {
            try { _warm.Refresh(); memory.Available += _warm.WorkingSet64 / (1024 * 1024); memory.Commit += _warm.PrivateMemorySize64 / (1024 * 1024); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        return SolAiProfiles.All.FirstOrDefault(p => p.Name == model.Name) is { } profile
            && SolAiProfiles.Fits(profile, memory.Total, memory.Available, Environment.ProcessorCount, FreeMb, model.SizeMb ?? 0, model.Cached, memory.Commit);
    }
    public bool CanUse(string id) => Available && Models.Any(m => m.Id == id && m.Cached && Fits(m));
    public object State(bool enabled, string selected)
    {
        var memory = Memory();
        var offered = Models.Where(m => Fits(m) || m.Cached).ToArray();
        var recommended = offered.Where(Fits).OrderBy(m => SolAiProfiles.All.Single(p => p.Name == m.Name).Rank).LastOrDefault()?.Id;
        return new {
        available = Available, enabled, selected, recommended,
        machine = new { ramMb = memory.Total, availableMb = memory.Available, threads = Environment.ProcessorCount },
        models = offered.Select(m => new { id = m.Id, name = m.Name, sizeMb = m.SizeMb, license = m.License,
            cached = m.Cached, device = m.Device, usable = Fits(m) }),
        busy = Busy, warm = Warm, phase = Phase, currentId = CurrentId, percent = Percent, error = Error,
        optimization=Optimization==null?null:new{id=Optimization.Id,cpuMs=Optimization.CpuMs,gpuMs=Optimization.GpuMs,status=Optimization.Status},
        check = Check == null ? null : new { id = Check.Id, text = Check.Text, loadMs = Check.LoadMs,
            elapsedMs = Check.ElapsedMs, memoryMb = Check.MemoryMb, device = Check.Device },
        freeMb = FreeMb, cacheDirectory = CacheDirectory,
        };
    }

    public async Task RunAsync(string operation, string? id = null, SolAiRequest? request = null)
    {
        if (_disposed || Busy || !Available || operation is not ("catalog" or "download" or "remove" or "check" or "generate" or "optimize")) return;
        if (operation == "generate" && (request == null || !SolAiText.Valid(request))) return;
        if (operation != "catalog" && !Models.Any(m => m.Id == id)) return;
        if (operation == "generate") { Result = null; Incomplete = false; }
        if (operation is "download" or "check" or "generate" or "optimize" && !Fits(Models.Single(m => m.Id == id)))
        { Error = "machine-capacity"; Changed?.Invoke(); return; }
        if (operation == "generate") { await GenerateWarmAsync(id!, request!); return; }
        if(operation!="catalog")await ReleaseWarmAsync();
        var start = new ProcessStartInfo(Exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new System.Text.UTF8Encoding(false), StandardOutputEncoding = new System.Text.UTF8Encoding(false), WorkingDirectory = AppContext.BaseDirectory,
        };
        start.ArgumentList.Add(operation); start.ArgumentList.Add(DirectoryPath); start.ArgumentList.Add(id ?? (Optimization?.Status=="gpu"?Optimization.Id:""));
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        using var process = new Process { StartInfo = start };
        _process = process; _cancelled = false; Error = null; CurrentId = id; Percent = 0; Phase = "starting";
        if (operation == "check") Check = null;
        if (operation == "generate") { Result = null; Incomplete = false; }
        Changed?.Invoke();
        var received = false;
        try
        {
            process.Start();
            if (request != null) { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request)); await process.StandardInput.FlushAsync(); }
            // Le SDK écrit parfois des diagnostics sur stderr ; les vider sans conserver de données.
            var errors = Task.Run(async () => { while (await process.StandardError.ReadLineAsync() != null) { } });
            // Bornage côté hôte aussi, même si le code natif ne répond plus à son token.
            using var timeout = new CancellationTokenSource(operation is "download" or "optimize" ? TimeSpan.FromHours(2) : TimeSpan.FromMinutes(4));
            using var killer = timeout.Token.Register(() => Kill(process));
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (!line.StartsWith("@sol-ai:", StringComparison.Ordinal) || line.Length > 500_000) continue;
                if (operation == "generate") timeout.CancelAfter(TimeSpan.FromMinutes(4));
                using var document = JsonDocument.Parse(line[8..]);
                var message = document.RootElement;
                switch (message.GetProperty("kind").GetString())
                {
                    case "phase": Phase = message.GetProperty("phase").GetString() ?? "starting"; break;
                    case "progress": Percent = Math.Clamp(message.GetProperty("percent").GetInt32(), 0, 100); break;
                    case "catalog":
                        Models = (JsonSerializer.Deserialize<List<AiModel>>(message.GetProperty("models"), Json) ?? [])
                            .Where(m => SolAiProfiles.All.Any(p => p.Name == m.Name)).ToList();
                        SaveCatalog(); received = true; break;
                    case "installed" or "removed":
                        var key = message.GetProperty("id").GetString();
                        Models = Models.Select(m => m.Id == key ? m with { Cached = message.GetProperty("kind").GetString() == "installed" } : m).ToList();
                        SaveCatalog(); received = true; break;
                    case "checked": Check = JsonSerializer.Deserialize<AiCheck>(message, Json); received = true; break;
                    case "optimized":
                        var optimized=JsonSerializer.Deserialize<AiOptimization>(message,Json);
                        if(optimized!=null && Models.Any(m=>m.Id==optimized.Id && m.Cached)) {Optimization=optimized;JsonFileStore.Save(OptimizationPath,optimized);received=true;}
                        break;
                    case "generated": Result = message.GetProperty("text").GetString(); Incomplete = message.GetProperty("incomplete").GetBoolean(); received = true; break;
                    case "error": Error = message.GetProperty("code").GetString(); received = true; break;
                    case "cancelled": _cancelled = true; received = true; break;
                }
                if (!_disposed) Changed?.Invoke();
            }
            await process.WaitForExitAsync();
            await errors;
            if (!_cancelled && Error == null && (!received || process.ExitCode != 0)) Error = "module-stopped";
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException)
        { if (!_cancelled) Error = "module-stopped"; Kill(process); }
        finally
        {
            _process = null; if (_cancelled) { Result = null; Incomplete = false; } Phase = _cancelled ? "cancelled" : Warm ? "ready" : "idle";
            if (!_disposed) Changed?.Invoke();
        }
    }

    private void SaveCatalog()
    {
        if (!JsonFileStore.Save(CatalogPath, Models)) Error = "catalog-save-failed";
    }

    public void Prepare(string id)
    {
        if(_disposed || _warm != null || Busy || !CanUse(id))return;
        try {Phase="loading";StartWarm(id,"warm");Changed?.Invoke();}
        catch(Exception e) when(e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) {Phase="idle";}
    }
    private void StartWarm(string id,string operation)
    {
        var start = new ProcessStartInfo(Exe) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(false), StandardOutputEncoding = new System.Text.UTF8Encoding(false), WorkingDirectory = AppContext.BaseDirectory };
        foreach (var arg in new[] { operation, DirectoryPath, id, Environment.ProcessId.ToString() }) start.ArgumentList.Add(arg);
        start.Environment["SOL_AI_KEEP_ALIVE"] = "1";start.Environment["SOL_AI_IDLE_MINUTES"] = KeepMinutes.ToString();
        var process = new Process { StartInfo = start };
        try {process.Start();}catch {process.Dispose();throw;}
        _warm=process;_warmId=id;CurrentId=id;_warmReady=false;
        _ = ReadWarmAsync(process);_ = ReleaseUnderPressureAsync(process);
    }

    private async Task GenerateWarmAsync(string id, SolAiRequest request)
    {
        if (_warm != null && _warmId != id) await ReleaseWarmAsync();
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _generation = completion; _cancelled = false; Error = null; Result = null; Partial = null; Incomplete = false;
        Percent = 0; CurrentId = id; Phase = Warm ? request.Thinking ? "thinking" : "generating" : "starting";
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        _warmTimeout = timeout;
        try
        {
            if (_warm == null)
                StartWarm(id,"generate");
            var process = _warm!;
            using var killer = timeout.Token.Register(() => Kill(process));
            Changed?.Invoke();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
            await process.StandardInput.FlushAsync();
            var result = await completion.Task;
            Result = result.GetProperty("text").GetString(); Incomplete = result.GetProperty("incomplete").GetBoolean();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { if (!_cancelled) Error = e.Message is "memory-limit" or "module-busy" or "empty-response" or "reasoning-incomplete" or "context-too-large" ? e.Message : "module-stopped"; await ReleaseWarmAsync(); }
        finally
        {
            _warmTimeout = null; _generation = null;
            Partial = null;
            if (_cancelled) { Result = null; Incomplete = false; }
            Phase = _cancelled ? "cancelled" : Warm ? "ready" : "idle";
            if (!_disposed) Changed?.Invoke();
        }
    }

    private async Task ReadWarmAsync(Process process)
    {
        var errors = DrainErrorsAsync(process);
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (_warm != process || !line.StartsWith("@sol-ai:", StringComparison.Ordinal) || line.Length > 500_000) continue;
                using var document = JsonDocument.Parse(line[8..]); var message = document.RootElement;
                if (_generation != null) _warmTimeout?.CancelAfter(TimeSpan.FromMinutes(4));
                switch (message.GetProperty("kind").GetString())
                {
                    case "phase": Phase = message.GetProperty("phase").GetString() ?? "starting"; if(Phase is "ready" or "thinking" or "generating")_warmReady=true; break;
                    case "progress": Percent = Math.Clamp(message.GetProperty("percent").GetInt32(), 0, 100); break;
                    case "partial": if (_generation != null && message.GetProperty("text").GetString() is { Length: <= 50_000 } text) Partial = text; break;
                    case "device":
                        if(message.TryGetProperty("fallback",out var fallback) && fallback.GetBoolean() && message.TryGetProperty("id",out var actual) && actual.GetString() is {} actualId && Models.Any(m=>m.Id==actualId && m.Cached)) {
                            _warmId=actualId;CurrentId=actualId;Optimization=new(actualId,0,0,"gpu-unavailable");JsonFileStore.Save(OptimizationPath,Optimization);
                        }
                        break;
                    case "generated": _generation?.TrySetResult(message.Clone()); break;
                    case "error": _generation?.TrySetException(new InvalidOperationException(message.GetProperty("code").GetString())); break;
                    case "cancelled": _generation?.TrySetException(new InvalidOperationException("cancelled")); break;
                }
                if (!_disposed) Changed?.Invoke();
            }
            await process.WaitForExitAsync(); await errors;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or JsonException) { Kill(process); }
        finally
        {
            if (_warm == process) { _warm = null; _warmId = null; _warmReady=false; _generation?.TrySetException(new InvalidOperationException("module-stopped")); if (!Busy) Phase = "idle"; if (!_disposed) Changed?.Invoke(); }
            process.Dispose();
        }
    }
    private static async Task DrainErrorsAsync(Process process) { while (await process.StandardError.ReadLineAsync() != null) { } }
    private async Task ReleaseUnderPressureAsync(Process process)
    {
        while (!_disposed && _warm == process)
        {
            await Task.Delay(30_000);
            var memory = Memory();
            if (_warm == process && !Busy && (memory.Available < 512 || memory.Commit < 512)) { await ReleaseWarmAsync(); Changed?.Invoke(); }
        }
    }
    private async Task ReleaseWarmAsync()
    {
        if (_warm is not { } process) return;
        _warm = null; _warmId = null; _warmReady=false; Kill(process);
        try { await process.WaitForExitAsync(); } catch (InvalidOperationException) { }
    }
    public void Release() { if (_warm is { } process) Kill(process); }

    public void Cancel()
    {
        var process = _process ?? (_generation != null ? _warm : null);
        if (process == null) return;
        _cancelled = true; Phase = "cancelling"; Changed?.Invoke();
        try { process.StandardInput.WriteLine("cancel"); process.StandardInput.Flush(); }
        catch (Exception e) when (e is IOException or InvalidOperationException) { }
        _ = Task.Run(async () => { await Task.Delay(2500); Kill(process); });
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    public void Dispose() { _disposed = true; if (_process is { } process) Kill(process); Release(); }
}
