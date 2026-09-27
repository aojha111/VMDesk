$ep = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Loopback, 3389)
$listener = New-Object System.Net.Sockets.TcpListener($ep)
$listener.Start()
Write-Host "sink listening on 127.0.0.1:3389"
while ($true) {
    $c = $listener.AcceptTcpClient()
    Write-Host ("accepted from " + $c.Client.RemoteEndPoint)
    Start-Sleep -Milliseconds 400
    $c.Close()
}
