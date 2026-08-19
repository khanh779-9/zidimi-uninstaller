using System.IO;
using zidimi_uninstaller.Services;

namespace zidimi_uninstaller.Models;

public enum ForceTargetSource
{
    DragDrop,
    HunterMode
}

/// <summary>
/// A normalized target selected outside of the normal Installed Apps list.
/// Both drag/drop and Hunter Mode resolve into this model so the removal
/// workflow can share the same safety checks and confirmation UI.
/// </summary>
public sealed class ForceTargetResolution
{
    public ForceTargetSource Source { get; init; }
    public string InputPath { get; init; } = string.Empty;
    public string RemovalPath { get; init; } = string.Empty;
    public ApplicationEntry? Application { get; init; }
    public int ConfidenceScore { get; init; }
    public string Evidence { get; init; } = string.Empty;
    public int? ProcessId { get; init; }
    public string WindowTitle { get; init; } = string.Empty;
    public bool IsSafeTarget { get; init; }
    public string SafetyReason { get; init; } = string.Empty;

    public bool IsRegisteredApplication => Application != null;
    public bool CanForceRemove => IsSafeTarget && !string.IsNullOrWhiteSpace(RemovalPath);

    public string DisplayName
    {
        get
        {
            if (Application != null)
                return Application.DisplayName;

            try
            {
                var trimmed = InputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var name = Path.GetFileName(trimmed);
                return string.IsNullOrWhiteSpace(name) ? InputPath : name;
            }
            catch
            {
                return InputPath;
            }
        }
    }

    public string Publisher => Application?.Publisher ?? string.Empty;

    public string ConfidenceText => IsRegisteredApplication
        ? string.Format(LanguageManager.T("Apps_TargetConfidence", "{0}% app match"), Math.Clamp(ConfidenceScore, 0, 100))
        : LanguageManager.T("Apps_TargetDirect", "Direct path target");

    public string SourceText => Source switch
    {
        ForceTargetSource.HunterMode => LanguageManager.T("Apps_TargetSourceHunter", "Hunter Mode"),
        _ => LanguageManager.T("Apps_TargetSourceDrop", "Drag & drop")
    };

    public string TargetTypeText
    {
        get
        {
            if (Directory.Exists(InputPath))
                return LanguageManager.T("Apps_TargetFolder", "Folder");
            if (File.Exists(InputPath))
                return LanguageManager.T("Apps_TargetFile", "File");
            return LanguageManager.T("Apps_TargetUnknown", "Target");
        }
    }
}
