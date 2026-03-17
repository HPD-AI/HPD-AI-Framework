using HPDOS.Shell.Cli;
using HPDOS.Shell.Shell;

if (args is ["gui"] || args is ["gui", ..])
    return await GUIMode.RunBrowserAsync();

if (args.Length == 0)
    return await CliRouter.RunAsync(["chat"]);

if (args is ["backend"] || args is ["backend", ..])
    return await GUIMode.RunBackendAsync();

return await CliRouter.RunAsync(args);
