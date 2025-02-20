## Copyright (c) Microsoft Corporation.
## Licensed under the MIT License.

#Requires -Version 7.2

$metadata = Get-Content $PSScriptRoot/tools/metadata.json | ConvertFrom-Json
$dotnetSDKVersion = $(Get-Content $PSScriptRoot/global.json | ConvertFrom-Json).Sdk.Version
$dotnetLocalDir = if ($IsWindows) { "$env:LocalAppData\Microsoft\dotnet" } else { "$env:HOME/.dotnet" }

function Start-Build
{
    [CmdletBinding()]
    param (
        [Parameter()]
        [ValidateSet('Debug', 'Release')]
        [string] $Configuration = "Debug",

        [Parameter()]
        [ValidateSet('win-x86', 'win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
        [string] $Runtime = [NullString]::Value,

        [Parameter()]
        [ValidateSet('openai-gpt', 'msaz', 'interpreter', 'ollama')]
        [string[]] $AgentToInclude,

        [Parameter()]
        [switch] $Clean,

        [Parameter()]
        [switch] $NotIncludeModule
    )

    $ErrorActionPreference = 'Stop'
    $IsReleaseBuild = (Test-Path env:\BUILD_BUILDID) -and (Test-Path env:\BUILD_BUILDNUMBER)

    if ($IsReleaseBuild) {
        Write-Verbose "Building in a OneBranch release pipeline. Non-interactive."
    }

    if (-not $AgentToInclude) {
        $agents = $metadata.AgentsToInclude
        $AgentToInclude = if ($agents -eq "*") {
            $MyInvocation.MyCommand.Parameters["AgentToInclude"].Attributes |
                Where-Object { $_ -is [ValidateSet] } |
                Select-Object -First 1 |
                ForEach-Object ValidValues
        } else {
            $agents.Split(",", [System.StringSplitOptions]::TrimEntries)
            Write-Verbose "Include agents specified in Metadata.json"
        }
    }

    $RID = $Runtime ?? (dotnet --info |
        Select-String '^\s*RID:\s+(\w+-\w+)$' |
        Select-Object -First 1 |
        ForEach-Object { $_.Matches.Groups[1].Value })

    Write-Verbose "Runtime: $RID"
    Write-Verbose "Agents: $($AgentToInclude -join ",")"
    Write-Verbose "Build AIShell module? $(-not $NotIncludeModule)"

    $shell_dir = Join-Path $PSScriptRoot "shell"
    $agent_dir = Join-Path $shell_dir "agents"

    $app_dir = Join-Path $shell_dir "AIShell.App"
    $module_dir = Join-Path $shell_dir "AIShell.Integration"

    $openai_agent_dir = Join-Path $agent_dir "AIShell.OpenAI.Agent"
    $msaz_dir = Join-Path $agent_dir "Microsoft.Azure.Agent"
    $interpreter_agent_dir = Join-Path $agent_dir "AIShell.Interpreter.Agent"
    $ollama_agent_dir = Join-Path $agent_dir "AIShell.Ollama.Agent"

    $config = $Configuration.ToLower()
    $out_dir = Join-Path $PSScriptRoot "out"
    $app_out_dir = Join-Path $out_dir $config "app"
    $module_out_dir = Join-Path $out_dir $config "module" "AIShell"

    $openai_out_dir = Join-Path $app_out_dir "agents" "AIShell.OpenAI.Agent"
    $msaz_out_dir = Join-Path $app_out_dir "agents" "Microsoft.Azure.Agent"
    $interpreter_out_dir = Join-Path $app_out_dir "agents" "AIShell.Interpreter.Agent"
    $ollama_out_dir =  Join-Path $app_out_dir "agents" "AIShell.Ollama.Agent"

    if ($Clean) {
        if (Test-Path $out_dir) {
            Write-Verbose "Deleting $out_dir" -Verbose
            Remove-Item -Recurse -Force -Path $out_dir
        }
    }

    Write-Host "`n[Build AI Shell ...]`n" -ForegroundColor Green
    $app_csproj = GetProjectFile $app_dir
    dotnet publish $app_csproj -c $Configuration -o $app_out_dir -r $RID --sc

    ## Move the 'Modules' folder to the appbase folder.
    if ($LASTEXITCODE -eq 0) {
        ## Remove the artifacts that are not for the current platform, to reduce size.
        $otherPlatDir = Join-Path $app_out_dir 'runtimes' ($IsWindows ? 'unix' : 'win')
        if (Test-Path $otherPlatDir -PathType Container) {
            Remove-Item $otherPlatDir -Recurse -Force -ErrorAction Stop
        }

        ## Move the 'Modules' folder and if possible, remove the 'runtimes' folder all together afterward.
        $platDir = Join-Path $app_out_dir 'runtimes' ($IsWindows ? 'win' : 'unix') 'lib'
        if (Test-Path $platDir -PathType Container) {
            $moduleDir = Get-ChildItem $platDir -Directory -Include 'Modules' -Recurse
            if ($moduleDir) {
                ## Remove the existing 'Modules' folder if it already exists.
                $target = Join-Path $app_out_dir 'Modules'
                if (Test-Path $target -PathType Container) {
                    Remove-Item $target -Recurse -Force -ErrorAction Stop
                }

                ## Move 'Modules' folder.
                Move-Item $moduleDir.FullName $app_out_dir -Force -ErrorAction Stop

                ## Remove the 'runtimes' folder if possible.
                $parent = $moduleDir.Parent
                while ($parent.FullName -ne $app_out_dir) {
                    $files = Get-ChildItem $parent.FullName -File -Recurse
                    if (-not $files) {
                        Remove-Item $parent.FullName -Recurse -Force -ErrorAction Stop
                        $parent = $parent.Parent
                    } else {
                        break
                    }
                }
            }
        }
    }

    if ($LASTEXITCODE -eq 0 -and $AgentToInclude -contains 'openai-gpt') {
        Write-Host "`n[Build the OpenAI agent ...]`n" -ForegroundColor Green
        $openai_csproj = GetProjectFile $openai_agent_dir
        dotnet publish $openai_csproj -c $Configuration -o $openai_out_dir
    }

    if ($LASTEXITCODE -eq 0 -and $AgentToInclude -contains 'msaz') {
        Write-Host "`n[Build the Azure agent ...]`n" -ForegroundColor Green
        $msaz_csproj = GetProjectFile $msaz_dir
        dotnet publish $msaz_csproj -c $Configuration -o $msaz_out_dir
    }

    if ($LASTEXITCODE -eq 0 -and $AgentToInclude -contains 'interpreter') {
        Write-Host "`n[Build the Interpreter agent ...]`n" -ForegroundColor Green
        $interpreter_csproj = GetProjectFile $interpreter_agent_dir
        dotnet publish $interpreter_csproj -c $Configuration -o $interpreter_out_dir
    }

    if ($LASTEXITCODE -eq 0 -and $AgentToInclude -contains 'ollama') {
        Write-Host "`n[Build the Ollama agent ...]`n" -ForegroundColor Green
        $ollama_csproj = GetProjectFile $ollama_agent_dir
        dotnet publish $ollama_csproj -c $Configuration -o $ollama_out_dir
    }

    if ($LASTEXITCODE -eq 0 -and -not $NotIncludeModule) {
        Write-Host "`n[Build the AIShell module ...]`n" -ForegroundColor Green
        $aish_module_csproj = GetProjectFile $module_dir
        dotnet publish $aish_module_csproj -c $Configuration -o $module_out_dir
    }

    if ($LASTEXITCODE -eq 0) {
        $shell_path = Join-Path $app_out_dir ($IsWindows ? "aish.exe" : "aish")

        if ($IsReleaseBuild) {
            Write-Host "`nBuild was successful, output path: $shell_path" -ForegroundColor Green
            [PSCustomObject]@{
                App = $app_out_dir
                Module = $module_out_dir
            } | ConvertTo-Json | Out-File "$PSScriptRoot/_build_output_.json"
        } else {
            Set-Clipboard $shell_path
            Write-Host "`nBuild was successful, output path: $shell_path " -NoNewline -ForegroundColor Green
            Write-Host "(copied to clipboard)`n" -ForegroundColor Cyan
        }
    }
}

function GetProjectFile($dir)
{
    return Get-Item "$dir/*.csproj" | ForEach-Object FullName
}

<#
.SYNOPSIS
    Find the dotnet SDK that meets the minimal version requirement.
#>
function Find-Dotnet
{
    $dotnetFile = $IsWindows ? "dotnet.exe" : "dotnet"
    $dotnetPath = Join-Path -Path $dotnetLocalDir -ChildPath $dotnetFile

    # If dotnet is already in the PATH, check to see if that version of dotnet can find the required SDK.
    # This is "typically" the globally installed dotnet.
    $dotnetInPath = Get-Command 'dotnet' -ErrorAction Ignore
    $foundRequiredSDK = $dotnetInPath ? (Test-DotnetSDK $dotnetInPath.Source) : $false

    if (-not $foundRequiredSDK) {
        if ($dotnetInPath.Source -ne $dotnetPath -and (Test-DotnetSDK $dotnetPath)) {
            Write-Warning "Prepending '$dotnetLocalDir' to PATH for the required .NET SDK $dotnetSDKVersion."
            $env:PATH = $dotnetLocalDir + [IO.Path]::PathSeparator + $env:PATH
        } else {
            throw "Cannot find the dotnet SDK with the version $dotnetSDKVersion. Please run 'Install-Dotnet'."
        }
    }
}

<#
.SYNOPSIS
    Check if the dotnet SDK meets the minimal version requirement.
#>
function Test-DotnetSDK
{
    param($dotnetPath)

    if (Test-Path $dotnetPath) {
        $installedVersions = & $dotnetPath --list-sdks | ForEach-Object {
            # this splits strings like
            # '6.0.202 [C:\Program Files\dotnet\sdk]'
            # '7.0.100-preview.2.22153.17 [C:\Users\johndoe\AppData\Local\Microsoft\dotnet\sdk]'
            # into version and path parts.
            ($_ -split '\s',2)[0]
        }

        Write-Verbose "Testing $dotnetPath ..." -Verbose
        Write-Verbose "Installed .NET SDK versions:" -Verbose
        $installedVersions | Write-Verbose -Verbose

        return $installedVersions -contains $dotnetSDKVersion
    }

    return $false
}

<#
.SYNOPSIS
    Install the dotnet SDK if we cannot find an existing one.
#>
function Install-Dotnet
{
    try {
        Find-Dotnet
        # Simply return if we find dotnet SDk with the correct version.
        return
    } catch { }

    Write-Verbose "Require .NET SDK '$dotnetSDKVersion' was not found" -Verbose
    $obtainUrl = "https://dotnet.microsoft.com/download/dotnet/scripts/v1"

    try {
        Remove-Item $dotnetLocalDir -Recurse -Force -ErrorAction Ignore
        $installScript = $IsWindows ? "dotnet-install.ps1" : "dotnet-install.sh"
        Invoke-WebRequest -Uri $obtainUrl/$installScript -OutFile $installScript

        if ($IsWindows) {
            & .\$installScript -Version $dotnetSDKVersion
        } else {
            bash ./$installScript -v $dotnetSDKVersion
        }

        # Try to find the right .NET SDK again.
        Find-Dotnet
    } finally {
        Remove-Item $installScript -Force -ErrorAction Ignore
    }
}

<#
.SYNOPSIS
    Set credential/PAT for write permission in release build, so that any
    new NuGet packages can be pulled into the PowerShell public feed.
#>
function Set-NuGetSourceCred
{
    param(
        [Parameter(Mandatory)]
        [string] $UserName,

        [Parameter(Mandatory)]
        [string] $ClearTextPAT
    )

    $nugetPath = "$PSScriptRoot/shell/nuget.config"

    ## Send pipeline variable to enable the UserName and PAT for the 'PowerShell_PublicPackages' feed.
    $xml = [xml](Get-Content -Path $nugetPath -Raw)
    $url = $xml.configuration.packageSources.add | Where-Object { $_.key -eq 'PowerShell_PublicPackages' } | ForEach-Object value

    $json = @{
        endpointCredentials = @(
            @{
                endpoint = $url
                username = $UserName
                password = $ClearTextPAT
            }
        )
    } | ConvertTo-Json -Compress
    Set-PipelineVariable -Name 'VSS_NUGET_EXTERNAL_FEED_ENDPOINTS' -Value $json
}

function Set-PipelineVariable {
    param(
        [parameter(Mandatory)]
        [string] $Name,
        [parameter(Mandatory)]
        [string] $Value
    )

    $vstsCommandString = "vso[task.setvariable variable=$Name]$Value"
    Write-Verbose -Verbose -Message ("sending " + $vstsCommandString)
    Write-Host "##$vstsCommandString"

    # Also set in the current session
    Set-Item -Path "env:$Name" -Value $Value
}

<#
.SYNOPSIS
    Bootstrap for building.
#>
function Start-Bootstrap
{
    # Verify if the required version of .NET SDK is available, and install it if not.
    Install-Dotnet
}

<#
.SYNOPSIS
    Copy all the 1st-party files to be signed to the target location, in the same relative folder structure.
#>
function Copy-1PFilesToSign
{
    param(
        [Parameter()]
        [string] $SourceRoot,
        [Parameter()]
        [string] $TargetRoot
    )

    $pattern = "*.ps*1", "AIShell.*.dll", "aish.dll", "aish.exe", "Markdown.VT.dll", "ReadLine.dll"

    if (Test-Path $TargetRoot) {
        Remove-Item -Path $TargetRoot -Recurse -Force
    }
    $null = New-Item -ItemType Directory -Path $TargetRoot -Force

    $SourceRoot = (Resolve-Path $SourceRoot).Path
    $TargetRoot = (Resolve-Path $TargetRoot).Path

    Push-Location $SourceRoot
    $filesToSign = Get-ChildItem $pattern -Recurse

    Write-Verbose "List all files to be signed:" -Verbose
    $filesToSign | Out-String -Width 500 -Stream | Write-Verbose -Verbose

    foreach ($file in $filesToSign) {
        $parent = $file.DirectoryName

        if ($parent -eq $SourceRoot) {
            Copy-Item -Path $file.FullName -Destination $TargetRoot
        } else {
            $targetParent = $parent.Replace($SourceRoot, $TargetRoot)
            if (-not (Test-Path $targetParent)) {
                $null = mkdir -Path $targetParent
            }
            Copy-Item -Path $file.FullName -Destination $targetParent
        }
    }
    Pop-Location

    Write-Verbose "Copy is done. List all files that were copied:" -Verbose
    Get-ChildItem $TargetRoot -Recurse -File | Out-String -Width 500 -Stream | Write-Verbose -Verbose
}

function Copy-3PFilesToSign
{
    param(
        [Parameter()]
        [string] $SourceRoot,
        [Parameter()]
        [string] $TargetRoot
    )

    if (Test-Path $TargetRoot) {
        Remove-Item -Path $TargetRoot -Recurse -Force
    }
    $null = New-Item -ItemType Directory -Path $TargetRoot -Force

    $SourceRoot = (Resolve-Path $SourceRoot).Path
    $TargetRoot = (Resolve-Path $TargetRoot).Path

    Push-Location $SourceRoot
    $unsigned = Get-ChildItem *.dll, *.exe -Recurse | Where-Object {
        $signature = Get-AuthenticodeSignature -FilePath $_.FullName
        $signature.Status -eq 'notsigned' -or $signature.SignerCertificate.Issuer -notmatch '^CN=Microsoft.*'
    }

    Write-Verbose "List all files to be signed:" -Verbose
    $unsigned | Out-String -Width 500 -Stream | Write-Verbose -Verbose

    foreach ($file in $unsigned) {
        $parent = $file.DirectoryName

        if ($parent -eq $SourceRoot) {
            Copy-Item -Path $file.FullName -Destination $TargetRoot
        } else {
            $targetParent = $parent.Replace($SourceRoot, $TargetRoot)
            if (-not (Test-Path $targetParent)) {
                $null = mkdir -Path $targetParent
            }
            Copy-Item -Path $file.FullName -Destination $targetParent
        }
    }
    Pop-Location

    Write-Verbose "Copy is done. List all files that were copied:" -Verbose
    Get-ChildItem $TargetRoot -Recurse -File | Out-String -Width 500 -Stream | Write-Verbose -Verbose
}

<#
.SYNOPSIS
    Copy all the signed files back to overwrite the original unsigned files.
#>
function Copy-SignedFileBack
{
    param(
        [Parameter()]
        [string] $SourceRoot,
        [Parameter()]
        [string] $TargetRoot
    )

    $SourceRoot = (Resolve-Path $SourceRoot).Path
    $TargetRoot = (Resolve-Path $TargetRoot).Path

    Push-Location $SourceRoot
    $signedFiles = Get-ChildItem -Recurse -File

    Write-Verbose "List all signed files:" -Verbose
    $signedFiles | Out-String -Width 500 -Stream | Write-Verbose -Verbose

    $dests = [System.Collections.Generic.List[string]]::new()
    foreach ($file in $signedFiles) {
        $parent = $file.DirectoryName
        $leaf = $file.Name

        if ($parent -eq $SourceRoot) {
            $destPath = Join-Path $TargetRoot $leaf
            Copy-Item -Path $file.FullName -Destination $destPath -Force
        } else {
            $targetParent = $parent.Replace($SourceRoot, $TargetRoot)
            $destPath = Join-Path $targetParent $leaf
            Copy-Item -Path $file.FullName -Destination $destPath -Force
        }

        $dests.Add($destPath)
    }

    Write-Verbose "Copy is done. List all copied-to files:" -Verbose
    $dests | Write-Verbose -Verbose
}

<#
.SYNOPSIS
    Run 'dotnet restore' for all the target runtime that we are interested in to
    update packages on the CFS feed.
    This needs to be run on a MS employee's dev machine whenever there is update
    to the NuGet packages used in PSReadLine repo, so that the package and all its
    dependencies can be pull into the CFS feed from upstream feed.
#>
function Update-CFSFeed
{
    $rids = @('win-x86', 'win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')

    Write-Host "1. clear all NuGet caches on the local machine." -ForegroundColor Green
    dotnet nuget locals all -c

    $projFiles = Get-ChildItem $PSScriptRoot/*.csproj -Recurse | ForEach-Object FullName

    Write-Host "2. restore for target runtimes." -ForegroundColor Green
    foreach ($rid in $rids) {
        Write-Host "  - $rid" -ForegroundColor Green
        foreach ($file in $projFiles) {
            dotnet restore -r $rid $file
        }
    }
}

Export-ModuleMember -Function Start-Build, Find-Dotnet, Install-Dotnet, Set-NuGetSourceCred, Start-Bootstrap, Copy-1PFilesToSign, Copy-3PFilesToSign, Copy-SignedFileBack, Update-CFSFeed
