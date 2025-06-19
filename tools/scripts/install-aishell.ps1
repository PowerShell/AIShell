# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

<#
.SYNOPSIS
    Installs or uninstalls AI Shell.

.DESCRIPTION
    This script installs the AI Shell application and PowerShell module from GitHub releases.
    It can also configure AI Shell to auto-start when PowerShell is launched in Windows Terminal.

.PARAMETER Version
    Specify the version to install, e.g. 'v1.0.0-preview.2'

.PARAMETER AddToProfile
    Automatically add AI Shell to PowerShell profile for auto-start without prompting.
    Only works on Windows. When used, AI Shell will start automatically when PowerShell
    is opened in Windows Terminal.

.PARAMETER DefaultAgent
    Default agent to use when auto-starting AI Shell. Must be used with -AddToProfile.
    Valid agent names include: openai, ollama, azure, interpreter, phisilica.
    If not specified, AI Shell will prompt for agent selection at startup.

.PARAMETER Uninstall
    Specify this parameter to uninstall AI Shell and remove it from the PowerShell profile.

.EXAMPLE
    .\install-aishell.ps1
    Installs AI Shell and prompts for profile integration.

.EXAMPLE
    .\install-aishell.ps1 -AddToProfile -DefaultAgent openai
    Installs AI Shell and automatically configures it to start with the OpenAI agent.

.EXAMPLE
    .\install-aishell.ps1 -Uninstall
    Uninstalls AI Shell and removes it from the PowerShell profile.

.LINK
    https://aka.ms/AIShell-Docs
#>

#Requires -Version 7.4.6

[CmdletBinding(DefaultParameterSetName = "Install")]
param(
    [Parameter(HelpMessage = "Specify the version to install, e.g. 'v1.0.0-preview.2'", ParameterSetName = "Install")]
    [ValidatePattern("^v\d+\.\d+\.\d+(-\w+\.\d{1,2})?$")]
    [string] $Version,

    [Parameter(HelpMessage = "Automatically add AI Shell to PowerShell profile for auto-start", ParameterSetName = "Install")]
    [switch] $AddToProfile,

    [Parameter(HelpMessage = "Default agent to use when auto-starting AI Shell", ParameterSetName = "Install")]
    [string] $DefaultAgent,

    [Parameter(HelpMessage = "Specify this parameter to uninstall AI Shell", ParameterSetName = "Uninstall")]
    [switch] $Uninstall
)

$Script:MacSymbolicLink = '/usr/local/bin/aish'
$Script:MacInstallationLocation = "/usr/local/AIShell"
$Script:WinInstallationLocation = "$env:LOCALAPPDATA\Programs\AIShell"
$Script:InstallLocation = $null
$Script:PackageURL = $null
$Script:ModuleVersion = $null
$Script:NewPSRLInstalled = $false
$Script:PSRLDependencyMap = @{ '1.0.4-preview4' = '2.4.2-beta2' }

function Resolve-Environment {
    if ($PSVersionTable.PSVersion -lt [version]"7.4.6") {
        throw "PowerShell v7.4.6 or higher is required for using the AIShell module. You can download it at https://github.com/PowerShell/PowerShell/releases/tag/v7.4.6 "
    }
    if ($IsLinux) {
        throw "Sorry, this install script is only compatible with Windows and macOS. If you want to install on Linux, please download the package directly from the GitHub repo at aka.ms/AIShell-Repo."
    }

    ($platShortName, $platFullName, $pkgExt, $Script:InstallLocation) = if ($IsWindows) {
        'win', 'Windows', 'zip', $Script:WinInstallationLocation
    } else {
        'osx', 'macOS', 'tar.gz', $Script:MacInstallationLocation
    }

    if ($Uninstall) {
        return
    }

    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    if ($architecture -notin @('X86', 'X64', 'Arm64')) {
        throw "AI Shell doesn't support the $architecture architecture on $platFullName."
    }

    $tags = (Invoke-RestMethod -Uri "https://api.github.com/repos/PowerShell/AIShell/tags" -ErrorAction Stop).name
    if ($Version -and $Version -notin $tags) {
        throw "The specified version '$Version' doesn't exist. Available versions are: $($tags -join ', ')"
    }

    $tagToUse = [string]::IsNullOrEmpty($Version) ? $tags[0] : $Version
    $appVersion = $tagToUse.TrimStart('v')

    $Script:PackageURL = "https://github.com/PowerShell/AIShell/releases/download/${tagToUse}/AIShell-${appVersion}-${platShortName}-$($architecture.ToLower()).${pkgExt}"

    $dashIndex = $appVersion.IndexOf('-')
    $Script:ModuleVersion = if ($dashIndex -eq -1) {
        ## The mapping between module version and the app version is not clear yet.
        throw "Not implemented for stable releases."
    } else {
        $previewLabel = $appVersion.Substring($dashIndex + 1)
        $previewDigit = $previewLabel.Substring($previewLabel.LastIndexOf('.') + 1)
        $patchDotIndex = $appVersion.LastIndexOf('.', $dashIndex)
        $appVersion.Substring(0, $patchDotIndex) + ".$previewDigit-" + $previewLabel.Replace('.', '')
    }
}

