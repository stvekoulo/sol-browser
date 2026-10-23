using System.Collections.Concurrent;
using System.Speech.Synthesis;
using Browser.App;

// Vraies voix et reconnaissance Windows, sans micro ni sortie sonore ni réseau.
using var callbacks = new BlockingCollection<Action>();
using var voice = new WindowsVoice(action => callbacks.Add(action));
using var speaker = new SpeechSynthesizer();
var installed = speaker.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "fr")
    ?? throw new InvalidOperationException("A French desktop voice is required for this check");
speaker.SelectVoice(installed.VoiceInfo.Name);
using var wave = new MemoryStream();
speaker.SetOutputToWaveStream(wave);
speaker.Speak("Bonjour je voudrais ouvrir une nouvelle page.");
speaker.SetOutputToNull(); wave.Position = 0;
var transcript = "";
voice.Recognized += text => transcript = text;
voice.Listen("fr", wave);
var deadline = DateTime.UtcNow.AddSeconds(30);
while (transcript.Length == 0 && voice.Mode != "error" && DateTime.UtcNow < deadline)
    if (callbacks.TryTake(out var next, 200)) next();
if (voice.Error is not null && voice.Error != "unclear" || voice.Text.Length == 0) throw new InvalidOperationException("Recognition failed: " + voice.Error);
if (!voice.Text.Contains("page", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected transcript: " + voice.Text);
if (voice.Error == "unclear" && transcript.Length > 0) throw new InvalidOperationException("Uncertain dictation must not be sent automatically");
if (voice.Error is null && voice.Mode != "idle") throw new InvalidOperationException("Microphone should be idle after one turn");
var recognized = voice.Text;
Console.WriteLine("Recognition confidence: " + voice.Confidence + "; mode: " + voice.Mode + "; error: " + voice.Error);
transcript = ""; wave.Position = 0;
voice.Listen("fr", wave); voice.Stop();
var until = DateTime.UtcNow.AddSeconds(1);
while (DateTime.UtcNow < until) if (callbacks.TryTake(out var next, 100)) next();
if (voice.Mode != "idle" || transcript.Length != 0) throw new InvalidOperationException("A cancelled turn leaked a stale transcript");
using var approvalWave = new MemoryStream();
speaker.SetOutputToWaveStream(approvalWave); speaker.Speak("Oui"); speaker.SetOutputToNull(); approvalWave.Position = 0;
voice.Listen("fr", approvalWave, approvalOnly: true);
deadline = DateTime.UtcNow.AddSeconds(15);
while (transcript.Length == 0 && voice.Mode != "error" && DateTime.UtcNow < deadline) if (callbacks.TryTake(out var next, 200)) next();
if (!transcript.Equals("oui", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Voice approval failed: " + voice.Text + "; " + voice.Error);
using var spoken = new MemoryStream();
voice.Speak("La page est ouverte.", "fr", spoken);
deadline = DateTime.UtcNow.AddSeconds(15);
while (voice.Mode == "speaking" && DateTime.UtcNow < deadline) if (callbacks.TryTake(out var next, 200)) next();
if (voice.Error is not null || voice.Mode != "idle" || spoken.Length <= 44) throw new InvalidOperationException("Voice response synthesis failed");
Console.WriteLine("PASS real Windows French synthesis to WAV and recognition: " + recognized);
Console.WriteLine("PASS restricted spoken approval, spoken response to WAV, cancellation, stale callback rejection, idle after recognition; microphone and real ChatGPT not used");
