using CmlLib.Core.Installers;
using CmlLib.Core.Rules;

namespace CmlLib.Core.Installer.Forge;

public class ForgeInstallOptions
{
    public string? JavaPath { get; set; }
    public IRulesEvaluator RulesEvaluator { get; set; } = new RulesEvaluator();
    public RulesEvaluatorContext RulesContext { get; set; } = new RulesEvaluatorContext(LauncherOSRule.Current);
    public IProgress<InstallerProgressChangedEventArgs>? FileProgress { get; set; }
    public IProgress<ByteProgress>? ByteProgress { get; set; }
    public IProgress<string>? InstallerOutput { get; set; }
    public CancellationToken CancellationToken { get; set; }
    public bool SkipIfAlreadyInstalled { get; set; } = true;
}