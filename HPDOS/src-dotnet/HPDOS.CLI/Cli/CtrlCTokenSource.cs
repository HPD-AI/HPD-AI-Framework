namespace HPDOS.Shell.Cli;

/// <summary>
/// Creates a CancellationTokenSource that is cancelled when the user presses Ctrl+C.
/// The CancelKeyPress handler is self-unregistering and sets e.Cancel = true to prevent
/// process termination — the token is the sole exit signal.
/// </summary>
internal static class CtrlCTokenSource
{
    public static CancellationTokenSource Create()
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += Handler;
        return cts;

        void Handler(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true; // prevent OS from killing the process
            cts.Cancel();
            Console.CancelKeyPress -= Handler;
        }
    }
}
