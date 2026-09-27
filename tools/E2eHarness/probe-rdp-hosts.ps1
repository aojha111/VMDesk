foreach ($h in 'rdpview.tv', 'www.rdpview.tv', 'termsrv.ru') {
    try {
        $r = Test-NetConnection $h -Port 3389 -WarningAction SilentlyContinue
        Write-Host ("{0} -> {1} tcp3389={2}" -f $h, $r.RemoteAddress, $r.TcpTestSucceeded)
    } catch {
        Write-Host ("{0} ERR {1}" -f $h, $_.Exception.Message)
    }
}
