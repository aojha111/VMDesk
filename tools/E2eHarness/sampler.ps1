$deadline = (Get-Date).AddSeconds(45)
$out = 'C:\MyProjects\VM-View\tools\E2eHarness\sockets-during-dial.log'
"" | Out-File $out
while ((Get-Date) -lt $deadline) {
    $procs = Get-Process VMDesk -ErrorAction SilentlyContinue
    foreach ($p in $procs) {
        $conns = Get-NetTCPConnection -OwningProcess $p.Id -ErrorAction SilentlyContinue |
            Where-Object { $_.State -ne 'Listen' }
        foreach ($c in $conns) {
            ("TCP {0}:{1} -> {2}:{3} {4}" -f $c.LocalAddress, $c.LocalPort, $c.RemoteAddress, $c.RemotePort, $c.State) | Out-File $out -Append
        }
        $udp = Get-NetUDPEndpoint -OwningProcess $p.Id -ErrorAction SilentlyContinue
        foreach ($u in $udp) {
            ("UDP {0}:{1}" -f $u.LocalAddress, $u.LocalPort) | Out-File $out -Append
        }
    }
    Start-Sleep -Milliseconds 700
}
Write-Host "sampler done, $(Get-Content $out | Measure-Object -Line | Select-Object -ExpandProperty Lines) lines"
