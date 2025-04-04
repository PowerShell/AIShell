@{
    RootModule = 'AIShell.psm1'
    NestedModules = @("AIShell.Integration.dll")
    ModuleVersion = '1.0.4'
    GUID = 'ECB8BEE0-59B9-4DAE-9D7B-A990B480279A'
    Author = 'Microsoft Corporation'
    CompanyName = 'Microsoft Corporation'
    Copyright = '(c) Microsoft Corporation. All rights reserved.'
    Description = 'Integration with the AIShell to provide intelligent shell experience'
    PowerShellVersion = '7.4.6'
    PowerShellHostName = 'ConsoleHost'
    FunctionsToExport = @()
    CmdletsToExport = @('Start-AIShell','Invoke-AIShell','Resolve-Error')
    VariablesToExport = '*'
    AliasesToExport = @('aish', 'askai', 'fixit')
    HelpInfoURI = 'https://aka.ms/aishell-help'
    PrivateData = @{ PSData = @{ Prerelease = 'preview4'; ProjectUri = 'https://github.com/PowerShell/AIShell' } }
}
