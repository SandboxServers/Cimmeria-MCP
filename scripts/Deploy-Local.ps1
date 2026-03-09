param(
    [Parameter(Mandatory)][string]$AppName
)

Push-Location "$PSScriptRoot/../src/CimmeriaMcp.Functions"
try {
    dotnet publish -c Release -o ./publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    func azure functionapp publish $AppName --dotnet-isolated
    if ($LASTEXITCODE -ne 0) { throw "func publish failed" }

    Write-Host "Deployed to $AppName successfully." -ForegroundColor Green
    Write-Host "MCP endpoint: https://$AppName.azurewebsites.net/runtime/webhooks/mcp"
}
finally {
    Pop-Location
}
