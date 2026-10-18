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
    private readonly HashSet<string> _spellingSeen = new(); // onglets où le correcteur de Sol a été posé
    // Un seul aller-retour par passage : le script se pose s'il manque, puis dit quel champ est en cours de saisie.
    private const string SpellingRead = "(window.__solSpelling || " + SpellingScript + ", window.__solSpelling.read())";
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
                var enabled = settings.Spelling != 0 && SpellingLanguages.Any(l => settings.Spelling == 3 || l.StartsWith(settings.Spelling == 1 ? "fr" : "en"));
                foreach (var id in _visible.ToArray())
                {
                    if (Core(id) is not { } core || !core.Source.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!enabled)
                    {
                        // Correcteur coupé : la page est remise en état une fois, puis laissée tranquille.
                        if (_spellingSeen.Remove(id) | _spellingCache.Remove(id)) await core.ExecuteScriptAsync("window.__solSpelling?.clear()");
                        continue;
                    }
                    _spellingSeen.Add(id);
                    var json = await core.ExecuteScriptAsync(SpellingRead);
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
          let field, originalAttribute, selected, scanned = {}, painted;
          const plain = el => el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement;
          // Zone éditable : le texte de ses nœuds, avec un saut de ligne entre deux blocs, à un retour à la ligne et autour
          // d'un élément non modifiable (étiquette, mention). Sans lui, la fin d'un paragraphe et le début du suivant
          // formeraient un seul mot (« pas.Beaucoup »), signalé à tort.
          const scan = el => {
            if (scanned.el === el && scanned.raw === el.textContent && scanned.list.every(s => s.node.isConnected)) return scanned;
            const list = []; let text = '', last = null, broken = false;
            const block = n => { let e = n.parentElement; while (e && e !== el && /^(inline|contents)$/.test(getComputedStyle(e).display)) e = e.parentElement; return e; };
            const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT,
              { acceptNode: n => n.nodeType === 1 && n.isContentEditable === false ? (broken = true, NodeFilter.FILTER_REJECT) : NodeFilter.FILTER_ACCEPT });
            for (let n; n = walker.nextNode();) {
              if (n.nodeType === 1) { if (n.tagName === 'BR') broken = true; continue; }
              if (!n.length) continue;
              const owner = block(n);
              if (list.length && (broken || owner !== last)) text += '\n';
              list.push({ node: n, start: text.length }); text += n.data; last = owner; broken = false;
            }
            return scanned = { el, raw: el.textContent, text, list };
          };
          const value = el => plain(el) ? el.value : scan(el).text;
          // Position du curseur dans ce texte, ou -1 (sélection étendue, curseur hors d'un nœud de texte).
          const caret = () => {
            if (plain(field)) return field.selectionStart === field.selectionEnd ? field.selectionStart : -1;
            const selection = getSelection();
            if (!selection.isCollapsed) return -1;
            const at = scan(field).list.find(s => s.node === selection.focusNode);
            return at ? at.start + selection.focusOffset : -1;
          };
          const hide = () => { painted = undefined; layer.replaceChildren(); host.remove(); };
          const clear = () => {
            painted = undefined; layer.replaceChildren(); host.remove();
            if (field) { if (originalAttribute === null) field.removeAttribute('spellcheck'); else field.setAttribute('spellcheck', originalAttribute); }
            field = undefined;
          };
          const eligible = el => el && el !== document.body && !el.readOnly && !el.disabled && el.spellcheck !== false && !el.closest('[spellcheck="false"]') &&
            !(el.autocomplete && /password|credit-card|cc-|one-time-code/i.test(el.autocomplete)) &&
            (el instanceof HTMLTextAreaElement || el instanceof HTMLInputElement && ['text','search'].includes(el.type) || el.isContentEditable);
          const range = (start, length) => {
            const r=document.createRange();let begun=false;
            for(const s of scan(field).list){const end=s.start+s.node.length;
              if(!begun && start<end){r.setStart(s.node,Math.max(0,start-s.start));begun=true;}
              if(begun && start+length<=end){r.setEnd(s.node,start+length-s.start);return r;}}
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
            if(!field || !field.isConnected || value(field)!==text){hide();return;}
            // Le mot en cours de frappe (curseur juste après lui) n'est pas encore une faute.
            const typing = caret();
            errors = errors.filter(([start,length]) => start+length !== typing);
            const box=field.getBoundingClientRect(), css=getComputedStyle(field);
            // Rien n'a changé depuis le dernier passage (texte, fautes, place du champ) : les traits restent tels quels.
            const state = [text, errors, accent, box.left, box.top, box.width, box.height, field.scrollLeft, field.scrollTop].join('|');
            if(state === painted && host.isConnected)return;
            painted = state;
            layer.replaceChildren();
            host.dataset.solSpelling = String(errors.length);
            if(!host.isConnected)document.documentElement.append(host);
            if(plain(field)){
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
          document.addEventListener('input',e=>{if(field && e.composedPath().includes(field))hide();},true);
          // Les traits sont posés à l'écran : dès que le texte bouge (défilement, taille), ils s'effacent jusqu'au prochain passage.
          addEventListener('scroll',hide,{capture:true,passive:true});
          addEventListener('resize',hide,{passive:true});
          document.addEventListener('contextmenu',e=>{
            selected=undefined;
            if(!field || !(e.composedPath().includes(field) || field.contains(e.target)))return;
            const text=value(field);let caret=plain(field)?field.selectionStart:undefined;
            if(caret===undefined){const hit=document.caretRangeFromPoint(e.clientX,e.clientY);const at=hit && scan(field).list.find(s=>s.node===hit.startContainer);if(!at)return;caret=at.start+hit.startOffset;}
            const words=[...text.matchAll(/[\p{L}\p{M}]+(?:['’][\p{L}\p{M}]+)*/gu)];
            const word=words.find(m=>caret>=m.index && caret<=m.index+m[0].length);
            selected=word ? {field,text,start:word.index,length:word[0].length,word:word[0],at:performance.now()} : undefined;
          },true);
          const replace = (original,replacement) => {
            const s=selected;if(!s || !s.field.isConnected || value(s.field)!==s.text || s.word!==original)return false;
            s.field.focus();
            if(plain(s.field))s.field.setSelectionRange(s.start,s.start+s.length);
            else {field=s.field;originalAttribute=field.getAttribute('spellcheck');field.spellcheck=false;const r=range(s.start,s.length);if(!r)return false;getSelection().removeAllRanges();getSelection().addRange(r);}
            return document.execCommand('insertText',false,replacement);
          };
          window.__solSpelling={read,paint,clear,word:()=>selected && performance.now()-selected.at<1500 ? selected.word : null,replace};
        })()
        """;
}
