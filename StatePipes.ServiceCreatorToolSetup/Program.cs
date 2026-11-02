// See https://aka.ms/new-console-template for more information
using StatePipes.ServiceCreatorToolSetup;

var plugin = new ClaudePluginSetup(new ProcessCommandRunner(), ClaudePluginSetup.FindClaude(), ClaudePluginSetup.InstalledMarketplaceDir(), Console.Out);
// The uninstaller runs --remove-plugin before it deletes these files
if (args.Contains("--remove-plugin", StringComparer.OrdinalIgnoreCase))
{
    try { plugin.Remove(); }
    catch (Exception e) { Console.WriteLine($"Claude Code plugin removal failed: {e.Message}"); }
    return;
}
// Each step runs even if the other fails, so a Claude Code problem never stops the Visual Studio or NuGet setup
try { SetupEnvironment.Setup(); }
catch (Exception e) { Console.WriteLine($"Visual Studio and NuGet setup failed: {e.Message}"); }
try { plugin.Register(); }
catch (Exception e) { Console.WriteLine($"Claude Code plugin setup failed: {e.Message}"); }
