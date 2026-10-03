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