function Install-AIShellApp {
    [CmdletBinding()]
    param()

    $destination = $Script:InstallLocation
    $packageUrl = $Script:PackageURL

    $destinationExists = Test-Path -Path $destination
    if ($destinationExists) {
        $anyFile = Get-ChildItem -Path $destination | Select-Object -First 1
        if ($anyFile) {
            $remove = $PSCmdlet.ShouldContinue("Do you want to remove it for a new installation?", "AI Shell was already installed (or partially installed) at '$destination'.")
            if ($remove) {
                $destinationExists = $false
                if ($IsWindows) {
                    Remove-Item -Path $destination -Recurse -Force -ErrorAction Stop
                } else {
                    sudo rm -rf $destination
                    if ($LASTEXITCODE -ne 0) {
                        throw "Failed to remove '$destination'."
                    }
                }
            } else {
                throw "Operation cancelled. You can remove the current installation by './install-aishell.ps1 -Uninstall' and try again."
            }
        }
    }

    if (-not $destinationExists) {
        # Create the directory if not existing.
        Write-Host "Creating the target folder '$destination' ..."
        if ($IsWindows) {
            $null = New-Item -Path $destination -ItemType Directory -Force
        } else {
            # '/usr/local' requires sudo.
            sudo mkdir $destination
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to create the installation folder '$destination'."
            }
        }
    }

    $fileName = [System.IO.Path]::GetFileName($packageUrl)
    $tempPath = Join-Path ([System.IO.Path]::GetTempPath()) $fileName
    if (Test-Path $tempPath) {
        Remove-Item $tempPath -Force -ErrorAction Stop
    }

    # Download AIShell package.
    Write-Host "Downloading AI Shell package '$fileName' ..."
    Invoke-WebRequest -Uri $packageUrl -OutFile $tempPath -ErrorAction Stop

    try {
        # Extract AIShell package.
        Write-Host "Extracting AI Shell to '$destination' ..."
        Unblock-File -Path $tempPath
        if ($IsWindows) {
            Expand-Archive -Path $tempPath -DestinationPath $destination -Force -ErrorAction Stop

            # Set the process-scope and user-scope Path env variables to include AIShell.
            $envPath = $env:Path
            if (-not $envPath.Contains($destination)) {
                Write-Host "Adding AI Shell app to the Path environment variable ..."
                $env:Path = "${destination};${envPath}"
                $userPath = [Environment]::GetEnvironmentVariable('Path', [EnvironmentVariableTarget]::User)
                $newUserPath = $userPath.EndsWith(';') ? "${userPath}${destination}" : "${userPath};${destination}"
                [Environment]::SetEnvironmentVariable('Path', $newUserPath, [EnvironmentVariableTarget]::User)
            }
        } else {
            sudo tar -xzf $tempPath -C $destination
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to extract '$tempPath' to the folder '$destination'."
            }

            $aishPath = Join-Path $destination 'aish'
            sudo chmod +x $aishPath
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to set the execution permission to the executable '$aishPath'."
            }

            # No need to setup the Path env variable as the symbolic link is already within Path.
            $symlink = $Script:MacSymbolicLink
            if (-not (Test-Path -Path $symlink)) {
                sudo ln -s $aishPath $symlink
                if ($LASTEXITCODE -ne 0) {
                    throw "Failed to create the symbolic link '$symlink' to '$aishPath'."
                }
            }
        }
    } finally {
        if (Test-Path -Path $tempPath) {
            Remove-Item -Path $tempPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Uninstall-AIShellApp {
    $destination = $Script:InstallLocation
    if (Test-Path $destination) {
        Write-Host "Removing AI Shell app from '$destination' ..."
        if ($IsWindows) {
            Remove-Item -Path $destination -Recurse -Force -ErrorAction Stop

            # Update the user-scope Path env variables to remove AIShell.
            $userPath = [Environment]::GetEnvironmentVariable('Path', [EnvironmentVariableTarget]::User)
            if ($userPath.Contains($destination)) {
                Write-Host "Removing AI Shell app from the user-scope Path environment variable ..."
                $newUserPath = $userPath.Split(';', [StringSplitOptions]::RemoveEmptyEntries -bor [StringSplitOptions]::TrimEntries) |
                    Where-Object { $_ -ne $destination } |
                    Join-String -Separator ';'
                [Environment]::SetEnvironmentVariable("Path", $newUserPath, [EnvironmentVariableTarget]::User)
            }

            # Update the process-scope Path env variables to remove AIShell.
            $procPath = $env:Path
            if ($procPath.Contains($destination)) {
                Write-Host "Removing AI Shell app from the process-scope Path environment variable ..."
                $newProcPath = $procPath.Split(';', [StringSplitOptions]::RemoveEmptyEntries -bor [StringSplitOptions]::TrimEntries) |
                    Where-Object { $_ -ne $destination } |
                    Join-String -Separator ';'
                $env:Path = $newProcPath
            }
        } else {
            sudo rm -rf $destination
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to remove the AIShell app from '$destination'."
            }

            $symlink = $Script:MacSymbolicLink
            sudo rm $symlink
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to remove the symbolic link '$symlink'."
            }
        }
    } else {
        Write-Host "AI Shell app was not found at '$destination'. Skip removing it."
    }
}

