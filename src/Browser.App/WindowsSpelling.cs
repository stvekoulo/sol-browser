using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Browser.Core;

namespace Browser.App;

// Windows fournit les dictionnaires ; la saisie reste en mémoire sur cette machine.
internal sealed class WindowsSpelling
{
    private readonly Dictionary<string, IChecker> _checkers = new();
    public WindowsSpelling()
    {
        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC"))!);
            var factory = (IFactory)instance!;
            var available = new List<string>();
            var languages = factory.Languages();
            try
            {
                var buffer = new string[1];
                while (languages.Next(1, buffer, IntPtr.Zero) == 0) available.Add(buffer[0]);
            }
            finally { Marshal.ReleaseComObject(languages); }
            foreach (var prefix in new[] { "fr", "en" })
            {
                var language = available.FirstOrDefault(l => l.Equals(prefix == "fr" ? "fr-FR" : "en-US", StringComparison.OrdinalIgnoreCase))
                    ?? available.FirstOrDefault(l => l.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase));
                if (language is not null && factory.IsSupported(language)) _checkers[language] = factory.Create(language);
            }
        }
        catch (COMException) { /* Windows sans fournisseur : correction native du moteur conservée. */ }
        finally { if (instance is not null) Marshal.ReleaseComObject(instance); }
    }

    public string[] Languages => _checkers.Keys.ToArray();
    private IEnumerable<IChecker> Selected(int mode) => _checkers.Where(p => mode == 3 || (mode == 1 ? p.Key.StartsWith("fr") : mode == 2 && p.Key.StartsWith("en"))).Select(p => p.Value);
    public record Error(int Start, int Length);
    public Error[] Check(string text, int mode)
    {
        if (text.Length > 8000) return [];
        var checkers = Selected(mode).ToArray();
        if (checkers.Length == 0) return [];
        var errors = Errors(checkers[0], text);
        // En bilingue, un mot reconnu par l'un des dictionnaires est accepté.
        foreach (var checker in checkers.Skip(1))
        {
            var other = Errors(checker, text);
            errors = errors.Where(e => other.Any(o => o.Start < e.Start + e.Length && e.Start < o.Start + o.Length)).ToArray();
        }
        return errors.Where(e => SpellingFilter.Keep(text, e.Start, e.Length)).ToArray();
    }
    private static Error[] Errors(IChecker checker, string text)
    {
        var result = new List<Error>();
        var errors = checker.Check(text);
        try
        {
            while (errors.Next(out var error) == 0 && error is not null)
            {
                try
                {
                    var start = (int)error.Start(); var length = (int)error.Length();
                    if (start >= 0 && length > 0 && start <= text.Length - length) result.Add(new(start, length));
                }
                finally { Marshal.ReleaseComObject(error); }
            }
        }
        finally { Marshal.ReleaseComObject(errors); }
        return result.ToArray();
    }
    public string[] Suggest(string word, int mode)
    {
        if (word.Length is 0 or > 100) return [];
        var result = new List<string>();
        foreach (var checker in Selected(mode))
        {
            var suggestions = checker.Suggest(word);
            try
            {
                var buffer = new string[1];
                while (result.Count < 8 && suggestions.Next(1, buffer, IntPtr.Zero) == 0) result.Add(buffer[0]);
            }
            finally { Marshal.ReleaseComObject(suggestions); }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray();
    }

    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFactory
    {
        IEnumString Languages();
        [return: MarshalAs(UnmanagedType.Bool)] bool IsSupported([MarshalAs(UnmanagedType.LPWStr)] string language);
        IChecker Create([MarshalAs(UnmanagedType.LPWStr)] string language);
    }
    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IChecker
    {
        [return: MarshalAs(UnmanagedType.LPWStr)] string Language();
        IErrors Check([MarshalAs(UnmanagedType.LPWStr)] string text);
        IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);
    }
    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IErrors { [PreserveSig] int Next(out IError error); }
    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IError
    {
        uint Start(); uint Length(); int Action();
        [return: MarshalAs(UnmanagedType.LPWStr)] string Replacement();
    }
}
