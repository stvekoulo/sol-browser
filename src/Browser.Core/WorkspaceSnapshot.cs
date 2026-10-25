namespace Browser.Core;

public sealed record WorkspaceSnapshot(string Id, string Name, DateTimeOffset Updated, Session Session, string[] ComparisonDevices);
