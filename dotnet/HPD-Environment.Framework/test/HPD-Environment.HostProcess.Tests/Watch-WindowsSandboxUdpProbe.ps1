param(
    [Parameter(Mandatory)]
    [guid] $RunId,
    [ipaddress] $Address = '0.0.0.0',
    [int] $Port = 50753,
    [int] $TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$listener = [Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new($Address, $Port))
try {
    $prefix = $RunId.ToString('N')
    $markers = [Collections.Generic.HashSet[string]]::new()
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $settle = $deadline
    Write-Output "Listening on $($listener.Client.LocalEndPoint) for run $prefix"
    while ([DateTime]::UtcNow -lt $deadline -and [DateTime]::UtcNow -lt $settle) {
        if (!$listener.Client.Poll(100000, [Net.Sockets.SelectMode]::SelectRead)) { continue }
        $peer = [Net.IPEndPoint]::new($Address, 0)
        $packet = [Text.Encoding]::ASCII.GetString($listener.Receive([ref]$peer))
        if ($packet -eq "${prefix}:sandbox") {
            throw "The receiver got the sandbox datagram from $peer."
        }
        if ($packet -in "${prefix}:before", "${prefix}:after") {
            if ($markers.Add($packet) -and $markers.Count -eq 2) {
                $settle = [DateTime]::UtcNow.AddSeconds(2)
            }
        }
    }
    if ($markers.Count -ne 2 -or [DateTime]::UtcNow -lt $settle) {
        throw 'The receiver did not observe both controls and a full observation window.'
    }
    Write-Output 'Both host controls arrived. No sandbox datagram arrived.'
}
finally {
    $listener.Dispose()
}
