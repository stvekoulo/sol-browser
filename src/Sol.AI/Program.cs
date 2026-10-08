using System.Diagnostics;
using System.Text.Json;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging.Abstractions;
using Browser.Core;
using System.Threading.Channels;

Console.InputEncoding = new System.Text.UTF8Encoding(false);
Console.OutputEncoding = new System.Text.UTF8Encoding(false);

// Génération : modèle conservé pendant la durée choisie, sans serveur ni conversation.
// Entrées uniquement du processus Sol ; la sortie préfixée ignore les diagnostics natifs du SDK.
if (args.Length is < 2 or > 4 || args[0] is not ("catalog" or "download" or "remove" or "check" or "generate" or "optimize" or "warm")) return 2;
var operation = args[0];
var directory = Path.GetFullPath(args[1]);
var modelId = args.ElementAtOrDefault(2) ?? "";
var parentId = int.TryParse(args.ElementAtOrDefault(3), out var pid) ? pid : 0;
// Le texte arrive par stdin, jamais dans une ligne de commande, un fichier temporaire ou un journal.
SolAiRequest? request = null;
if (operation == "generate")
{
    var line = await Console.In.ReadLineAsync();
    if (line is null || line.Length > 500_000) return 2;
    try { request = JsonSerializer.Deserialize<SolAiRequest>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); } catch (JsonException) { return 2; }
    if (request == null || !SolAiText.Valid(request)) return 2;
}
using var cancellation = new CancellationTokenSource(operation is "download" or "optimize" ? TimeSpan.FromHours(2) : TimeSpan.FromMinutes(3));
var ct = cancellation.Token;
var keepAlive = operation is "generate" or "warm" && Environment.GetEnvironmentVariable("SOL_AI_KEEP_ALIVE") == "1";
var keepMinutes = int.TryParse(Environment.GetEnvironmentVariable("SOL_AI_IDLE_MINUTES"), out var duration) && duration is >= 0 and <= 10080 ? duration : 45;
var waiting = false;
var requests = Channel.CreateBounded<SolAiRequest>(1);
_ = Task.Run(async () =>
{
    while (await Console.In.ReadLineAsync() is { } line)
        if (line == "cancel") cancellation.Cancel();
        else if (keepAlive && line.StartsWith("keep:") && int.TryParse(line[5..], out var minutes) && minutes is >= 0 and <= 10080)
        { keepMinutes = minutes; if (waiting) cancellation.CancelAfter(minutes == 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromMinutes(minutes)); }
        else if (keepAlive && line.Length <= 500_000)
        {
            try { var next = JsonSerializer.Deserialize<SolAiRequest>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (next != null && SolAiText.Valid(next)) requests.Writer.TryWrite(next); }
            catch (JsonException) { }
        }
    cancellation.Cancel(); // le navigateur a fermé le canal
});
if (parentId != 0) _ = Task.Run(async () =>
{
    try { using var parent = Process.GetProcessById(parentId); await parent.WaitForExitAsync(ct); Environment.Exit(3); }
    catch (ArgumentException) { Environment.Exit(3); }
    catch (OperationCanceledException) { }
});
var writeLock = new object();
void Publish(object message)
{
    lock (writeLock) { Console.WriteLine("@sol-ai:" + JsonSerializer.Serialize(message)); Console.Out.Flush(); }
}

