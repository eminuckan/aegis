#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$false)]
    [string]$Version = "1.0.0",
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipPack = $false,
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipPublish = $false
)

Write-Host "🚀 Building Aegis Permission Scan Tool v$Version" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green

# Clean previous builds
if (Test-Path "bin") { Remove-Item -Recurse -Force "bin" }
if (Test-Path "publish") { Remove-Item -Recurse -Force "publish" }
if (Test-Path "obj") { Remove-Item -Recurse -Force "obj" }

# Update version in csproj
Write-Host "📝 Updating version to $Version..." -ForegroundColor Yellow
$csprojContent = Get-Content "SyncPermissions.csproj"
$csprojContent = $csprojContent -replace '<Version>.*</Version>', "<Version>$Version</Version>"
$csprojContent | Set-Content "SyncPermissions.csproj"

# Build and create NuGet package
if (-not $SkipPack) {
    Write-Host "📦 Creating NuGet package..." -ForegroundColor Yellow
    dotnet pack --configuration Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "✅ NuGet package created successfully!" -ForegroundColor Green
}

# Create self-contained executables
if (-not $SkipPublish) {
    Write-Host "🔧 Creating self-contained executables..." -ForegroundColor Yellow
    
    $platforms = @(
        @{ Runtime = "win-x64"; Folder = "windows"; Extension = ".exe" },
        @{ Runtime = "linux-x64"; Folder = "linux"; Extension = "" },
        @{ Runtime = "osx-x64"; Folder = "macos"; Extension = "" }
    )
    
    foreach ($platform in $platforms) {
        Write-Host "  Building for $($platform.Runtime)..." -ForegroundColor Cyan
        dotnet publish -c Release -r $platform.Runtime --self-contained -p:PublishSingleFile=true -o "publish/$($platform.Folder)"
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        
        # Rename executable to aegis
        $oldName = "publish/$($platform.Folder)/SyncPermissions$($platform.Extension)"
        $newName = "publish/$($platform.Folder)/aegis$($platform.Extension)"
        if (Test-Path $oldName) {
            Move-Item $oldName $newName
        }
    }
    Write-Host "✅ Self-contained executables created successfully!" -ForegroundColor Green
}

Write-Host ""
Write-Host "🎉 Build completed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "📋 Available outputs:" -ForegroundColor White
if (-not $SkipPack) {
    Write-Host "  • NuGet Package: bin/Release/Aegis.PermissionScan.$Version.nupkg" -ForegroundColor Gray
    Write-Host "    Install with: dotnet tool install -g --add-source bin/Release Aegis.PermissionScan" -ForegroundColor Gray
}
if (-not $SkipPublish) {
    Write-Host "  • Windows:       publish/windows/aegis.exe" -ForegroundColor Gray
    Write-Host "  • Linux:         publish/linux/aegis" -ForegroundColor Gray
    Write-Host "  • macOS:         publish/macos/aegis" -ForegroundColor Gray
}  
 