function Install-AIShellModule {
    $modVersion = $Script:ModuleVersion
    Write-Host "Installing the PowerShell module 'AIShell' $modVersion ..."
    Install-PSResource -Name AIShell -Repository PSGallery -Prerelease -TrustRepository -Version $modVersion -ErrorAction Stop -WarningAction SilentlyContinue

    $psrldep = $Script:PSRLDependencyMap[$modVersion]
    if ($psrldep) {
        $psrlModule = Get-Module -Name PSReadLine
        $psrlVer = $psrldep.Contains('-') ? $psrldep.Split('-')[0] : $psrldep
        if ($null -eq $psrlModule -or $psrlModule.Version -lt [version]$psrlVer) {
            Write-Host "  - This version of AIShell module depends on PSReadLine '$psrldep' or higher, which is missing."
            Write-Host "    Installing the PowerShell module 'PSReadLine' $psrldep or a higher version ..."
            Install-PSResource -Name PSReadLine -Repository PSGallery -Prerelease -TrustRepository -Version "[$psrldep, ]" -ErrorAction Stop -WarningAction SilentlyContinue
            $Script:NewPSRLInstalled = $true
        }
    }

    if ($IsMacOS) {
        Write-Host -ForegroundColor Yellow "NOTE: The 'AIShell' PowerShell module only works in iTerm2 terminal in order to provide the sidecar experience."
    }
}

function Uninstall-AIShellModule {
    if (Get-InstalledPSResource -Name "AIShell" -ErrorAction SilentlyContinue) {
        try {
            Write-Host "Uninstalling AIShell Module ..."
            Uninstall-PSResource -Name AIShell -ErrorAction Stop
        } catch {
            throw "Failed to uninstall the 'AIShell' module. Please check if the module got imported in any active PowerShell session. If so, please exit the session and try this script again."
        }
    }
}

function Get-AvailableAgents {
    # Try to get available agents by importing the module temporarily
    try {
        Import-Module AIShell -Force -ErrorAction Stop
        
        # Get available agents by running Start-AIShell briefly and capturing output
        # Since we can't easily enumerate agents without starting the shell,
        # we'll provide a list of common known agents
        $knownAgents = @(
            @{ Name = "openai"; Description = "OpenAI GPT models" }
            @{ Name = "ollama"; Description = "Ollama local models" }
            @{ Name = "azure"; Description = "Azure OpenAI Service" }
            @{ Name = "interpreter"; Description = "Code interpreter agent" }
            @{ Name = "phisilica"; Description = "Phi Silica agent" }
        )
        
        Remove-Module AIShell -Force -ErrorAction SilentlyContinue
        return $knownAgents
    }
    catch {
        # If module import fails, return basic known agents
        Write-Warning "Could not enumerate agents dynamically. Using known agent list."
        return @(
            @{ Name = "openai"; Description = "OpenAI GPT models" }
            @{ Name = "ollama"; Description = "Ollama local models" }
        )
    }
}

