param([string]$Toolchain,[string]$Configuration="Release")
$ErrorActionPreference="Stop"
Set-Location -LiteralPath $PSScriptRoot
if (!$Toolchain) {throw "Pass -Toolchain with the path of an x64 llvm-mingw distribution."}
$bin=Join-Path $Toolchain "bin"
$env:Path="$bin;$env:Path"
New-Item -ItemType Directory -Force build,dist | Out-Null
$publish=Join-Path (Resolve-Path build) ("publish-"+[Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory $publish | Out-Null
& "$bin/clang.exe" -O2 -DNDEBUG -Inative/vendor -c native/miniaudio_impl.c -o build/miniaudio.o
if($LASTEXITCODE){throw "miniaudio compilation failed"}
$sources=@("native/api.cpp","native/processor.cpp","native/callbacks.cpp","native/device_manager.cpp","native/gui/logger.cpp","native/gui/bridge_service.cpp")
& "$bin/clang++.exe" -std=c++17 -O2 -DNDEBUG -DNOMINMAX -Inative/vendor -shared -static @sources build/miniaudio.o -lole32 -luuid -luser32 -o "$publish/RouteEngine.dll"
if($LASTEXITCODE){throw "Native compilation failed"}
& "$bin/clang++.exe" -std=c++17 -O2 -static native/processor.cpp tests/processor_check.cpp -o build/processor_check.exe
if($LASTEXITCODE){throw "DSP test compilation failed"}
& ./build/processor_check.exe
if($LASTEXITCODE){throw "DSP regression test failed"}
dotnet publish app/Hesheng.csproj -c $Configuration -r win-x64 --self-contained true -o $publish --nologo -p:DebugType=None
if($LASTEXITCODE){throw "WPF publish failed"}
Copy-Item LICENSE "$publish/LICENSE-WASAPI-Bridge.txt"
Copy-Item native/vendor/LICENSE "$publish/LICENSE-miniaudio.txt"
Copy-Item README.md "$publish/README.md"
$assets=Get-Content -Raw app/obj/project.assets.json | ConvertFrom-Json
$runtime=Get-Content -Raw "$publish/Hesheng.runtimeconfig.json" | ConvertFrom-Json
foreach($framework in $runtime.runtimeOptions.includedFrameworks){
    $id=$framework.name.ToLowerInvariant()+".runtime.win-x64"
    $package=$assets.packageFolders.PSObject.Properties.Name |
        ForEach-Object { Join-Path $_ "$id/$($framework.version)" } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if(!$package){throw "Runtime notices missing for $id"}
    Get-ChildItem -LiteralPath $package -File |
        Where-Object Name -match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' |
        ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $publish "$id-$($_.Name)")}
}
$version=([xml](Get-Content -Raw app/Hesheng.csproj)).Project.PropertyGroup.Version
$zip=Join-Path (Resolve-Path dist) "Hesheng-$version-win-x64.zip"
tar -caf "$zip.tmp.zip" -C $publish .
if($LASTEXITCODE){throw "Creating release archive failed"}
Move-Item -LiteralPath "$zip.tmp.zip" -Destination $zip -Force
Write-Output "Portable release: $zip"
