using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace HPDOS.Shell.Bridge;

/// <summary>
/// Bridge class exposed to JavaScript via HybridWebView.
/// All public methods are callable from JS via window.HybridWebView.InvokeDotNet()
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class HPDOSBridge
{
    public string Ping() => "pong";
}
