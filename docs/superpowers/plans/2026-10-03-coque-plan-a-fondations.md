# Coque du navigateur — Plan A : outils, prototypes, logique de l'hôte

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Installer les outils sur D:, lever les deux risques techniques par des prototypes jetables, et livrer la bibliothèque `Browser.Core` (logique de l'hôte, testée, sans WebView2).

**Architecture:** L'hôte C# détient tout l'état ; l'interface React l'affiche et envoie des commandes JSON validées. Ce plan construit la partie de l'hôte qui ne dépend pas du type de fenêtre. Le Plan B (fenêtre, vues web, interface React) sera écrit après la Tâche 3, dont le résultat fixe le type de fenêtre.

**Tech Stack:** .NET 10 SDK, C#, WinUI 3 (Windows App SDK, non empaqueté) pour le prototype, WebView2, xUnit, Microsoft.Data.Sqlite, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-10-03-coque-navigateur-design.md`

## Global Constraints

- **Rien sur C:.** C: n'a que 2,3 Go libres. SDK, caches NuGet et npm, fichiers temporaires et données WebView2 de développement vont dans `D:\dev`. Chaque commande `dotnet` ou `npm` se lance dans un shell où `tools/env.ps1` a été chargé (`. D:\projet-navigateur\tools\env.ps1`).
- Shell : PowerShell 7. Répertoire de travail : `D:\projet-navigateur`.
- Aucune ressource sous licence Apple (San Francisco, SF Symbols).
- La vue d'interface ne charge que des fichiers locaux. Les vues de pages n'ont aucun canal vers l'hôte (exception : le prototype jetable de la Tâche 3).
- L'hôte valide chaque commande reçue et ignore le reste.
- Tout ce qui est sous `prototypes/` est jetable et ne doit être référencé par aucun code de `src/`.

## Structure des fichiers

```
tools/env.ps1                         variables d'environnement de dev (tout sur D:)
.gitignore
prototypes/Proto/                     JETABLE — app WinUI 3 des Tâches 2 et 3
  Proto.csproj, App.xaml(.cs), MainWindow.xaml(.cs)
  ui/index.html                       fausse interface avec menu
  inject/blur.js, inject/bench.js     flou injecté et mesure de défilement
docs/superpowers/specs/2026-10-03-prototypes-resultats.md   résultats et décisions
src/Browser.Core/
  TabManager.cs                       onglets : ouvrir, fermer, déplacer, activer, rouvrir
  SplitLayout.cs                      vue scindée : onglet secondaire, ratio
  CommandParser.cs                    validation des messages interface → hôte
  AddressInput.cs                     saisie de la barre d'adresse → URL
  BrowserDb.cs                        SQLite : historique, favoris
  JsonFileStore.cs                    réglages et session en JSON, repli si corrompu
  AppPaths.cs                         dossier de données
tests/Browser.Core.Tests/             un fichier de tests par fichier source
```

---

### Task 1: Outils sur D: et squelette du dépôt

**Files:**
- Create: `tools/env.ps1`, `.gitignore`

**Interfaces:**
- Produces: `tools/env.ps1`, à charger avant toute commande `dotnet`/`npm` ; la commande `dotnet` (SDK 10) disponible après chargement.

- [ ] **Step 1: Écrire `tools/env.ps1`**

```powershell
# À charger dans chaque shell de dev : . D:\projet-navigateur\tools\env.ps1
# Garde tous les outils, caches et données de dev hors de C: (disque plein).
$dev = 'D:\dev'
foreach ($d in 'dotnet','dotnet-home','nuget-packages','npm-cache','tmp','webview2-data','navigateur-data') {
    New-Item -ItemType Directory -Force "$dev\$d" | Out-Null
}
$env:DOTNET_ROOT = "$dev\dotnet"
$env:DOTNET_CLI_HOME = "$dev\dotnet-home"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = "$dev\nuget-packages"
$env:npm_config_cache = "$dev\npm-cache"
$env:TEMP = "$dev\tmp"
$env:TMP = "$dev\tmp"
$env:WEBVIEW2_USER_DATA_FOLDER = "$dev\webview2-data"
$env:NAVIGATEUR_DATA_DIR = "$dev\navigateur-data"
if ($env:PATH -notlike "*$dev\dotnet*") { $env:PATH = "$dev\dotnet;$env:PATH" }
```

- [ ] **Step 2: Écrire `.gitignore`**

```
bin/
obj/
node_modules/
dist/
*.user
```

- [ ] **Step 3: Installer le SDK .NET 10 dans `D:\dev\dotnet`**

Téléchargement du script officiel Microsoft (`dot.net`) : demander l'accord de l'utilisateur avant de lancer.

```powershell
. D:\projet-navigateur\tools\env.ps1
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile D:\dev\dotnet-install.ps1
& D:\dev\dotnet-install.ps1 -Channel 10.0 -InstallDir D:\dev\dotnet
```

- [ ] **Step 4: Vérifier**

Run: `. D:\projet-navigateur\tools\env.ps1; dotnet --version; (Get-Command dotnet).Source`
Expected: une version `10.0.x` et le chemin `D:\dev\dotnet\dotnet.exe`.

Run: `(Get-PSDrive C).Free / 1GB`
Expected: à peu près la même valeur qu'avant l'installation (2,3 Go).

- [ ] **Step 5: Commit**

```bash
git add tools/env.ps1 .gitignore
git commit -m "chore: dev environment on D: drive"
```

---

### Task 2: Prototype 1 — menus de l'interface par-dessus la page (JETABLE)

Question à trancher : dans WinUI 3, une WebView2 d'interface à fond transparent peut-elle se superposer à une WebView2 de page, de sorte qu'un menu déroulant s'affiche au-dessus du site ?

Principe testé : la vue d'interface ne couvre normalement que la barre du haut (88 px). À l'ouverture d'un menu, l'hôte l'agrandit à toute la fenêtre ; son fond transparent laisse voir la page ; un clic hors du menu le ferme et la vue reprend 88 px.

**Files:**
- Create: `prototypes/Proto/Proto.csproj`, `prototypes/Proto/App.xaml`, `prototypes/Proto/App.xaml.cs`, `prototypes/Proto/MainWindow.xaml`, `prototypes/Proto/MainWindow.xaml.cs`, `prototypes/Proto/ui/index.html`
- Create: `docs/superpowers/specs/2026-10-03-prototypes-resultats.md`

**Interfaces:**
- Produces: une décision écrite — « WinUI 3 avec superposition transparente » ou « repli : menus dans une vue web dédiée opaque » — que le Plan B consomme.

- [ ] **Step 1: `Proto.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
    <RootNamespace>Proto</RootNamespace>
    <Platforms>x64</Platforms>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <UseWinUI>true</UseWinUI>
    <WindowsPackageType>None</WindowsPackageType>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <EnableMsixTooling>true</EnableMsixTooling>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.*" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.*" />
  </ItemGroup>
  <ItemGroup>
    <Content Include="ui\**;inject\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: `App.xaml` et `App.xaml.cs`**

```xml
<Application x:Class="Proto.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Application.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
      </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
  </Application.Resources>
</Application>
```

```csharp
using Microsoft.UI.Xaml;

namespace Proto;

public partial class App : Application
{
    private Window? _window;
    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
```

- [ ] **Step 3: `MainWindow.xaml`**

La vue d'interface est déclarée après la vue de page, donc dessinée au-dessus.

```xml
<Window x:Class="Proto.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Grid>
    <WebView2 x:Name="Page" Margin="0,88,0,0" />
    <WebView2 x:Name="Ui" VerticalAlignment="Top" Height="88" />
  </Grid>
</Window>
```

- [ ] **Step 4: `MainWindow.xaml.cs`**

```csharp
using System;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Proto;

public sealed partial class MainWindow : Window
{
    private const double BarHeight = 88;

    public MainWindow()
    {
        InitializeComponent();
        _ = InitAsync();
    }

    private async System.Threading.Tasks.Task InitAsync()
    {
        Ui.DefaultBackgroundColor = Colors.Transparent;
        await Ui.EnsureCoreWebView2Async();
        await Page.EnsureCoreWebView2Async();

        Ui.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "ui.local", Path.Combine(AppContext.BaseDirectory, "ui"),
            CoreWebView2HostResourceAccessKind.Allow);
        Ui.CoreWebView2.WebMessageReceived += OnUiMessage;
        Ui.Source = new Uri("https://ui.local/index.html");
        Page.Source = new Uri("https://www.wikipedia.org/");
    }

    private void OnUiMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        switch (e.TryGetWebMessageAsString())
        {
            case "menu:open":
                Ui.Height = double.NaN;
                Ui.VerticalAlignment = VerticalAlignment.Stretch;
                break;
            case "menu:close":
                Ui.VerticalAlignment = VerticalAlignment.Top;
                Ui.Height = BarHeight;
                break;
        }
    }
}
```

- [ ] **Step 5: `ui/index.html`**

```html
<!doctype html>
<meta charset="utf-8">
<style>
  html, body { margin: 0; height: 100%; background: transparent; font: 14px system-ui; }
  #bar { height: 88px; background: #f2f2f4; display: flex; align-items: center; gap: 8px; padding: 0 16px; box-sizing: border-box; }
  #menu { display: none; position: absolute; top: 80px; left: 16px; width: 260px; padding: 8px;
          background: rgba(255,255,255,.85); backdrop-filter: blur(20px); border-radius: 12px;
          box-shadow: 0 10px 40px rgba(0,0,0,.25); }
  #menu.open { display: block; }
  #menu div { padding: 8px 10px; border-radius: 8px; }
  #menu div:hover { background: rgba(0,0,0,.08); }
  #out { margin-left: auto; font-variant-numeric: tabular-nums; }
