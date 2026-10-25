using System.Speech.Recognition;
using System.Speech.Synthesis;

namespace Browser.App;

// API vocale de bureau Windows (SAPI), compatible avec Sol non empaqueté.
// Les callbacks repassent par le dispatcher de la fenêtre. Aucun fichier audio n'est créé.
public sealed class WindowsVoice(Action<Action> dispatch) : IDisposable
{
    private SpeechRecognitionEngine? _recognizer;
    private SpeechSynthesizer? _speaker;
    private int _generation;
    private long _lastLevel;
    private bool _disposed;
    public string Mode { get; private set; } = "idle";
    public string Text { get; private set; } = "";
    public string? Error { get; private set; }
    public double Level { get; private set; }
    public double Confidence { get; private set; }
    public event Action? Changed;
    public event Action<string>? Recognized;

    public void Listen(string language, Stream? wave = null, bool approvalOnly = false)
    {
        if (_disposed) return;
        Stop(); Text = ""; Error = null; Confidence = 0;
        var generation = _generation;
        try
        {
            var info = SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == language);
            if (info is null) { Fail("language-missing"); return; }
            var engine = _recognizer = new SpeechRecognitionEngine(info);
            if (approvalOnly)
            {
                var answers = language == "fr" ? new[] { "oui", "non", "je confirme", "autoriser", "refuser", "annuler" }
                    : new[] { "yes", "no", "approve", "confirm", "reject", "cancel" };
                engine.LoadGrammar(new Grammar(new GrammarBuilder(new Choices(answers)) { Culture = info.Culture }));
            }
            else engine.LoadGrammar(new DictationGrammar());
            engine.InitialSilenceTimeout = TimeSpan.FromSeconds(12);
            engine.BabbleTimeout = TimeSpan.FromSeconds(30);
            engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(1000);
            engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(1400);
            engine.SpeechHypothesized += (_, e) => Post(generation, () => { Text = e.Result.Text; Changed?.Invoke(); });
            engine.AudioLevelUpdated += (_, e) =>
            {
                var now = Environment.TickCount64;
                if (now - Interlocked.Read(ref _lastLevel) < 80) return;
                Interlocked.Exchange(ref _lastLevel, now);
                Post(generation, () => { Level = e.AudioLevel / 100d; Changed?.Invoke(); });
            };
            engine.RecognizeCompleted += (_, e) => Post(generation, () =>
            {
                ReleaseRecognition(); Level = 0; Mode = "idle";
                if (e.Cancelled) { Changed?.Invoke(); return; }
                if (e.Error is not null) { Fail("recognition"); return; }
                Text = e.Result?.Text ?? "";
                Confidence = e.Result?.Confidence ?? 0;
                if (Text.Length == 0) { Fail("no-speech"); return; }
                if (e.Result!.Confidence < 0.35) { Fail("unclear"); return; }
                Changed?.Invoke(); Recognized?.Invoke(Text);
            });
            if (wave is null) engine.SetInputToDefaultAudioDevice(); else engine.SetInputToWaveStream(wave);
            Mode = "listening"; Changed?.Invoke();
            engine.RecognizeAsync(RecognizeMode.Single);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        { ReleaseRecognition(); Fail("microphone"); }
    }

    public void Speak(string text, string language, Stream? wave = null)
    {
        if (_disposed) return;
        Stop(); Error = null;
        if (string.IsNullOrWhiteSpace(text)) return;
        var generation = _generation;
        try
        {
            var speaker = _speaker = new SpeechSynthesizer();
            var voice = speaker.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == language);
            if (voice is null) { ReleaseSpeaker(); Fail("voice-missing"); return; }
            speaker.SelectVoice(voice.VoiceInfo.Name);
            if (wave is null) speaker.SetOutputToDefaultAudioDevice(); else speaker.SetOutputToWaveStream(wave);
            speaker.VisemeReached += (_, e) => Post(generation, () => { Level = e.Viseme == 0 ? 0 : 0.3 + e.Viseme % 5 * 0.12; Changed?.Invoke(); });
            speaker.SpeakCompleted += (_, e) => Post(generation, () =>
            {
                ReleaseSpeaker(); Mode = "idle"; Level = 0;
                if (e.Error is not null) Fail("speech"); else Changed?.Invoke();
            });
            Mode = "speaking"; Changed?.Invoke(); speaker.SpeakAsync(text);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        { ReleaseSpeaker(); Fail("speech"); }
    }

    public void Stop()
    {
        _generation++;
        ReleaseRecognition(); ReleaseSpeaker();
        Mode = "idle"; Level = 0; Error = null; Changed?.Invoke();
    }
    private void Post(int generation, Action action) => dispatch(() => { if (!_disposed && generation == _generation) action(); });
    private void Fail(string code) { Mode = "error"; Error = code; Level = 0; Changed?.Invoke(); }
    private void ReleaseRecognition()
    {
        var engine = _recognizer; _recognizer = null;
        if (engine is null) return;
        try { engine.RecognizeAsyncCancel(); engine.Dispose(); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }
    private void ReleaseSpeaker()
    {
        var speaker = _speaker; _speaker = null;
        if (speaker is null) return;
        try { speaker.SpeakAsyncCancelAll(); speaker.Dispose(); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; Stop(); }
}
