if ($IsMacOS -and $env:TERM_PROGRAM -ne "iTerm.app") {
    throw "The AIShell module requires iTerm2 to work properly. Please install and run from the iTerm2 terminal."
}

$module = Get-Module -Name PSReadLine
if ($null -eq $module -or $module.Version -lt [version]"2.4.2") {
    throw "The PSReadLine v2.4.2-beta2 or higher is required for the AIShell module to work properly."
}

## Create the channel singleton when loading the module.
$null = [AIShell.Integration.Channel]::CreateSingleton($host.Runspace, [Microsoft.PowerShell.PSConsoleReadLine])