</style>
<div id="bar">
  <button id="btn">Menu</button>
  <button id="blur">Flou : off</button>
  <button id="bench">Mesurer</button>
  <span id="out"></span>
</div>
<div id="menu"><div>Nouvel onglet</div><div>Historique</div><div>Favoris</div></div>
<script>
  const send = m => chrome.webview.postMessage(m);
  const menu = document.getElementById('menu');
  const setMenu = open => { menu.classList.toggle('open', open); send(open ? 'menu:open' : 'menu:close'); };
  document.getElementById('btn').onclick = e => { e.stopPropagation(); setMenu(!menu.classList.contains('open')); };
  document.addEventListener('click', e => { if (!menu.contains(e.target)) setMenu(false); });
  let blur = false;
  document.getElementById('blur').onclick = e => { blur = !blur; e.target.textContent = 'Flou : ' + (blur ? 'on' : 'off'); send(blur ? 'blur:on' : 'blur:off'); };
  document.getElementById('bench').onclick = () => { document.getElementById('out').textContent = 'mesure…'; send('bench'); };
  chrome.webview.addEventListener('message', e => document.getElementById('out').textContent = e.data);
</script>
```

- [ ] **Step 6: Compiler et lancer**

Run: `. D:\projet-navigateur\tools\env.ps1; dotnet run --project prototypes/Proto`
Expected: une fenêtre avec une barre grise en haut et Wikipédia en dessous.

Si `dotnet build` échoue sur la compilation XAML sans Visual Studio, noter l'erreur exacte dans le fichier de résultats : c'est en soi un résultat du prototype (le repli devient alors un hôte WPF, à valider avec l'utilisateur avant de continuer).

- [ ] **Step 7: Vérifier à la main et consigner**

Liste de contrôle :
1. Clic sur « Menu » : le menu s'affiche par-dessus Wikipédia, le site reste visible autour et derrière (fond translucide du menu).
2. Menu ouvert : cliquer sur la page ferme le menu ; un second clic atteint bien la page (lien cliquable).
3. Menu fermé : défilement et clics sur la page fonctionnent normalement.
4. Aucun flash blanc ou noir à l'ouverture/fermeture du menu.

Écrire `docs/superpowers/specs/2026-10-03-prototypes-resultats.md` :

```markdown
# Résultats des prototypes

