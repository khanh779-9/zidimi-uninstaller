using System;
using System.Collections.Generic;

namespace zidimi_uninstaller.Models;

public sealed class InstallMonitorSnapshot
{
    public DateTime CapturedAt { get; init; } = DateTime.Now;
    public Dictionary<string, string> Applications { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> RegistryKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<InstallLogArtifact> WindowsArtifacts { get; init; } = new();
}
