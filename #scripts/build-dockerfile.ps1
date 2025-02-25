Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

$SolutionRoot = Resolve-Path "$PSScriptRoot/.."

Write-Output 'git submodule update --recursive --init'
git submodule update --recursive --init

&dotnet build "source\#external\Mobius.ILasm\Mobius.ILASM\Mobius.ILasm.csproj" -c Release

Write-Output 'dotnet publish source/WebApp.Server/WebApp.Server.csproj ...'
dotnet publish source/WebApp.Server/WebApp.Server.csproj -c Release --runtime linux-x64 -f "net9.0" -p:ErrorOnDuplicatePublishOutputFiles=false
if ($LastExitCode -ne 0) { throw "dotnet publish exited with code $LastExitCode" }
$webAppPublishRoot = 'source/WebApp.Server/bin/Release/net9.0/linux-x64/publish'
Write-Output "Compress-Archive -Path $webAppPublishRoot/* -DestinationPath $SolutionRoot/WebApp.Server.zip"
Compress-Archive -Force -Path "$webAppPublishRoot/*" -DestinationPath "$SolutionRoot/WebApp.Server.zip"

&docker build -t sharplab_webapp_server -f $SolutionRoot/source/WebApp.Server/dockerfile $SolutionRoot
&docker build -t sharplab_webapp -f $SolutionRoot/source/WebApp/dockerfile $SolutionRoot/source