## Prototype 1 — menus par-dessus la page
- Compilation WinUI 3 par `dotnet build` sans Visual Studio : OUI / NON (erreur : …)
- Points 1 à 4 de la liste de contrôle : OK / KO pour chacun, avec ce qui a été observé
- **Décision :** superposition transparente WinUI 3 / repli vue web dédiée opaque / repli hôte WPF
```

Remplir avec les observations réelles, pas avec les valeurs attendues.

- [ ] **Step 8: Commit**

```bash
git add prototypes/Proto docs/superpowers/specs/2026-10-03-prototypes-resultats.md
git commit -m "spike: UI overlay above page view (throwaway)"
```

---

### Task 3: Prototype 2 — flou progressif injecté et son coût (JETABLE)

Question à trancher : un flou progressif à plusieurs couches, injecté dans chaque page, garde-t-il un défilement fluide sur cette machine ?

**Files:**
- Create: `prototypes/Proto/inject/blur.js`, `prototypes/Proto/inject/bench.js`
- Modify: `prototypes/Proto/MainWindow.xaml.cs`
- Modify: `docs/superpowers/specs/2026-10-03-prototypes-resultats.md`

**Interfaces:**
- Consumes: l'app `Proto` de la Tâche 2 (boutons « Flou » et « Mesurer » déjà présents, messages `blur:on`, `blur:off`, `bench`).
- Produces: `inject/blur.js` validé (repris tel quel par le Plan B) et une décision écrite « 4 couches » ou « repli 1 couche ».

- [ ] **Step 1: `inject/blur.js`**

Construit avec l'API DOM uniquement (pas de `innerHTML`) pour passer sur les sites qui imposent Trusted Types. Shadow DOM fermé pour que le CSS du site ne l'atteigne pas.

```js
(() => {
  if (window.top !== window || window.__navBlur) return;
  window.__navBlur = true;
  const HEIGHT = 72, STEPS = [1, 2, 4, 8];
  const build = () => {
    const host = document.createElement('div');
    const root = host.attachShadow({ mode: 'closed' });
    for (const side of ['top', 'bottom']) {
      const band = document.createElement('div');
      band.style.cssText = `position:fixed;left:0;right:0;${side}:0;height:${HEIGHT}px;pointer-events:none;z-index:2147483647`;
      const dir = side === 'top' ? 'to top' : 'to bottom';
      STEPS.forEach((px, i) => {
        const layer = document.createElement('div');
        const from = (i * 100) / STEPS.length, to = ((i + 1) * 100) / STEPS.length;
        const mask = `linear-gradient(${dir},transparent ${from}%,#000 ${to}%)`;
        layer.style.cssText = `position:absolute;inset:0;backdrop-filter:blur(${px}px);-webkit-mask-image:${mask};mask-image:${mask}`;
        band.appendChild(layer);
      });
      root.appendChild(band);
    }
    document.documentElement.appendChild(host);
  };
  if (document.documentElement) build();
  else document.addEventListener('DOMContentLoaded', build, { once: true });
})();
```

- [ ] **Step 2: `inject/bench.js`**

Défile pendant 5 secondes et renvoie les temps de trame.

```js
new Promise(resolve => {
  const deltas = []; let last = performance.now(); const start = last;
  const tick = now => {
    deltas.push(now - last); last = now;
    window.scrollBy(0, 20);
    if (now - start < 5000) requestAnimationFrame(tick);
    else {
      deltas.sort((a, b) => a - b);
      const p95 = deltas[Math.floor(deltas.length * 0.95)];
      const slow = deltas.filter(d => d > 25).length;
      resolve(`${deltas.length} trames, p95 ${p95.toFixed(1)} ms, ${slow} lentes`);
    }
  };
  requestAnimationFrame(tick);
});
```

- [ ] **Step 3: Brancher dans `MainWindow.xaml.cs`**

Ajouter le champ et les trois cas dans `OnUiMessage` (qui devient `async void`) :

```csharp
    private string? _blurScriptId;

    private async void OnUiMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var inject = Path.Combine(AppContext.BaseDirectory, "inject");
        switch (e.TryGetWebMessageAsString())
        {
            case "menu:open":
                Ui.Height = double.NaN;
                Ui.VerticalAlignment = VerticalAlignment.Stretch;
                break;
            case "menu:close":
                Ui.VerticalAlignment = VerticalAlignment.Top;
                Ui.Height = BarHeight;
                break;
            case "blur:on":
                _blurScriptId = await Page.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    File.ReadAllText(Path.Combine(inject, "blur.js")));
                Page.CoreWebView2.Reload();
                break;
            case "blur:off":
                if (_blurScriptId is not null) Page.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_blurScriptId);
                _blurScriptId = null;
                Page.CoreWebView2.Reload();
                break;
            case "bench":
                var result = await Page.CoreWebView2.ExecuteScriptWithResultAsync(
                    File.ReadAllText(Path.Combine(inject, "bench.js")));
                result.TryGetResultAsString(out var text, out _);
                Ui.CoreWebView2.PostWebMessageAsString(text ?? "échec de la mesure");
                break;
        }
    }
```

Si `ExecuteScriptWithResultAsync` n'attend pas la promesse dans la version installée du SDK, remplacer par `Page.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", ...)` avec `awaitPromise: true` et lire `result.value` dans le JSON renvoyé.

- [ ] **Step 4: Mesurer**

Run: `. D:\projet-navigateur\tools\env.ps1; dotnet run --project prototypes/Proto`

Pour chacun des trois sites, changer temporairement `Page.Source` (une page longue de Wikipédia, `https://github.com/WebKit/WebKit`, `https://www.youtube.com/`), puis : « Mesurer » avec flou off, activer le flou, « Mesurer » avec flou on. Noter les six résultats.

Vérifier aussi à l'œil : les bandes sont visibles en haut et en bas, le dégradé de flou est progressif, les clics traversent les bandes.

- [ ] **Step 5: Décider et consigner**

Règle : si, avec le flou, le p95 reste sous 20 ms sur les trois sites, on garde les 4 couches. Sinon, refaire la mesure avec `STEPS = [8]` (une seule couche) et consigner les deux séries.

