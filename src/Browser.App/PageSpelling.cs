using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;

namespace Browser.App;

public sealed partial class PageHost
{
    private readonly WindowsSpelling _spelling = new();
    private readonly DispatcherTimer _spellingTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _spellingStarted, _spellingBusy;
    private readonly Dictionary<string, (string Text, int Mode, WindowsSpelling.Error[] Errors)> _spellingCache = new();
    public string[] SpellingLanguages => _spelling.Languages;
    public string SpellingAccent { get; set; } = "#6495ed";
    public void StopSpelling() => _spellingTimer.Stop();

    private void StartSpelling()
    {
        if (_spellingStarted) return;
        _spellingStarted = true;
        _spellingTimer.Tick += async (_, _) =>
        {
            if (_spellingBusy) return;
            _spellingBusy = true;
            try
            {
                foreach (var id in _visible.ToArray())
                {
                    if (Core(id) is not { } core || !core.Source.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                    await core.ExecuteScriptAsync(SpellingScript);
                    var enabled = settings.Spelling != 0 && SpellingLanguages.Any(l => settings.Spelling == 3 || l.StartsWith(settings.Spelling == 1 ? "fr" : "en"));
                    if (!enabled)
                    {
                        await core.ExecuteScriptAsync("window.__solSpelling?.clear()");
                        _spellingCache.Remove(id);
                        continue;
                    }
                    var json = await core.ExecuteScriptAsync("window.__solSpelling.read()");
                    using var snapshot = JsonDocument.Parse(json);
                    if (snapshot.RootElement.ValueKind != JsonValueKind.Object) { _spellingCache.Remove(id); continue; }
                    if (!snapshot.RootElement.TryGetProperty("text", out var textValue) || textValue.ValueKind != JsonValueKind.String) continue;
                    var text = textValue.GetString() ?? "";
                    if (text.Length > 8000) continue;
                    if (!_spellingCache.TryGetValue(id, out var previous) || previous.Text != text || previous.Mode != settings.Spelling)
                        _spellingCache[id] = (text, settings.Spelling, _spelling.Check(text, settings.Spelling));
                    var errors = _spellingCache[id].Errors.Select(e => new[] { e.Start, e.Length });
                    await core.ExecuteScriptAsync($"window.__solSpelling.paint({JsonSerializer.Serialize(text)}, {JsonSerializer.Serialize(errors)}, {JsonSerializer.Serialize(SpellingAccent)})");
                }
            }
            catch (Exception e) when (e is COMException or InvalidOperationException or JsonException) { /* vue fermée ou page remplacée */ }
            finally { _spellingBusy = false; }
        };
        _spellingTimer.Start();
    }

    public async Task<(string Word, string[] Suggestions)> SpellingSuggestions(string id)
    {
        if (settings.Spelling == 0 || Core(id) is not { } core) return ("", []);
        try
        {
            using var doc = JsonDocument.Parse(await core.ExecuteScriptAsync("window.__solSpelling?.word() ?? null"));
            if (doc.RootElement.ValueKind != JsonValueKind.String) return ("", []);
            var word = doc.RootElement.GetString()!;
            return _spelling.Check(word, settings.Spelling).Length == 0 ? ("", []) : (word, _spelling.Suggest(word, settings.Spelling));
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or JsonException) { return ("", []); }
    }
    public async void CorrectSpelling(string id, string original, string replacement)
    {
        try
        {
            if (Core(id) is { } core)
                await core.ExecuteScriptAsync($"window.__solSpelling?.replace({JsonSerializer.Serialize(original)}, {JsonSerializer.Serialize(replacement)})");
        }
        catch (Exception e) when (e is COMException or InvalidOperationException) { /* onglet fermé */ }
    }

    // ponytail: champ actif du document principal, 8 000 caractères ; garder la correction native dans les iframes et les éditeurs spécialisés.
    private const string SpellingScript = """
        (() => {
          if (window.__solSpelling) return;
          const host = document.createElement('div');
          host.style.cssText = 'position:fixed;inset:0;pointer-events:none;z-index:2147483647';
          const shadow = host.attachShadow({mode:'closed'});
          const layer = document.createElement('div'); shadow.append(layer);
          let field, originalAttribute, selected;
          const value = el => el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement ? el.value : el.textContent || '';
          const clear = () => {
            layer.replaceChildren(); host.remove();
            if (field) { if (originalAttribute === null) field.removeAttribute('spellcheck'); else field.setAttribute('spellcheck', originalAttribute); }
            field = undefined;
          };
          const eligible = el => el && el !== document.body && !el.readOnly && !el.disabled && el.spellcheck !== false && !el.closest('[spellcheck="false"]') &&
            !(el.autocomplete && /password|credit-card|cc-|one-time-code/i.test(el.autocomplete)) &&
            (el instanceof HTMLTextAreaElement || el instanceof HTMLInputElement && ['text','search'].includes(el.type) || el.isContentEditable);
          const nodes = () => { const out=[]; const walker=document.createTreeWalker(field,NodeFilter.SHOW_TEXT); let n; while(n=walker.nextNode())out.push(n);return out; };
          const range = (start, length) => {
            const r=document.createRange();let offset=0, begun=false;
            for(const n of nodes()){const end=offset+n.length;
              if(!begun && start<end){r.setStart(n,start-offset);begun=true;}
              if(begun && start+length<=end){r.setEnd(n,start+length-offset);return r;} offset=end;}
          };
          const read = () => {
            let el=document.activeElement; while(el?.shadowRoot?.activeElement)el=el.shadowRoot.activeElement;
            if (el === field && document.hasFocus() && value(field).length<=8000) return {text:value(field)};
            clear();
            if(!document.hasFocus() || !eligible(el) || value(el).length>8000)return null;
            field=el; originalAttribute=el.getAttribute('spellcheck'); el.spellcheck=false;
            return {text:value(el)};
          };
          const paint = (text, errors, accent) => {
            layer.replaceChildren();
            if(!field || !field.isConnected || value(field)!==text)return;
            host.dataset.solSpelling = String(errors.length);
            if(!host.isConnected)document.documentElement.append(host);
            const box=field.getBoundingClientRect(), css=getComputedStyle(field);
            if(field instanceof HTMLInputElement || field instanceof HTMLTextAreaElement){
              const clip=document.createElement('div');clip.style.cssText=`position:fixed;left:${box.left}px;top:${box.top}px;width:${box.width}px;height:${box.height}px;overflow:hidden;border-radius:${css.borderRadius};`;
              const mirror=document.createElement('div');
              for(const key of ['font','letterSpacing','lineHeight','padding','borderWidth','borderStyle','boxSizing','textAlign','textIndent','direction','wordSpacing','tabSize'])mirror.style[key]=css[key];
              mirror.style.borderColor='transparent'; mirror.style.color='transparent';
              mirror.style.width=box.width+'px';mirror.style.minHeight=box.height+'px';
              mirror.style.whiteSpace=field instanceof HTMLInputElement ? 'pre' : 'pre-wrap';mirror.style.overflowWrap='break-word';
              mirror.style.transform=`translate(${-field.scrollLeft}px,${-field.scrollTop}px)`;
              let offset=0;
              for(const [start,length] of errors){mirror.append(document.createTextNode(text.slice(offset,start)));const mark=document.createElement('span');mark.textContent=text.slice(start,start+length);mark.style.textDecoration=`underline wavy ${accent}`;mark.style.textUnderlineOffset='3px';mirror.append(mark);offset=start+length;}
              mirror.append(document.createTextNode(text.slice(offset)+'\u200b'));clip.append(mirror);layer.append(clip);
            }else{
              for(const [start,length] of errors){const r=range(start,length);if(!r)continue;
                for(const rect of r.getClientRects()){if(rect.bottom<box.top || rect.top>box.bottom)continue;const mark=document.createElement('div');
                  mark.style.cssText=`position:fixed;left:${Math.max(rect.left,box.left)}px;top:${Math.min(rect.bottom,box.bottom)-2}px;width:${Math.max(0,Math.min(rect.right,box.right)-Math.max(rect.left,box.left))}px;height:3px;background:radial-gradient(circle at 2px 0,transparent 1px,${accent} 1px,${accent} 2px,transparent 2px) 0 0/4px 3px repeat-x`;layer.append(mark);}
              }
            }
          };
          document.addEventListener('input',e=>{if(field && e.composedPath().includes(field)){layer.replaceChildren();host.remove();}},true);
          document.addEventListener('contextmenu',e=>{
            selected=undefined;
            if(!field || !(e.composedPath().includes(field) || field.contains(e.target)))return;
            const text=value(field);let caret=field.selectionStart;
            if(caret===undefined){const hit=document.caretRangeFromPoint(e.clientX,e.clientY);if(!hit)return;const r=document.createRange();r.selectNodeContents(field);r.setEnd(hit.startContainer,hit.startOffset);caret=r.toString().length;}
            const words=[...text.matchAll(/[\p{L}\p{M}]+(?:['’][\p{L}\p{M}]+)*/gu)];
            const word=words.find(m=>caret>=m.index && caret<=m.index+m[0].length);
            selected=word ? {field,text,start:word.index,length:word[0].length,word:word[0],at:performance.now()} : undefined;
          },true);
          const replace = (original,replacement) => {
            const s=selected;if(!s || !s.field.isConnected || value(s.field)!==s.text || s.word!==original)return false;
            s.field.focus();
            if(s.field instanceof HTMLInputElement || s.field instanceof HTMLTextAreaElement)s.field.setSelectionRange(s.start,s.start+s.length);
            else {field=s.field;originalAttribute=field.getAttribute('spellcheck');field.spellcheck=false;const r=range(s.start,s.length);if(!r)return false;getSelection().removeAllRanges();getSelection().addRange(r);}
            return document.execCommand('insertText',false,replacement);
          };
          window.__solSpelling={read,paint,clear,word:()=>selected && performance.now()-selected.at<1500 ? selected.word : null,replace};
        })()
        """;
}
