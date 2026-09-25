using CmlLib.Core.Installers;
using CmlLib.Core.Rules;

namespace CmlLib.Core.Installer.NeoForge.Installers;

public class NeoForgeInstallOptions
{
    public string? JavaPath { get; set; }
    public RulesEvaluatorContext RulesContext { get; set; } = new RulesEvaluatorContext(LauncherOSRule.Current);
    public IProgress<InstallerProgressChangedEventArgs>? FileProgress { get; set; }
    public IProgress<ByteProgress>? ByteProgress { get; set; }
    public IProgress<string>? InstallerOutput { get; set; }
    public CancellationToken CancellationToken { get; set; }
    public bool SkipIfAlreadyInstalled { get; set; } = true;
}