Ajouter à `docs/superpowers/specs/2026-10-03-prototypes-resultats.md` :

```markdown
## Prototype 2 — flou progressif injecté
| Site | Sans flou | 4 couches | 1 couche (si mesuré) |
|---|---|---|---|
| Wikipédia | … | … | … |
| GitHub | … | … | … |
| YouTube | … | … | … |

- Rendu visuel et clics traversants : OK / KO, observations
- Sites où l'injection n'a pas pris : …
- **Décision :** 4 couches / 1 couche / flou désactivé par défaut
```

Remplir avec les mesures réelles.

- [ ] **Step 6: Commit et point d'arrêt**

```bash
git add prototypes/Proto docs/superpowers/specs/2026-10-03-prototypes-resultats.md
git commit -m "spike: injected progressive blur and scroll cost (throwaway)"
```

**Arrêt :** présenter les deux décisions à l'utilisateur avant de continuer. Si une décision est un repli, la spec est mise à jour en conséquence.

---

### Task 4: `Browser.Core` — onglets et vue scindée

**Files:**
- Create: `src/Browser.Core/Browser.Core.csproj`, `src/Browser.Core/TabManager.cs`, `src/Browser.Core/SplitLayout.cs`
- Test: `tests/Browser.Core.Tests/TabManagerTests.cs`, `tests/Browser.Core.Tests/SplitLayoutTests.cs`

**Interfaces:**
- Produces:
  - `record Tab(string Id, string Url, string Title)`
  - `TabManager` : `IReadOnlyList<Tab> Tabs`, `string? ActiveId`, `event Action? Changed`, `Tab Open(string url, bool activate = true)`, `bool Close(string id)`, `bool Activate(string id)`, `bool Move(string id, int index)`, `Tab? ReopenClosed()`, `bool Update(string id, string? url = null, string? title = null)`
  - `SplitLayout` : `string? SecondaryId`, `double Ratio`, `bool IsSplit`, `bool Open(string tabId, string? activeId)`, `void Close()`, `void Resize(double ratio)`, `void OnTabClosed(string id)`

- [ ] **Step 1: Créer la solution et les projets**

```powershell
. D:\projet-navigateur\tools\env.ps1
dotnet new sln -n Navigateur
dotnet new classlib -o src/Browser.Core
dotnet new xunit -o tests/Browser.Core.Tests
dotnet add tests/Browser.Core.Tests reference src/Browser.Core
dotnet sln add src/Browser.Core tests/Browser.Core.Tests
Remove-Item src/Browser.Core/Class1.cs, tests/Browser.Core.Tests/UnitTest1.cs
```

- [ ] **Step 2: Écrire les tests qui échouent**

`tests/Browser.Core.Tests/TabManagerTests.cs` :

```csharp
using Browser.Core;

public class TabManagerTests
{
    [Fact]
    public void Open_adds_tab_and_activates_it()
    {
        var m = new TabManager();
        var t = m.Open("https://a.test");
        Assert.Single(m.Tabs);
        Assert.Equal(t.Id, m.ActiveId);
    }

    [Fact]
    public void Open_in_background_keeps_active_tab()
    {
        var m = new TabManager();
        var a = m.Open("https://a.test");
        m.Open("https://b.test", activate: false);
        Assert.Equal(a.Id, m.ActiveId);
    }

    [Fact]
    public void Closing_active_tab_activates_the_one_that_takes_its_place()
    {
        var m = new TabManager();
        var a = m.Open("https://a.test");
        var b = m.Open("https://b.test");
        var c = m.Open("https://c.test");
        m.Activate(b.Id);
        Assert.True(m.Close(b.Id));
        Assert.Equal(c.Id, m.ActiveId);
        m.Close(c.Id);
        Assert.Equal(a.Id, m.ActiveId);
    }

    [Fact]
    public void Closing_last_tab_leaves_no_active_tab()
    {
        var m = new TabManager();
        m.Close(m.Open("https://a.test").Id);
        Assert.Null(m.ActiveId);
    }

    [Fact]
    public void Unknown_ids_are_rejected()
    {
        var m = new TabManager();
        Assert.False(m.Close("nope"));
        Assert.False(m.Activate("nope"));
        Assert.False(m.Move("nope", 0));
        Assert.False(m.Update("nope", title: "x"));
    }

    [Fact]
    public void Move_reorders_and_rejects_out_of_range()
    {
        var m = new TabManager();
        var a = m.Open("https://a.test");
        var b = m.Open("https://b.test");
        Assert.True(m.Move(b.Id, 0));
        Assert.Equal(new[] { b.Id, a.Id }, m.Tabs.Select(t => t.Id));
        Assert.False(m.Move(b.Id, 2));
        Assert.False(m.Move(b.Id, -1));
    }

    [Fact]
    public void ReopenClosed_restores_last_closed_url()
    {
        var m = new TabManager();
        m.Open("https://keep.test");
        m.Close(m.Open("https://gone.test").Id);
        Assert.Equal("https://gone.test", m.ReopenClosed()!.Url);
        Assert.Null(m.ReopenClosed());
    }

    [Fact]
    public void Update_changes_url_and_title_and_raises_Changed()
    {
        var m = new TabManager();
        var t = m.Open("https://a.test");
        var raised = 0;
        m.Changed += () => raised++;
        Assert.True(m.Update(t.Id, url: "https://b.test", title: "B"));
        Assert.Equal(new Tab(t.Id, "https://b.test", "B"), m.Tabs[0]);
        Assert.Equal(1, raised);
    }
}
```

`tests/Browser.Core.Tests/SplitLayoutTests.cs` :