function Add-AIShellToProfile {
    [CmdletBinding()]
    param(
        [string] $DefaultAgent
    )
    
    if (-not $IsWindows) {
        Write-Warning "Profile integration is currently only supported on Windows."
        return
    }
    
    # Validate and sanitize the agent name
    if ($DefaultAgent) {
        # Remove any potentially dangerous characters and validate
        $DefaultAgent = $DefaultAgent -replace '[^a-zA-Z0-9\-_\.]', ''
        if ([string]::IsNullOrWhiteSpace($DefaultAgent)) {
            Write-Warning "Invalid agent name provided. AI Shell will use default agent selection."
            $DefaultAgent = $null
        }
    }
    
    $profilePath = $PROFILE.CurrentUserAllHosts
    $profileDir = Split-Path $profilePath -Parent
    
    # Create profile directory if it doesn't exist
    if (-not (Test-Path $profileDir)) {
        New-Item -Path $profileDir -ItemType Directory -Force | Out-Null
    }
    
    # Define the AI Shell auto-start section with unique markers
    $aiShellSectionStart = "# AI Shell Auto-Start - BEGIN (Added by AIShell installer)"
    $aiShellSectionEnd = "# AI Shell Auto-Start - END (Added by AIShell installer)"
    
    $agentParameter = if ($DefaultAgent) { " -Agent '$DefaultAgent'" } else { "" }
    
    $aiShellCode = @"
$aiShellSectionStart
# Auto-start AI Shell sidecar if in Windows Terminal
if (`$env:WT_SESSION -and (Get-Command Start-AIShell -ErrorAction SilentlyContinue)) {
    try {
        Start-AIShell$agentParameter
    } catch {
        Write-Warning "Failed to auto-start AI Shell: `$_"
    }
}
$aiShellSectionEnd
"@
    
    # Read existing profile content
    $existingContent = ""
    if (Test-Path $profilePath) {
        $existingContent = Get-Content $profilePath -Raw -ErrorAction SilentlyContinue
        
        # Check if AI Shell section already exists
        if ($existingContent -match [regex]::Escape($aiShellSectionStart)) {
            Write-Host "AI Shell auto-start section already exists in profile. Updating..."
            # Remove existing section
            $pattern = [regex]::Escape($aiShellSectionStart) + ".*?" + [regex]::Escape($aiShellSectionEnd)
            $existingContent = [regex]::Replace($existingContent, $pattern, "", [System.Text.RegularExpressions.RegexOptions]::Singleline)
            $existingContent = $existingContent.Trim()
        }
    }
    
    # Add the new AI Shell section
    $newContent = if ($existingContent) {
        $existingContent + "`n`n" + $aiShellCode
    } else {
        $aiShellCode
    }
    
    # Write to profile
    try {
        Set-Content -Path $profilePath -Value $newContent -ErrorAction Stop
        Write-Host "Successfully added AI Shell auto-start to PowerShell profile: $profilePath"
        if ($DefaultAgent) {
            Write-Host "  Default agent: $DefaultAgent"
        }
    }
    catch {
        Write-Error "Failed to update PowerShell profile: $_"
    }
}

function Remove-AIShellFromProfile {
    [CmdletBinding()]
    param()
    
    if (-not $IsWindows) {
        return  # Profile integration only supported on Windows
    }
    
    $profilePath = $PROFILE.CurrentUserAllHosts
    
    if (-not (Test-Path $profilePath)) {
        return  # No profile file exists
    }
    
    try {
        $existingContent = Get-Content $profilePath -Raw -ErrorAction Stop
        
        # Define the markers
        $aiShellSectionStart = "# AI Shell Auto-Start - BEGIN (Added by AIShell installer)"
        $aiShellSectionEnd = "# AI Shell Auto-Start - END (Added by AIShell installer)"
        
        # Check if AI Shell section exists
        if ($existingContent -match [regex]::Escape($aiShellSectionStart)) {
            Write-Host "Removing AI Shell auto-start from PowerShell profile..."
            
            # Remove the AI Shell section
            $pattern = [regex]::Escape($aiShellSectionStart) + ".*?" + [regex]::Escape($aiShellSectionEnd)
            $newContent = [regex]::Replace($existingContent, $pattern, "", [System.Text.RegularExpressions.RegexOptions]::Singleline)
            $newContent = $newContent.Trim()
            
            # Write back to profile
            if ($newContent) {
                Set-Content -Path $profilePath -Value $newContent -ErrorAction Stop
            } else {
                # If profile is now empty, remove the file
                Remove-Item $profilePath -ErrorAction Stop
            }
            
            Write-Host "Successfully removed AI Shell auto-start from PowerShell profile."
        }
    }
    catch {
        Write-Warning "Failed to remove AI Shell from PowerShell profile: $_"
    }
}

