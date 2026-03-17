using HPDOS.Shell.Bridge;
using HPDOS.Shell.Server;
using HPDOS.Shell.Shell;

namespace HPDOS.Shell;

public class App : Application
{
    readonly HPDOSBridge _bridge;

    public App(HPDOSBridge bridge)
    {
        _bridge = bridge;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var page = new MainPage(_bridge);

        var window = new Window(page) { Title = "HPDOS" };

#if MACCATALYST
        // On macCatalyst, Width/Height alone have no effect.
        // Must set Min = Max to force the size.
        window.MinimumWidth  = 1280;
        window.MaximumWidth  = 1280;
        window.MinimumHeight = 800;
        window.MaximumHeight = 800;
#endif

        window.Destroying += (_, _) =>
        {
            ShellConfig.RequestShutdown?.Invoke();
            _ = GUIMode.StopServerAsync();
        };

        return window;
    }
}