```csharp
using Browser.Core;

public class SplitLayoutTests
{
    [Fact]
    public void Open_sets_secondary_unless_it_is_the_active_tab()
    {
        var s = new SplitLayout();
        Assert.False(s.Open("t1", activeId: "t1"));
        Assert.False(s.IsSplit);
        Assert.True(s.Open("t2", activeId: "t1"));
        Assert.Equal("t2", s.SecondaryId);
    }

    [Fact]
    public void Resize_clamps_ratio()
    {
        var s = new SplitLayout();
        s.Resize(0.05); Assert.Equal(0.2, s.Ratio);
        s.Resize(0.95); Assert.Equal(0.8, s.Ratio);
        s.Resize(0.6); Assert.Equal(0.6, s.Ratio);
    }

    [Fact]
    public void Closing_the_secondary_tab_ends_the_split()
    {
        var s = new SplitLayout();
        s.Open("t2", "t1");
        s.OnTabClosed("t9"); Assert.True(s.IsSplit);
        s.OnTabClosed("t2"); Assert.False(s.IsSplit);
    }
}
```

- [ ] **Step 3: Vérifier l'échec**

Run: `dotnet test`
Expected: échec de compilation, `TabManager` et `SplitLayout` introuvables.

- [ ] **Step 4: Implémenter**

`src/Browser.Core/TabManager.cs` :

```csharp
namespace Browser.Core;

public sealed record Tab(string Id, string Url, string Title);

public sealed class TabManager
{
    private readonly List<Tab> _tabs = new();
    // ponytail: pile non bornée ; plafonner si la mémoire d'une longue session pose problème
    private readonly Stack<string> _closed = new();
    private int _nextId = 1;

    public IReadOnlyList<Tab> Tabs => _tabs;
    public string? ActiveId { get; private set; }
    public event Action? Changed;

    public Tab Open(string url, bool activate = true)
    {
        var tab = new Tab($"t{_nextId++}", url, url);
        _tabs.Add(tab);
        if (activate || ActiveId is null) ActiveId = tab.Id;
        Changed?.Invoke();
        return tab;
    }

    public bool Close(string id)
    {
        var i = IndexOf(id);
        if (i < 0) return false;
        _closed.Push(_tabs[i].Url);
        _tabs.RemoveAt(i);
        if (ActiveId == id)
            ActiveId = _tabs.Count == 0 ? null : _tabs[Math.Min(i, _tabs.Count - 1)].Id;
        Changed?.Invoke();
        return true;
    }

    public bool Activate(string id)
    {
        if (IndexOf(id) < 0) return false;
        ActiveId = id;
        Changed?.Invoke();
        return true;
    }

    public bool Move(string id, int index)
    {
        var i = IndexOf(id);
        if (i < 0 || index < 0 || index >= _tabs.Count) return false;
        var tab = _tabs[i];
        _tabs.RemoveAt(i);
        _tabs.Insert(index, tab);
        Changed?.Invoke();
        return true;
    }

    public Tab? ReopenClosed() => _closed.Count == 0 ? null : Open(_closed.Pop());

    public bool Update(string id, string? url = null, string? title = null)
    {
        var i = IndexOf(id);
        if (i < 0) return false;
        _tabs[i] = _tabs[i] with { Url = url ?? _tabs[i].Url, Title = title ?? _tabs[i].Title };
        Changed?.Invoke();
        return true;
    }

    private int IndexOf(string id) => _tabs.FindIndex(t => t.Id == id);
}
```

`src/Browser.Core/SplitLayout.cs` :

```csharp
namespace Browser.Core;

public sealed class SplitLayout
{
    public string? SecondaryId { get; private set; }
    public double Ratio { get; private set; } = 0.5;
    public bool IsSplit => SecondaryId is not null;

    public bool Open(string tabId, string? activeId)
    {
        if (tabId == activeId) return false;
        SecondaryId = tabId;
        return true;
    }

    public void Close() => SecondaryId = null;
    public void Resize(double ratio) => Ratio = Math.Clamp(ratio, 0.2, 0.8);
    public void OnTabClosed(string id) { if (SecondaryId == id) Close(); }
}
```

- [ ] **Step 5: Vérifier le succès**

Run: `dotnet test`
Expected: 11 tests réussis.

- [ ] **Step 6: Commit**

```bash
git add Navigateur.sln* src/Browser.Core tests/Browser.Core.Tests
git commit -m "feat: tab manager and split layout"
```

---

### Task 5: `Browser.Core` — validation des commandes de l'interface

**Files:**
- Create: `src/Browser.Core/CommandParser.cs`
- Test: `tests/Browser.Core.Tests/CommandParserTests.cs`

**Interfaces:**
- Produces:
  - `record Command(string Type, string? Id = null, string? Url = null, string? Title = null, string? Input = null, string? Query = null, int? Index = null, double? Ratio = null)`
  - `static Command? CommandParser.Parse(string json)` — `null` pour tout message invalide.
  - Types acceptés et champs obligatoires : `tab.open` (aucun ; `url` facultatif), `tab.close` (id), `tab.activate` (id), `tab.move` (id, index), `tab.reopen`, `nav.go` (id, input), `nav.back` (id), `nav.forward` (id), `nav.reload` (id), `nav.stop` (id), `split.open` (id), `split.close`, `split.resize` (ratio), `bookmark.add` (url, title), `bookmark.remove` (url), `history.search` (query), `download.cancel` (id), `download.open` (id), `window.minimize`, `window.maximize`, `window.close`.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using Browser.Core;

public class CommandParserTests
{
    [Fact]
    public void Parses_a_valid_command()
    {
        var c = CommandParser.Parse("""{"type":"tab.move","id":"t3","index":1}""");
        Assert.Equal(new Command("tab.move", Id: "t3", Index: 1), c);
    }

