using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using System.Diagnostics;
using Browser.Core;

// ChatML natif pour les modèles Qwen3/SmolLM3 : pas de Jinja, ni de raisonnement à afficher.
// Un modèle par processus ; le générateur et son cache sont libérés après chaque passage.
internal sealed class NativeText : IDisposable
{
    private IntPtr _model, _tokenizer;
    private readonly HashSet<int> _eos;
    private readonly bool _smol;
    private readonly int _thinkEnd;
    public NativeText(string path, string alias, string provider = "")
    {
        _smol = alias == "smollm3-3b";
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "genai_config.json")));
        var eos = config.RootElement.GetProperty("model").GetProperty("eos_token_id");
        _eos = eos.ValueKind == JsonValueKind.Array ? eos.EnumerateArray().Select(x => x.GetInt32()).ToHashSet() : [eos.GetInt32()];
        NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "onnxruntime.dll"));
        try {
            IntPtr options = 0;
            try {
                Check(OgaCreateConfig(path, out options));
                if (provider.Length > 0) {
                    var configured=config.RootElement.GetProperty("model").GetProperty("decoder").GetProperty("session_options").GetProperty("provider_options")
                        .EnumerateArray().Any(item=>item.EnumerateObject().Any(p=>p.Name.Equals(provider,StringComparison.OrdinalIgnoreCase)));
                    if(!configured) {Check(OgaConfigClearProviders(options));Check(OgaConfigAppendProvider(options, provider));}
                    Check(OgaConfigSetDecoderProviderOptionsHardwareDeviceType(options, provider, "GPU"));
                }
                Check(OgaCreateModelFromConfig(options, out _model));
            } finally { if (options != 0) OgaDestroyConfig(options); }
            Check(OgaCreateTokenizer(_model, out _tokenizer));
            Check(OgaCreateSequences(out var marker));
            try { Check(OgaTokenizerEncode(_tokenizer, "</think>", marker)); _thinkEnd = Marshal.ReadInt32(OgaSequencesGetSequenceData(marker, 0)); }
            finally { OgaDestroySequences(marker); }
        }
        catch { Dispose(); throw; }
    }
    public (string Text, bool Incomplete) Complete(SolAiRequest request, CancellationToken ct, Action<string> activity, Action<string>? partial = null, bool deterministic = false)
    {
        IntPtr sequences=0, parameters=0, generator=0, stream=0;
        try
        {
            ct.ThrowIfCancellationRequested();
            Check(OgaCreateSequences(out sequences));
            // Les données JSON échappent les marqueurs spéciaux ; elles ne peuvent créer un rôle ChatML.
            var instruction = SolAiText.Instruction(request);
            // Format système officiel SmolLM3, rendu sans les méthodes Jinja non prises en charge.
            if (_smol) instruction = "## Metadata\n\nKnowledge Cutoff Date: June 2025\nToday Date: "
                + DateTime.Now.ToString("dd MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
                + "\nReasoning Mode: " + (request.Thinking ? "/think" : "/no_think") + "\n\n## Custom Instructions\n\n" + instruction.Replace("/no_think", "").Replace("/think", "").TrimEnd();
            var example = request.Action == "write" ? "<|im_start|>user\n"
                + SolAiText.Data(new("write", "", request.Language, request.Language == "fr" ? "Invite Sam à déjeuner vendredi." : "Invite Sam to lunch Friday."))
                + "<|im_end|>\n<|im_start|>assistant\n"
                + (request.Language == "fr" ? "Bonjour Sam, serais-tu disponible pour déjeuner ensemble vendredi ?" : "Hi Sam, would you like to have lunch together Friday?") + "<|im_end|>\n" : "";
            var prompt = "<|im_start|>system\n" + instruction + "<|im_end|>\n" + example + "<|im_start|>user\n"
                + SolAiText.Data(request) + "<|im_end|>\n<|im_start|>assistant\n" + (request.Thinking ? "" : "<think>\n\n</think>\n\n");
            Check(OgaTokenizerEncode(_tokenizer, prompt, sequences));
            var input = checked((int)OgaSequencesGetSequenceCount(sequences,0));
            Check(OgaCreateGeneratorParams(_model,out parameters));
            var outputTokens = request.Thinking ? 2816 : 768;
            Check(OgaGeneratorParamsSetSearchNumber(parameters,"max_length",input+outputTokens));
            Check(OgaGeneratorParamsSetSearchNumber(parameters,"temperature",0.6));
            Check(OgaGeneratorParamsSetSearchNumber(parameters,"top_p",0.95));
            Check(OgaGeneratorParamsSetSearchBool(parameters,"do_sample",!deterministic));
            Check(OgaCreateGenerator(_model,parameters,out generator));
            Check(OgaGenerator_AppendTokenSequences(generator,sequences));
            var thinking = request.Thinking;
            var answerStart = request.Thinking ? -1 : input;
            Check(OgaCreateTokenizerStream(_tokenizer, out stream));
            var answer = new StringBuilder(); var update = Stopwatch.StartNew();
            while(!OgaGenerator_IsDone(generator))
            {
                ct.ThrowIfCancellationRequested();
                Check(OgaGenerator_GenerateNextToken(generator));
                var length = checked((int)OgaGenerator_GetSequenceCount(generator, 0));
                var token = Marshal.ReadInt32(OgaGenerator_GetSequenceData(generator, 0), (length - 1) * 4);
                if (token == _thinkEnd) { thinking = false; answerStart = length; answer.Clear(); }
                activity(thinking ? "thinking" : "generating");
                if (token != _thinkEnd && !thinking && !_eos.Contains(token)) {
                    Check(OgaTokenizerStreamDecode(stream, token, out var chunk));
                    answer.Append(Marshal.PtrToStringUTF8(chunk));
                    if (partial != null && answer.Length > 0 && update.ElapsedMilliseconds >= 150) { partial(SolAiText.Clean(answer.ToString())); update.Restart(); }
                }
            }
            var count = checked((int)OgaGenerator_GetSequenceCount(generator,0));
            if(count <= input) throw new InvalidOperationException("empty-response");
            var tokens = OgaGenerator_GetSequenceData(generator,0);
            var incomplete = count-input >= outputTokens && !_eos.Contains(Marshal.ReadInt32(tokens,(count-1)*4));
            if (answerStart < 0 || answerStart >= count) throw new InvalidOperationException("reasoning-incomplete");
            // Decode only final-answer tokens: the tokenizer removes special Think tags itself.
            Check(OgaTokenizerDecode(_tokenizer,IntPtr.Add(tokens,answerStart*4),(nuint)(count-answerStart),out var result));
            try
            {
                var text = SolAiText.Clean(Marshal.PtrToStringUTF8(result));
                if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("empty-response");
                return (text, incomplete);
            }
            finally { OgaDestroyString(result); }
        }
        finally
        {
            if(stream!=0)OgaDestroyTokenizerStream(stream);
            if(generator!=0)OgaDestroyGenerator(generator); if(parameters!=0)OgaDestroyGeneratorParams(parameters);
            if(sequences!=0)OgaDestroySequences(sequences);
        }
    }
    public void Dispose()
    {
        if(_tokenizer!=0) {OgaDestroyTokenizer(_tokenizer);_tokenizer=0;}
        if(_model!=0) {OgaDestroyModel(_model);_model=0;}
    }
    private static void Check(IntPtr error)
    {
        if(error==0)return;
        try { throw new InvalidOperationException(Marshal.PtrToStringUTF8(OgaResultGetError(error))); }
        finally { OgaDestroyResult(error); }
    }
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaCreateModel([MarshalAs(UnmanagedType.LPUTF8Str)]string path,out IntPtr model);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaCreateConfig([MarshalAs(UnmanagedType.LPUTF8Str)]string path,out IntPtr config);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaConfigClearProviders(IntPtr config);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaConfigAppendProvider(IntPtr config,[MarshalAs(UnmanagedType.LPUTF8Str)]string provider);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaConfigSetDecoderProviderOptionsHardwareDeviceType(IntPtr config,[MarshalAs(UnmanagedType.LPUTF8Str)]string provider,[MarshalAs(UnmanagedType.LPUTF8Str)]string type);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaCreateModelFromConfig(IntPtr config,out IntPtr model);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern void OgaDestroyConfig(IntPtr config);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaCreateTokenizerStream(IntPtr tokenizer,out IntPtr stream);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaTokenizerStreamDecode(IntPtr stream,int token,out IntPtr text);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern void OgaDestroyTokenizerStream(IntPtr stream);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaCreateTokenizer(IntPtr model,out IntPtr tokenizer);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaCreateSequences(out IntPtr sequences);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaTokenizerEncode(IntPtr tokenizer,[MarshalAs(UnmanagedType.LPUTF8Str)]string text,IntPtr sequences);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern nuint OgaSequencesGetSequenceCount(IntPtr sequences,nuint index);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaSequencesGetSequenceData(IntPtr sequences,nuint index);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaCreateGeneratorParams(IntPtr model,out IntPtr parameters);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaGeneratorParamsSetSearchNumber(IntPtr parameters,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,double value);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaGeneratorParamsSetSearchBool(IntPtr parameters,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,[MarshalAs(UnmanagedType.I1)]bool value);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaCreateGenerator(IntPtr model,IntPtr parameters,out IntPtr generator);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaGenerator_AppendTokenSequences(IntPtr generator,IntPtr sequences);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)][return:MarshalAs(UnmanagedType.I1)]static extern bool OgaGenerator_IsDone(IntPtr generator);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaGenerator_GenerateNextToken(IntPtr generator);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern nuint OgaGenerator_GetSequenceCount(IntPtr generator,nuint index);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaGenerator_GetSequenceData(IntPtr generator,nuint index);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaTokenizerDecode(IntPtr tokenizer,IntPtr tokens,nuint count,out IntPtr text);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern IntPtr OgaResultGetError(IntPtr error);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern void OgaDestroyResult(IntPtr error);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern void OgaDestroyString(IntPtr text);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern void OgaDestroyModel(IntPtr model);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern void OgaDestroyTokenizer(IntPtr tokenizer);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern void OgaDestroySequences(IntPtr sequences);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern void OgaDestroyGeneratorParams(IntPtr parameters);
[DllImport("onnxruntime-genai",CallingConvention=CallingConvention.StdCall)]static extern void OgaDestroyGenerator(IntPtr generator);

}
