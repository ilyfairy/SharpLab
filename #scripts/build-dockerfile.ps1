Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

$SolutionRoot = Resolve-Path "$PSScriptRoot/.."

Write-Output 'git submodule update --recursive --init'
git submodule update --recursive --init

&dotnet build "source\#external\Mobius.ILasm\Mobius.ILASM\Mobius.ILasm.csproj" -c Release

Write-Output 'dotnet publish source/WebApp.Server/WebApp.Server.csproj ...'
$webAppServerPublishRoot = "$SolutionRoot/source/WebApp.Server/bin/publish"
dotnet publish source/WebApp.Server/WebApp.Server.csproj -c Release --runtime linux-x64 -f "net8.0" -p:ErrorOnDuplicatePublishOutputFiles=false --output $webAppServerPublishRoot
if ($LastExitCode -ne 0) { throw "dotnet publish exited with code $LastExitCode" }

Write-Output 'dotnet publish source/Container.Docker.Manager/Container.Docker.Manager.csproj ...'
$containerManagerPublishRoot = "$SolutionRoot/source/Container.Docker.Manager/bin/publish"
dotnet publish source/Container.Docker.Manager/Container.Docker.Manager.csproj -c Release --runtime linux-x64 -f "net9.0" /p:PublishSingleFile=true --self-contained true --output $containerManagerPublishRoot
if ($LastExitCode -ne 0) { throw "dotnet publish exited with code $LastExitCode" }

Write-Output 'dotnet publish source/Container./Container.csproj ...'
$containerPublishRoot = "$SolutionRoot/source/Container/bin/publish"
dotnet publish source/Container/Container.csproj -c Release --runtime linux-x64 -f "net8.0" --output $containerPublishRoot
if ($LastExitCode -ne 0) { throw "dotnet publish exited with code $LastExitCode" }

&docker build -t sharplab_webapp_server_const_generics -f $SolutionRoot/source/WebApp.Server/dockerfile $SolutionRoot
# &docker build -t sharplab_webapp -f $SolutionRoot/source/WebApp/dockerfile $SolutionRoot/source
&docker build -t sharplab_container_const_generics -f $SolutionRoot/source/Container/dockerfile $SolutionRoot
&docker build -t sharplab_container_manager_const_generics -f $SolutionRoot/source/Container.Docker.Manager/dockerfile $SolutionRoot