function Prompt-ProfileIntegration {
    [CmdletBinding()]
    param()
    
    if (-not $IsWindows) {
        Write-Host "Profile integration is only available on Windows."
        return $false
    }
    
    Write-Host ""
    Write-Host -ForegroundColor Cyan "AI Shell Profile Integration"
    Write-Host "Would you like to automatically start AI Shell when you open PowerShell in Windows Terminal?"
    Write-Host "This will add code to your PowerShell profile (`$PROFILE) to launch the AI Shell sidecar."
    Write-Host ""
    
    do {
        $response = Read-Host "Add AI Shell to your PowerShell profile? (y/N)"
        $response = $response.Trim().ToLower()
    } while ($response -notin @('', 'y', 'yes', 'n', 'no'))
    
    return ($response -in @('y', 'yes'))
}

function Prompt-DefaultAgent {
    [CmdletBinding()]
    param()
    
    Write-Host ""
    Write-Host -ForegroundColor Cyan "Default Agent Selection"
    Write-Host "Which agent would you like to use by default when AI Shell starts?"
    Write-Host ""
    
    $agents = Get-AvailableAgents
    
    # Display available agents
    for ($i = 0; $i -lt $agents.Count; $i++) {
        Write-Host "$($i + 1). $($agents[$i].Name) - $($agents[$i].Description)"
    }
    Write-Host "$($agents.Count + 1). Let AI Shell choose at startup (recommended)"
    Write-Host ""
    
    do {
        $choice = Read-Host "Select an option (1-$($agents.Count + 1))"
        try {
            $choiceNum = [int]$choice
            if ($choiceNum -ge 1 -and $choiceNum -le ($agents.Count + 1)) {
                break
            }
        }
        catch {
            # Invalid input, continue loop
        }
        Write-Host "Please enter a number between 1 and $($agents.Count + 1)."
    } while ($true)
    
    if ($choiceNum -eq ($agents.Count + 1)) {
        return $null  # Let AI Shell choose at startup
    } else {
        return $agents[$choiceNum - 1].Name
    }
}

<###################################
#
#           Setup/Execute
#
###################################>

Resolve-Environment

if ($Uninstall) {
    Uninstall-AIShellApp
    Uninstall-AIShellModule
    Remove-AIShellFromProfile

    Write-Host -ForegroundColor Green "`nAI Shell App and PowerShell module have been successfully uninstalled."
} else {
    Install-AIShellApp
    Install-AIShellModule

    # Handle profile integration
    $shouldAddToProfile = $false
    $selectedAgent = $null
    
    if ($AddToProfile) {
        # Non-interactive mode: use the provided default agent
        $shouldAddToProfile = $true
        $selectedAgent = $DefaultAgent
    } else {
        # Interactive mode: prompt user for consent and agent selection
        if (Prompt-ProfileIntegration) {
            $shouldAddToProfile = $true
            $selectedAgent = Prompt-DefaultAgent
        }
    }
    
    if ($shouldAddToProfile) {
        Add-AIShellToProfile -DefaultAgent $selectedAgent
    }

    $condition = $IsMacOS ? " if you are in iTerm2" : $null
    Write-Host -ForegroundColor Green -Object @"

Installation succeeded.
To learn more about AI Shell please visit https://aka.ms/AIShell-Docs.
To get started, please run 'Start-AIShell' to use the sidecar experience${condition}, or run 'aish' to use the standalone experience.
"@
    if ($Script:NewPSRLInstalled) {
        Write-Host -ForegroundColor Yellow -Object @"
NOTE: A new version of the PSReadLine module was installed as a dependency.
To ensure the new PSReadLine gets used, please run 'Start-AIShell' from a new session.
"@
    }
    
    if ($shouldAddToProfile) {
        Write-Host -ForegroundColor Yellow -Object @"
NOTE: AI Shell has been added to your PowerShell profile for auto-start in Windows Terminal.
You can remove this by running the install script with -Uninstall.
"@
    }
}