    [Fact]
    public void Optional_field_may_be_absent()
    {
        Assert.Equal(new Command("tab.open"), CommandParser.Parse("""{"type":"tab.open"}"""));
        Assert.Equal("https://a.test", CommandParser.Parse("""{"type":"tab.open","url":"https://a.test"}""")!.Url);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"id":"t1"}""")]
    [InlineData("""{"type":"shell.exec","id":"t1"}""")]
    [InlineData("""{"type":"tab.close"}""")]
    [InlineData("""{"type":"tab.close","id":42}""")]
    [InlineData("""{"type":"tab.move","id":"t1","index":"1"}""")]
    [InlineData("""{"type":"tab.move","id":"t1","index":1.5}""")]
    [InlineData("""{"type":"split.resize","ratio":"half"}""")]
    [InlineData("""{"type":"bookmark.add","url":"https://a.test"}""")]
    public void Rejects_invalid_messages(string json) => Assert.Null(CommandParser.Parse(json));
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test --filter CommandParserTests`
Expected: échec de compilation, `CommandParser` introuvable.

- [ ] **Step 3: Implémenter**

```csharp
using System.Text.Json;

namespace Browser.Core;

public sealed record Command(string Type, string? Id = null, string? Url = null, string? Title = null,
    string? Input = null, string? Query = null, int? Index = null, double? Ratio = null);

public static class CommandParser
{
    private static readonly Dictionary<string, string[]> Required = new()
    {
        ["tab.open"] = [], ["tab.close"] = ["id"], ["tab.activate"] = ["id"],
        ["tab.move"] = ["id", "index"], ["tab.reopen"] = [],
        ["nav.go"] = ["id", "input"], ["nav.back"] = ["id"], ["nav.forward"] = ["id"],
        ["nav.reload"] = ["id"], ["nav.stop"] = ["id"],
        ["split.open"] = ["id"], ["split.close"] = [], ["split.resize"] = ["ratio"],
        ["bookmark.add"] = ["url", "title"], ["bookmark.remove"] = ["url"],
        ["history.search"] = ["query"],
        ["download.cancel"] = ["id"], ["download.open"] = ["id"],
        ["window.minimize"] = [], ["window.maximize"] = [], ["window.close"] = [],
    };

    public static Command? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Str(root, "type") is not { } type || !Required.TryGetValue(type, out var required)) return null;

            var cmd = new Command(type, Str(root, "id"), Str(root, "url"), Str(root, "title"),
                Str(root, "input"), Str(root, "query"), Int(root, "index"), Num(root, "ratio"));
            return required.All(field => Has(cmd, field)) ? cmd : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Has(Command c, string field) => field switch
    {
        "id" => c.Id is not null, "url" => c.Url is not null, "title" => c.Title is not null,
        "input" => c.Input is not null, "query" => c.Query is not null,
        "index" => c.Index is not null, "ratio" => c.Ratio is not null,
        _ => false,
    };

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test --filter CommandParserTests`
Expected: 12 tests réussis.

- [ ] **Step 5: Commit**

```bash
git add src/Browser.Core/CommandParser.cs tests/Browser.Core.Tests/CommandParserTests.cs
git commit -m "feat: validate UI-to-host commands"
```

---

### Task 6: `Browser.Core` — saisie de la barre d'adresse

**Files:**
- Create: `src/Browser.Core/AddressInput.cs`
- Test: `tests/Browser.Core.Tests/AddressInputTests.cs`

**Interfaces:**
- Produces: `static string AddressInput.Resolve(string input, string searchUrlTemplate)` — renvoie une URL à charger. `searchUrlTemplate` contient `{0}` pour la requête encodée.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using Browser.Core;

public class AddressInputTests
{
    private const string Search = "https://www.google.com/search?q={0}";

    [Theory]
    [InlineData("https://a.test/x", "https://a.test/x")]
    [InlineData("http://a.test", "http://a.test")]
    [InlineData("about:blank", "about:blank")]
    [InlineData("github.com/WebKit", "https://github.com/WebKit")]
    [InlineData("  example.org  ", "https://example.org")]
    [InlineData("localhost:5173", "http://localhost:5173")]
    [InlineData("localhost", "http://localhost")]
    [InlineData("127.0.0.1:8080/api", "http://127.0.0.1:8080/api")]
    [InlineData("192.168.1.20", "http://192.168.1.20")]
    [InlineData("react hooks", "https://www.google.com/search?q=react%20hooks")]
    [InlineData("c# 12", "https://www.google.com/search?q=c%23%2012")]
    [InlineData("webkit", "https://www.google.com/search?q=webkit")]
    [InlineData("42", "https://www.google.com/search?q=42")]
    [InlineData("what is a.b", "https://www.google.com/search?q=what%20is%20a.b")]
    public void Resolves(string input, string expected) =>
        Assert.Equal(expected, AddressInput.Resolve(input, Search));
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test --filter AddressInputTests`
Expected: échec de compilation, `AddressInput` introuvable.

- [ ] **Step 3: Implémenter**

```csharp
using System.Net;

namespace Browser.Core;

public static class AddressInput
{
    private static readonly string[] Schemes = ["http://", "https://", "file://", "about:"];

    public static string Resolve(string input, string searchUrlTemplate)
    {
        var text = input.Trim();
        if (Schemes.Any(s => text.StartsWith(s, StringComparison.OrdinalIgnoreCase))) return text;

        if (!text.Contains(' '))
        {
            var host = text.Split('/', 2)[0].Split(':', 2)[0];
            // IPAddress.TryParse accepte "42" (0.0.0.42) : on exige un point pour ne pas détourner une recherche
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || (host.Contains('.') && IPAddress.TryParse(host, out _)))
                return "http://" + text;
            if (host.Contains('.')) return "https://" + text;
        }
        return string.Format(searchUrlTemplate, Uri.EscapeDataString(text));
    }
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test --filter AddressInputTests`
Expected: 14 tests réussis.

- [ ] **Step 5: Commit**

```bash
git add src/Browser.Core/AddressInput.cs tests/Browser.Core.Tests/AddressInputTests.cs
git commit -m "feat: resolve address bar input to URL or search"
```

---

### Task 7: `Browser.Core` — historique et favoris (SQLite)

**Files:**
- Create: `src/Browser.Core/BrowserDb.cs`
- Modify: `src/Browser.Core/Browser.Core.csproj` (paquet `Microsoft.Data.Sqlite`)
- Test: `tests/Browser.Core.Tests/BrowserDbTests.cs`

**Interfaces:**
- Produces:
  - `record Visit(string Url, string Title, int Count, DateTimeOffset Last)`
  - `record Bookmark(string Url, string Title)`
  - `BrowserDb(string dataSource)` (`":memory:"` pour les tests), `IDisposable`
  - `void RecordVisit(string url, string title, DateTimeOffset when)`
  - `IReadOnlyList<Visit> RecentHistory(int limit)` — du plus récent au plus ancien
  - `IReadOnlyList<Visit> Suggest(string query, int limit)` — URL ou titre contenant `query`, les plus visités d'abord
  - `void AddBookmark(string url, string title)`, `void RemoveBookmark(string url)`, `bool IsBookmarked(string url)`, `IReadOnlyList<Bookmark> Bookmarks()`

- [ ] **Step 1: Ajouter le paquet**

Run: `dotnet add src/Browser.Core package Microsoft.Data.Sqlite`

- [ ] **Step 2: Écrire les tests qui échouent**

```csharp
using Browser.Core;

public class BrowserDbTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Revisiting_a_url_increments_count_and_updates_title_and_time()
    {
        using var db = new BrowserDb(":memory:");
        db.RecordVisit("https://a.test", "Old", T0);
        db.RecordVisit("https://a.test", "New", T0.AddMinutes(5));
        var v = Assert.Single(db.RecentHistory(10));
        Assert.Equal(new Visit("https://a.test", "New", 2, T0.AddMinutes(5)), v);
    }

    [Fact]
    public void RecentHistory_is_newest_first_and_limited()
    {
        using var db = new BrowserDb(":memory:");
        db.RecordVisit("https://a.test", "A", T0);
        db.RecordVisit("https://b.test", "B", T0.AddMinutes(1));
        db.RecordVisit("https://c.test", "C", T0.AddMinutes(2));
        Assert.Equal(new[] { "https://c.test", "https://b.test" }, db.RecentHistory(2).Select(v => v.Url));
    }

    [Fact]
    public void Suggest_matches_url_or_title_most_visited_first()
    {
        using var db = new BrowserDb(":memory:");
        db.RecordVisit("https://docs.test/react", "Docs", T0);
        db.RecordVisit("https://b.test", "React blog", T0);
        db.RecordVisit("https://b.test", "React blog", T0.AddMinutes(1));
        db.RecordVisit("https://other.test", "Other", T0);
        Assert.Equal(new[] { "https://b.test", "https://docs.test/react" }, db.Suggest("react", 5).Select(v => v.Url));
    }

    [Fact]
    public void Suggest_treats_wildcards_literally()
    {
        using var db = new BrowserDb(":memory:");
        db.RecordVisit("https://a.test", "A", T0);
        Assert.Empty(db.Suggest("%", 5));
        Assert.Empty(db.Suggest("_", 5));
    }

    [Fact]
    public void Bookmarks_add_remove_and_list()
    {
        using var db = new BrowserDb(":memory:");
        db.AddBookmark("https://a.test", "A");
        db.AddBookmark("https://a.test", "A renamed");
        Assert.True(db.IsBookmarked("https://a.test"));
        Assert.Equal(new Bookmark("https://a.test", "A renamed"), Assert.Single(db.Bookmarks()));
        db.RemoveBookmark("https://a.test");
        Assert.False(db.IsBookmarked("https://a.test"));
    }
}
```

- [ ] **Step 3: Vérifier l'échec**

Run: `dotnet test --filter BrowserDbTests`
Expected: échec de compilation, `BrowserDb` introuvable.

- [ ] **Step 4: Implémenter**

```csharp
using Microsoft.Data.Sqlite;

namespace Browser.Core;

public sealed record Visit(string Url, string Title, int Count, DateTimeOffset Last);
public sealed record Bookmark(string Url, string Title);

public sealed class BrowserDb : IDisposable
{
    private readonly SqliteConnection _db;

    public BrowserDb(string dataSource)
    {
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dataSource }.ToString());
        _db.Open();
        Exec("""
            CREATE TABLE IF NOT EXISTS visits (
                url TEXT PRIMARY KEY, title TEXT NOT NULL, count INTEGER NOT NULL, last INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS bookmarks (
                url TEXT PRIMARY KEY, title TEXT NOT NULL, added INTEGER NOT NULL);
            """);
    }

    public void RecordVisit(string url, string title, DateTimeOffset when) => Exec("""
        INSERT INTO visits (url, title, count, last) VALUES ($url, $title, 1, $last)
        ON CONFLICT(url) DO UPDATE SET title = $title, count = count + 1, last = $last
        """, ("$url", url), ("$title", title), ("$last", when.ToUnixTimeMilliseconds()));

    public IReadOnlyList<Visit> RecentHistory(int limit) => Visits(
        "SELECT url, title, count, last FROM visits ORDER BY last DESC LIMIT $limit", ("$limit", limit));

    public IReadOnlyList<Visit> Suggest(string query, int limit)
    {
        var pattern = "%" + query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        return Visits("""
            SELECT url, title, count, last FROM visits
            WHERE url LIKE $p ESCAPE '\' OR title LIKE $p ESCAPE '\'
            ORDER BY count DESC, last DESC LIMIT $limit
            """, ("$p", pattern), ("$limit", limit));
    }

    public void AddBookmark(string url, string title) => Exec("""
        INSERT INTO bookmarks (url, title, added) VALUES ($url, $title, $added)
        ON CONFLICT(url) DO UPDATE SET title = $title
        """, ("$url", url), ("$title", title), ("$added", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

    public void RemoveBookmark(string url) => Exec("DELETE FROM bookmarks WHERE url = $url", ("$url", url));

    public bool IsBookmarked(string url)
    {
        using var cmd = Command("SELECT 1 FROM bookmarks WHERE url = $url", ("$url", url));
        return cmd.ExecuteScalar() is not null;
    }

    public IReadOnlyList<Bookmark> Bookmarks()
    {
        using var cmd = Command("SELECT url, title FROM bookmarks ORDER BY added");
        using var r = cmd.ExecuteReader();
        var list = new List<Bookmark>();
        while (r.Read()) list.Add(new Bookmark(r.GetString(0), r.GetString(1)));
        return list;
    }

    public void Dispose() => _db.Dispose();

    private IReadOnlyList<Visit> Visits(string sql, params (string, object)[] args)
    {
        using var cmd = Command(sql, args);
        using var r = cmd.ExecuteReader();
        var list = new List<Visit>();
        while (r.Read())
            list.Add(new Visit(r.GetString(0), r.GetString(1), r.GetInt32(2),
                DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(3))));
        return list;
    }

    private void Exec(string sql, params (string, object)[] args)
    {
        using var cmd = Command(sql, args);
        cmd.ExecuteNonQuery();
    }

    private SqliteCommand Command(string sql, params (string Name, object Value)[] args)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }
}
```

- [ ] **Step 5: Vérifier le succès**

Run: `dotnet test --filter BrowserDbTests`
Expected: 5 tests réussis.

- [ ] **Step 6: Commit**

```bash
git add src/Browser.Core tests/Browser.Core.Tests/BrowserDbTests.cs
git commit -m "feat: history and bookmarks storage"
```

---

### Task 8: `Browser.Core` — réglages, session et dossier de données

**Files:**
- Create: `src/Browser.Core/JsonFileStore.cs`, `src/Browser.Core/AppPaths.cs`
- Test: `tests/Browser.Core.Tests/JsonFileStoreTests.cs`

**Interfaces:**
- Produces:
  - `static T JsonFileStore.Load<T>(string path) where T : new()` — valeurs par défaut si le fichier manque ; si illisible, le renomme en `<path>.corrupt` et renvoie les valeurs par défaut.
  - `static void JsonFileStore.Save<T>(string path, T value)` — écriture atomique.
  - `class Settings { string SearchUrlTemplate = "https://www.google.com/search?q={0}"; bool BlurEnabled = true; string HomeUrl = "about:blank"; }`
  - `class Session { List<string> Urls; int ActiveIndex; int? SecondaryIndex; double Ratio = 0.5; }`
  - `static string AppPaths.DataDir` — `%NAVIGATEUR_DATA_DIR%` si défini, sinon `%LOCALAPPDATA%\projet-navigateur` ; `AppPaths.Settings`, `AppPaths.Session`, `AppPaths.Database`.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using Browser.Core;

public class JsonFileStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nav-tests").FullName;
    private string File_(string name) => Path.Combine(_dir, name);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = JsonFileStore.Load<Settings>(File_("settings.json"));
        Assert.True(s.BlurEnabled);
        Assert.Equal("https://www.google.com/search?q={0}", s.SearchUrlTemplate);
    }

    [Fact]
    public void Round_trips_a_session()
    {
        var path = File_("sub/session.json");
        JsonFileStore.Save(path, new Session { Urls = ["https://a.test", "https://b.test"], ActiveIndex = 1, SecondaryIndex = 0, Ratio = 0.6 });
        var s = JsonFileStore.Load<Session>(path);
        Assert.Equal(new[] { "https://a.test", "https://b.test" }, s.Urls);
        Assert.Equal(1, s.ActiveIndex);
        Assert.Equal(0, s.SecondaryIndex);
        Assert.Equal(0.6, s.Ratio);
    }

    [Fact]
    public void Corrupt_file_gives_defaults_and_is_kept_aside()
    {
        var path = File_("session.json");
        File.WriteAllText(path, "{ not json");
        var s = JsonFileStore.Load<Session>(path);
        Assert.Empty(s.Urls);
        Assert.False(File.Exists(path));
        Assert.Equal("{ not json", File.ReadAllText(path + ".corrupt"));
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test --filter JsonFileStoreTests`
Expected: échec de compilation, `JsonFileStore` introuvable.

- [ ] **Step 3: Implémenter**

`src/Browser.Core/JsonFileStore.cs` :

```csharp
using System.Text.Json;

namespace Browser.Core;

public sealed class Settings
{
    public string SearchUrlTemplate { get; set; } = "https://www.google.com/search?q={0}";
    public bool BlurEnabled { get; set; } = true;
    public string HomeUrl { get; set; } = "about:blank";
}

public sealed class Session
{
    public List<string> Urls { get; set; } = new();
    public int ActiveIndex { get; set; }
    public int? SecondaryIndex { get; set; }
    public double Ratio { get; set; } = 0.5;
}

public static class JsonFileStore
{
    public static T Load<T>(string path) where T : new()
    {
        if (!File.Exists(path)) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? new T();
        }
        catch (JsonException)
        {
            File.Move(path, path + ".corrupt", overwrite: true);
            return new T();
        }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value));
        File.Move(tmp, path, overwrite: true);
    }
}
```

`src/Browser.Core/AppPaths.cs` :

```csharp
namespace Browser.Core;

public static class AppPaths
{
    public static string DataDir { get; } =
        Environment.GetEnvironmentVariable("NAVIGATEUR_DATA_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "projet-navigateur");

    public static string Settings => Path.Combine(DataDir, "settings.json");
    public static string Session => Path.Combine(DataDir, "session.json");
    public static string Database => Path.Combine(DataDir, "browser.db");
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test`
Expected: tous les tests réussis (45 au total).

- [ ] **Step 5: Commit**

```bash
git add src/Browser.Core tests/Browser.Core.Tests/JsonFileStoreTests.cs
git commit -m "feat: settings, session and data directory"
```

---

## Suite : Plan B

À écrire après la Tâche 3, une fois le type de fenêtre décidé. Il couvre ce qui reste de la spec :

- Fenêtre hôte sans bordure, création et empilement des vues web, séparation de sécurité (interface limitée aux fichiers locaux, aucune écoute des vues de pages).
- Branchement de `Browser.Core` sur WebView2 : navigation, événements de titre/URL/progression, téléchargements, pages d'erreur, récupération après plantage d'une page ou de la vue d'interface, runtime WebView2 absent.
- Interface React/Vite : barre de titre aux trois boutons ronds, onglets, barre d'adresse avec suggestions, panneaux historique/favoris/téléchargements, vue scindée, thème en variables CSS.
- Menu contextuel maison, raccourcis clavier, flou injecté avec réglage et coupure sur les onglets non visibles, restauration de session.
