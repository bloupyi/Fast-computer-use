$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src = "$PSScriptRoot\FastEngine.cs"
$out = "$PSScriptRoot\FastEngine.exe"
$refs = @(
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\UIAutomationClient.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\UIAutomationTypes.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Web.Extensions.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll"
)

$refArgs = $refs | ForEach-Object { "-r:$_" }
$args = @("-nologo", "-optimize+", "-platform:x64", "-out:$out") + $refArgs + @($src)

Write-Host "Compiling $src to $out..."
& $csc $args

if ($LASTEXITCODE -eq 0 -and (Test-Path $out)) {
    Write-Host "Build succeeded: $out"
} else {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit 1
}