FoundryLocalManager? manager = null;
FileStream? operationLock = null;
try
{
    Directory.CreateDirectory(directory);
    // Shared readers allow other windows to generate; mutations require all loaded models to be released.
    var mutates = operation is "download" or "remove" or "optimize";
    operationLock = new FileStream(Path.Combine(directory, "module.lock"), FileMode.OpenOrCreate,
        mutates ? FileAccess.ReadWrite : FileAccess.Read, mutates ? FileShare.None : FileShare.Read);
    Publish(new { kind = "phase", phase = "starting" });
    await FoundryLocalManager.CreateAsync(new Configuration
    {
        AppName = "SolAI", AppDataDir = directory, ModelCacheDir = Path.Combine(directory, "models"),
        LogsDir = Path.Combine(directory, "logs"), DisableNonessentialTelemetry = true, LogLevel = LogLevel.Fatal,
        // Contrôle de régression : le catalogue ne peut joindre aucun serveur distant dans cet essai.
        CatalogUrls = Environment.GetEnvironmentVariable("SOL_AI_OFFLINE_TEST") == "1"
            ? [("http://127.0.0.1:9", "")] : null,
    }, NullLogger.Instance, ct);
    manager = FoundryLocalManager.Instance;
    var wantsGpu=modelId.Contains("-gpu:",StringComparison.OrdinalIgnoreCase);
    if(wantsGpu || operation=="optimize") {
        try { await manager.DownloadAndRegisterEpsAsync(new[]{"WebGpuExecutionProvider"},ct); }
        catch(Exception e) when(e is InvalidOperationException or FoundryLocalException) { }
    }
    // Register before obtaining the hardware-filtered catalog; cached GPU use reuses that provider.
    var catalog = await manager.GetCatalogAsync(ct);
    async Task PublishCatalog()
    {
        var models = await catalog.ListModelsAsync(ct);
        var cached = (await catalog.GetCachedModelsAsync(ct)).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var available = models.SelectMany(m => m.Variants).DistinctBy(m => m.Id)
            .Where(m => m.Info.Task == "chat-completion" && (m.Info.Runtime?.DeviceType == DeviceType.CPU || cached.Contains(m.Id)))
            .OrderBy(m => m.Info.FileSizeMb ?? int.MaxValue)
            .Where(m => SolAiProfiles.All.Any(p => p.Alias == m.Alias))
            .Select(m => new { id = m.Id, name = SolAiProfiles.All.Single(p => p.Alias == m.Alias).Name,
                sizeMb = m.Info.FileSizeMb, license = m.Info.License ?? "", cached = cached.Contains(m.Id), device = m.Info.Runtime?.DeviceType == DeviceType.GPU ? "GPU" : "CPU" });
        Publish(new { kind = "catalog", models = available.ToArray() });
    }
    if (operation == "catalog") await PublishCatalog();
    else if (operation == "optimize")
    {
        var cpu = (await catalog.GetCachedModelsAsync(ct)).FirstOrDefault(m => SolAiProfiles.All.Any(p=>p.Alias==m.Alias) && m.Info.Runtime?.DeviceType == DeviceType.CPU)
            ?? throw new InvalidOperationException("not-installed");
        Publish(new {kind="phase",phase="optimizing"});
        var ep=manager.DiscoverEps().FirstOrDefault(e=>e.Name.Equals("WebGpuExecutionProvider",StringComparison.OrdinalIgnoreCase));
        long cpuMs=0, gpuMs=0; var preferred=cpu.Id; var status="gpu-unavailable";
        if (ep != null)
        {
            var registered=await manager.DownloadAndRegisterEpsAsync(new[]{ep.Name},(name,percent)=>Publish(new{kind="progress",percent=(int)percent}),ct);
            if (registered.Success)
            {
                var gpu=(await catalog.ListModelsAsync(ct)).FirstOrDefault(m=>m.Alias==cpu.Alias)?.Variants.FirstOrDefault(m=>m.Info.Runtime?.DeviceType==DeviceType.GPU && m.Info.Runtime.ExecutionProvider.Equals("WebGPUExecutionProvider",StringComparison.OrdinalIgnoreCase));
                if (gpu != null)
                {
                    if (!await gpu.IsCachedAsync(ct)) {
                        if (gpu.Info.FileSizeMb is not > 0 || new DriveInfo(Path.GetPathRoot(directory)!).AvailableFreeSpace/(1024*1024) < gpu.Info.FileSizeMb.Value*2L+512) throw new IOException("disk-space");
                        Publish(new{kind="phase",phase="downloading"});
                        await gpu.DownloadAsync(p=>Publish(new{kind="progress",percent=(int)p}),ct);
                    }
                    Publish(new{kind="phase",phase="optimizing"});
                    async Task<long> Measure(IModel variant, string provider) {
                        using var engine=new NativeText(await variant.GetPathAsync(ct),variant.Alias,provider);
                        var timer=Stopwatch.StartNew();
                        var result=await Task.Run(()=>engine.Complete(new("translate","Hello, the meeting starts tomorrow at ten.","fr",Thinking:false),ct,_=>cancellation.CancelAfter(TimeSpan.FromMinutes(3)),deterministic:true),ct);
                        if(result.Incomplete || !result.Text.Contains("demain",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("gpu-check-failed");
                        return timer.ElapsedMilliseconds;
                    }
                    cpuMs=await Measure(cpu,"");
                    try { gpuMs=await Measure(gpu,"WebGPU"); if(gpuMs < cpuMs*0.8) {preferred=gpu.Id;status="gpu";} else status="cpu-faster"; }
                    catch(Exception e) when(e is InvalidOperationException or OutOfMemoryException) {status="gpu-unavailable";}
                    // Keep the CPU fallback; discard a GPU variant that did not improve this machine.
                    if(preferred==cpu.Id)await gpu.RemoveFromCacheAsync(ct);
                }
            }
        }
        await PublishCatalog();
        Publish(new{kind="optimized",id=preferred,cpuMs,gpuMs,status});
    }
    else
    {
        var model = operation == "download" ? await catalog.GetModelVariantAsync(modelId, ct)
            : (await catalog.GetCachedModelsAsync(ct)).FirstOrDefault(m => m.Id == modelId);
        if(model==null && wantsGpu && operation is "generate" or "check" or "warm") {
            model=(await catalog.GetCachedModelsAsync(ct)).FirstOrDefault(m=>m.Info.Runtime?.DeviceType==DeviceType.CPU && SolAiProfiles.All.Any(p=>p.Alias==m.Alias && modelId.StartsWith(p.Alias+"-",StringComparison.Ordinal)));
            if(model!=null)Publish(new{kind="device",device="CPU",fallback=true,id=model.Id});
        }
        if (model == null) throw new InvalidOperationException("model-unavailable");
        if (model.Info.Task != "chat-completion" || model.Info.Runtime?.DeviceType is not (DeviceType.CPU or DeviceType.GPU))
            throw new InvalidOperationException("model-unavailable");
        if (!SolAiProfiles.All.Any(p => p.Alias == model.Alias)) throw new InvalidOperationException("model-unavailable");
        switch (operation)
        {
            case "download":
                if (model.Info.FileSizeMb is not > 0) throw new InvalidOperationException("size-unknown");
                var free = new DriveInfo(Path.GetPathRoot(directory)!).AvailableFreeSpace;
                // Une marge pour les fichiers temporaires ; c'est une estimation, pas la taille installée.
                if (free < (model.Info.FileSizeMb.Value * 2L + 512) * 1024 * 1024) throw new IOException("disk-space");
                Publish(new { kind = "phase", phase = "downloading" });
                var last = -1;
                await model.DownloadAsync(percent =>
                {
                    var rounded = (int)Math.Clamp(percent, 0, 100);
                    if (rounded != last) { last = rounded; Publish(new { kind = "progress", percent = rounded }); }
                }, ct);
                if (!await model.IsCachedAsync(ct)) throw new IOException("download-incomplete");
                Publish(new { kind = "installed", id = model.Id });
                break;
            case "remove":
                Publish(new { kind = "phase", phase = "removing" });
                foreach(var variant in (await catalog.GetCachedModelsAsync(ct)).Where(m=>m.Alias==model.Alias))await variant.RemoveFromCacheAsync(ct);
                await PublishCatalog();
                Publish(new { kind = "removed", id = model.Id });
                break;
            case "check":
            case "generate":
            case "warm":
            {
                if (!await model.IsCachedAsync(ct)) throw new InvalidOperationException("not-installed");
                Publish(new { kind = "phase", phase = "loading" });
                var watch = Stopwatch.StartNew();
                var device=model.Info.Runtime?.DeviceType == DeviceType.GPU ? "GPU" : "CPU";
                NativeText native;
                async Task<NativeText> CpuFallback() {
                    var cpu=(await catalog.GetCachedModelsAsync(ct)).FirstOrDefault(m=>m.Alias==model.Alias && m.Info.Runtime?.DeviceType==DeviceType.CPU)
                        ?? throw new InvalidOperationException("not-installed");
                    device="CPU";Publish(new{kind="device",device,fallback=true,id=cpu.Id});
                    return new NativeText(await cpu.GetPathAsync(ct),cpu.Alias);
                }
                try {
                    if(device=="GPU")await manager.DownloadAndRegisterEpsAsync(new[]{"WebGpuExecutionProvider"},ct);
                    native=new NativeText(await model.GetPathAsync(ct),model.Alias,device=="GPU"?"WebGPU":"");
                } catch(Exception e) when(device=="GPU" && e is (InvalidOperationException or OutOfMemoryException)) {native=await CpuFallback();}
                Publish(new{kind="device",device,fallback=device=="CPU" && model.Info.Runtime?.DeviceType==DeviceType.GPU});
                try {
                var loadMs = watch.ElapsedMilliseconds;
                if(operation=="warm") {
                    waiting=true;cancellation.CancelAfter(keepMinutes==0?Timeout.InfiniteTimeSpan:TimeSpan.FromMinutes(keepMinutes));
                    Publish(new{kind="phase",phase="ready"});request=await requests.Reader.ReadAsync(ct);waiting=false;watch.Restart();loadMs=0;
                }
                do
                {
                Publish(new { kind = "phase", phase = operation == "check" ? "checking" : request?.Thinking == false ? "generating" : "thinking" });
                // Un essai fixe, sans saisie ni contenu de navigation. Aucune conversation conservée.
                var incomplete = false;
                async Task<string> Complete(SolAiRequest part, bool visible = false, string prefix = "")
                {
                    cancellation.CancelAfter(TimeSpan.FromMinutes(3));
                    var heartbeat = Stopwatch.StartNew();
                    string? lastPhase=null;
                    void Activity(string phase) {
                        cancellation.CancelAfter(TimeSpan.FromMinutes(3));
                        if(lastPhase!=phase || heartbeat.ElapsedMilliseconds>1000) {Publish(new{kind="phase",phase});heartbeat.Restart();lastPhase=phase;}
                    }
                    Action<string>? Output=visible?value=>{if(prefix.Length+value.Length<=50_000)Publish(new{kind="partial",text=prefix+value});}:null;
                    (string Text,bool Incomplete) result;
                    try { result = await Task.Run(() => native.Complete(part, ct, Activity, Output), ct); }
                    catch(Exception e) when(device=="GPU" && e is (InvalidOperationException or OutOfMemoryException)) {
                        native.Dispose();native=await CpuFallback();
                        if(visible)Publish(new{kind="partial",text=prefix});
                        result=await Task.Run(()=>native.Complete(part,ct,Activity,Output),ct);
                    }
                    incomplete |= result.Incomplete; return result.Text;
                }
                async Task<string> Condense(string source, SolAiRequest basis)
                {
                    while (source.Length > 2000)
                    {
                        var previousLength = source.Length;
                        var parts = new List<string>();
                        foreach (var part in SolAiText.Passages(source)) parts.Add(await Complete(basis with { Action = "summary", Text = part, Instruction = "" }));
                        source = string.Join("\n\n", parts);
                        if (source.Length >= previousLength) throw new InvalidOperationException("context-too-large");
                    }
                    return source;
                }
                string text;
                if (request == null)
                {
                    text = await Complete(new("write", "", "en", "Reply only with the numeric result of 2 + 2."));
                }
                else if (request.Action == "summary") text = await Complete(request with { Text = await Condense(request.Text, request) }, visible:true);
                else if (request.Action == "write") text = await Complete(request with { Text = await Condense(request.Text, request), Instruction = await Condense(request.Instruction, request) }, visible:true);
                else
                {
                    var parts = SolAiText.Passages(request.Text).ToArray();
                    var results = new List<string>();
                    for (var i = 0; i < parts.Length; i++)
                    {
                        results.Add(await Complete(request with { Text = parts[i] }, visible:true, prefix:results.Count == 0 ? "" : string.Join("\n\n", results)+"\n\n"));
                        Publish(new { kind = "progress", percent = (i + 1) * 100 / parts.Length });
                    }
                    text = string.Join("\n\n", results);
                }
                if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("empty-response");
                if (text.Length > 50_000) throw new InvalidOperationException("response-too-large");
                if (operation is "generate" or "warm") Publish(new { kind = "generated", text, incomplete, loadMs, elapsedMs = watch.ElapsedMilliseconds, memoryMb = Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024), device });
                else Publish(new { kind = "checked", id = model.Id, text = text[..Math.Min(1000, text.Length)],
                    loadMs, elapsedMs = watch.ElapsedMilliseconds, memoryMb = Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024), device });
                if (!keepAlive) break;
                // Retain only model weights; Complete disposed the generator, tokens and request context.
                request = null;
                text = "";
                waiting = true;
                cancellation.CancelAfter(keepMinutes == 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromMinutes(keepMinutes));
                Publish(new { kind = "phase", phase = "ready" });
                request = await requests.Reader.ReadAsync(ct);
                waiting = false;
                cancellation.CancelAfter(TimeSpan.FromMinutes(3));
                watch.Restart(); loadMs = 0;
                } while (true);
                } finally {native.Dispose();}
                break;
            }
        }
    }
    return 0;
}
catch (OperationCanceledException) { Publish(new { kind = "cancelled" }); return 1; }
catch (Exception e)
{
    // Aucun contenu utilisateur dans les journaux/erreurs de l'hôte.
    var code = e is IOException && (e.HResult & 0xFFFF) == 32 ? "module-busy"
        : e is OutOfMemoryException || e.Message.Contains("Could not allocate", StringComparison.Ordinal) ? "memory-limit"
        : e.Message is "disk-space" or "model-unavailable" or "size-unknown" or "not-installed" or "empty-response" or "reasoning-incomplete" or "download-incomplete" or "context-too-large" or "response-too-large"
        ? e.Message : operation == "catalog" ? "catalog-failed" : operation + "-failed";
    Publish(new { kind = "error", code });
    return 1;
}
finally
{
    manager?.Shutdown();
    manager?.Dispose();
    operationLock?.Dispose();
}
