using HPDOS.Shell.Bridge;
using HPDOS.Shell.Shell;

namespace HPDOS.Shell;

public class MainPage : ContentPage
{
    readonly HybridWebView _webView;

    public MainPage(HPDOSBridge bridge)
    {
        _webView = new HybridWebView
        {
            HybridRoot  = "wwwroot",
            DefaultFile = "index.html",
            BackgroundColor = Color.FromArgb("#0F0F17"),
        };

        _webView.WebViewInitialized += OnWebViewInitialized;
        _webView.RawMessageReceived += OnRawMessageReceived;
        _webView.SetInvokeJavaScriptTarget(bridge);

        Content = _webView;
    }

    async void OnWebViewInitialized(object? sender, WebViewInitializedEventArgs e)
    {
        await _webView.EvaluateJavaScriptAsync(
            $"window.__HPDOS_API_BASE = '{ShellConfig.ActiveUrl}';");
    }

    void OnRawMessageReceived(object? sender, HybridWebViewRawMessageReceivedEventArgs _)
    {
    }
}
