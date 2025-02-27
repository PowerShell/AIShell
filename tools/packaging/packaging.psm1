## Copyright (c) Microsoft Corporation.
## Licensed under the MIT License.

$repoRoot = (Resolve-Path -Path "$PSScriptRoot/../..").Path
$packagingStrings = Import-PowerShellDataFile "$PSScriptRoot\packaging.strings.psd1"
$dotnetSDKVersion = $(Get-Content $repoRoot/global.json | ConvertFrom-Json).Sdk.Version

function New-NugetPackage
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $PackageSourcePath,

        [Parameter(Mandatory)]
        [string] $Version,

        [Parameter(Mandatory)]
        [string] $PackageDestinationPath
    )

    $nuget = Get-Command -Type Application nuget -ErrorAction SilentlyContinue

    if ($null -eq $nuget) {
        throw 'nuget application is not available in PATH'
    }

    $NuSpecPath = Join-Path ([System.IO.Path]::GetTempPath()) nuget-spec
    if (Test-Path $NuSpecPath) {
        Write-Verbose -Verbose "Remove existing nuget-spec folder"
        Remove-Item $NuSpecPath -Recurse -Force
    }

    $dotnetRuntime = "net$([version]::new($dotnetSDKVersion).Major).0"
    $libPath = Join-Path $NuSpecPath "lib" $dotnetRuntime
    $null = New-Item -ItemType Directory $libPath

    Write-Verbose -Verbose "nuget-spec path: $NuSpecPath"
    Write-Verbose -Verbose "lib path: $libPath"

    Copy-Item "$PackageSourcePath/AIShell.Abstraction.dll" $libPath
    New-NuSpec -FilePath "$NuSpecPath/AIShell.Abstraction.nuspec" -PackageVersion $Version -DotNetRuntime $dotnetRuntime

    Write-Verbose -Verbose "List all files:"
    Get-ChildItem $NuSpecPath | Out-String -Width 500 -Stream | Write-Verbose -Verbose

    Push-Location $NuSpecPath
    $null = nuget pack .

    if (-not (Test-Path $PackageDestinationPath)) {
        New-Item $PackageDestinationPath -ItemType Directory -Force > $null
    }

    Copy-Item *.nupkg $PackageDestinationPath -Force -Verbose
    Pop-Location
}

function New-NuSpec
{
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(Mandatory)]
        [string] $PackageVersion,

        [Parameter(Mandatory)]
        [string] $DotNetRuntime
    )

    $nuspec = $packagingStrings.NuspecTemplate -f $PackageVersion
    $nuspecObj = [xml] $nuspec

    $nuspecObj.package.metadata.dependencies.group.targetFramework = $DotNetRuntime
    $dependencies = Get-ProjectPackageInformation -ProjectFile "$repoRoot/shell/AIShell.Abstraction/AIShell.Abstraction.csproj"
    foreach ($entry in $dependencies) {
        $dep = $nuspecObj.package.metadata.dependencies.group.AppendChild($nuspecObj.CreateElement("dependency"))
        $dep.SetAttribute('id', $entry.Name)
        $dep.SetAttribute('version', $entry.Version)
    }

    foreach ($item in $nuspecObj.package.metadata.dependencies.group.dependency) {
        $item.RemoveAttribute('xmlns')
    }

    $nuspecObj.Save($FilePath)
}

function Get-ProjectPackageInformation
{
    param(
        [Parameter(Mandatory)]
        [string] $ProjectFile
    )

    [xml] $csprojXml = (Get-Content -Raw -Path $ProjectFile)

    # get the package references
    $packages=$csprojXml.Project.ItemGroup.PackageReference

    # check to see if there is a newer package for each refernce
    foreach($package in $packages) {
        if ($package.Version -notmatch '\*' -and $package.Include) {
            # Get the name of the package
            [PSCustomObject] @{
                Name = $package.Include
                Version = $package.Version
            }
        }
    }
}

function New-TarballPackage
{
    [CmdletBinding(SupportsShouldProcess=$true)]
    param (
        [Parameter(Mandatory)]
        [string] $PackageSourcePath,

        [Parameter(Mandatory)]
        [string] $Version,

        [Parameter(Mandatory)]
        [string] $Architecture,

        [string] $CurrentLocation = (Get-Location),
        [switch] $Force
    )

    $packageName = "AIShell-$Version-{0}-$Architecture.tar.gz"

    if ($IsWindows) {
        throw "Must be on Linux or macOS to build 'tar.gz' packages!"
    } elseif ($IsLinux) {
        $packageName = $packageName -f "linux"
    } elseif ($IsMacOS) {
        $packageName = $packageName -f "osx"
    }

    $packagePath = Join-Path -Path $CurrentLocation -ChildPath $packageName
    Write-Verbose "Create package $packageName" -Verbose
    Write-Verbose "Package destination path: $packagePath" -Verbose

    if (Test-Path -Path $packagePath) {
        if ($Force -or $PSCmdlet.ShouldProcess("Overwrite existing package file")) {
            Write-Verbose "Overwrite existing package file at $packagePath" -Verbose
            Remove-Item -Path $packagePath -Force -ErrorAction Stop -Confirm:$false
        }
    }

    if (Get-Command -Name chmod -CommandType Application -ErrorAction Ignore) {
        Write-Verbose "Add the execution permission to 'aish'" -Verbose
        $executable = Join-Path $PackageSourcePath 'aish'
        chmod +x $executable

        $permission = Get-ChildItem $executable | ForEach-Object UnixFileMode
        Write-Verbose "File permission: $permission" -Verbose
    } else {
        throw "Failed to create the package because the application 'chmod' cannot be found"
    }

    if (Get-Command -Name tar -CommandType Application -ErrorAction Ignore) {
        Write-Verbose "Create tarball package" -Verbose
        $options = "-czf"
        if ($PSBoundParameters.ContainsKey('Verbose') -and $PSBoundParameters['Verbose'].IsPresent) {
            # Use the verbose mode '-v' if '-Verbose' is specified
            $options = "-czvf"
        }

        try {
            Push-Location -Path $PackageSourcePath
            tar $options $packagePath *
        } finally {
            Pop-Location
        }

        if (Test-Path -Path $packagePath) {
            Write-Host "You can find the tarball package at '$packagePath'" -ForegroundColor Green
        } else {
            throw "Failed to create $packageName"
        }
    } else {
        throw "Failed to create the package because the application 'tar' cannot be found"
    }
}

function New-ZipPackage
{
    [CmdletBinding(SupportsShouldProcess=$true)]
    param (
        [Parameter(Mandatory)]
        [string] $PackageSourcePath,

        [Parameter(Mandatory)]
        [string] $Version,        

        [Parameter(Mandatory)]
        [string] $Architecture,

        [string] $CurrentLocation = (Get-Location),
        [switch] $Force
    )

    $packageName = "AIShell-$Version-win-$Architecture.zip"
    $packagePath = Join-Path -Path $CurrentLocation -ChildPath $packageName

    Write-Verbose "Create package $packageName" -Verbose
    Write-Verbose "Package destination path: $packagePath" -Verbose

    if (Test-Path -Path $packagePath) {
        if ($Force -or $PSCmdlet.ShouldProcess("Overwrite existing package file")) {
            Write-Verbose "Overwrite existing package file at $packagePath" -Verbose
            Remove-Item -Path $packagePath -Force -ErrorAction Stop -Confirm:$false
        }
    }

    if (Get-Command Compress-Archive -ErrorAction Ignore) {
        Compress-Archive -Path $PackageSourcePath\* -DestinationPath $packagePath
        if (Test-Path $packagePath) {
            Write-Host "You can find the ZIP package at '$packagePath'" -ForegroundColor Green
        } else {
            throw "Failed to create $packageName"
        }
    } else {
        Write-Error -Message "Compress-Archive cmdlet is missing in this PowerShell version"
    }
}

function New-MSIXPackage
{
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact='Low')]
    param (
        [Parameter(Mandatory)]
        [string] $ProductSourcePath,

        [Parameter(Mandatory)]
        [string] $Version,

        [Parameter(Mandatory)]
        [string] $Architecture,

        [string] $CurrentLocation = (Get-Location),
        # Produce private package for testing in Store
        [Switch] $Private,
        [Switch] $Force
    )

    $makeappx = Get-Command makeappx -CommandType Application -ErrorAction Ignore
    if ($null -eq $makeappx) {
        # This is location in our dockerfile
        $dockerPath = Join-Path $env:SystemDrive "makeappx"
        if (Test-Path $dockerPath) {
            $makeappx = Get-ChildItem $dockerPath -Include makeappx.exe -Recurse | Select-Object -First 1
        }

        if ($null -eq $makeappx) {
            # Try to find in well known location
            $makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64" -Include makeappx.exe -Recurse | Select-Object -First 1
            if ($null -eq $makeappx) {
                throw "Could not locate makeappx.exe, make sure Windows 10 SDK is installed"
            }
        }
    }

    $makepri = Get-Item (Join-Path $makeappx.Directory "makepri.exe") -ErrorAction Stop

    $ProductNameSuffix = if ($Private) { 'Private' } else { "win-$Architecture" }
    $packageName = "AIShell-$Version-$ProductNameSuffix"

    $productName = $displayName = "AIShell"

    if ($Private) {
        $productName += '-Private'
        $displayName += '-Private'
    } elseif ($Version.Contains('-')) {
        $productName += 'Preview'
        $displayName += ' Preview'
    }

    Write-Verbose -Verbose "ProductName: $productName"
    Write-Verbose -Verbose "DisplayName: $displayName"

    $isPreview = $Version.Contains('-')
    $ProductVersion = Get-WindowsVersion -PackageName $packageName -ProductVersion $Version

    if ($isPreview) {
        Write-Verbose "Using Preview assets" -Verbose
    }

    # Appx manifest needs to be in root of source path, but the embedded version needs to be updated
    # cp-459155 is 'CN=Microsoft Windows Store Publisher (Store EKU), O=Microsoft Corporation, L=Redmond, S=Washington, C=US'
    # authenticodeFormer is 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US'
    $releasePublisher = 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US'

    $appxManifest = Get-Content "$repoRoot\tools\assets\AppxManifest.xml" -Raw
    $appxManifest = $appxManifest.Replace('$VERSION$', $ProductVersion).Replace('$ARCH$', $Architecture).Replace('$PRODUCTNAME$', $productName).Replace('$DISPLAYNAME$', $displayName).Replace('$PUBLISHER$', $releasePublisher)
    $xml = [xml]$appxManifest
    if ($isPreview) {
        Write-Verbose -Verbose "Adding aish-preview.exe alias"
        $aliasNode = $xml.Package.Applications.Application.Extensions.Extension.AppExecutionAlias.ExecutionAlias.Clone()
        $aliasNode.alias = "aish-preview.exe"
        $xml.Package.Applications.Application.Extensions.Extension.AppExecutionAlias.AppendChild($aliasNode) | Out-Null
    }
    $xml.Save("$ProductSourcePath\AppxManifest.xml")

    # Necessary image assets need to be in source assets folder
    $assets = @(
        'Square150x150Logo'
        'Square44x44Logo'
        'Square44x44Logo.targetsize-48'
        'Square44x44Logo.targetsize-48_altform-unplated'
        'StoreLogo'
    )

    if (!(Test-Path "$ProductSourcePath\assets")) {
        $null = New-Item -ItemType Directory -Path "$ProductSourcePath\assets"
    }

    $assets | ForEach-Object {
        if ($isPreview) {
            Copy-Item -Path "$repoRoot\tools\assets\$_-Preview.png" -Destination "$ProductSourcePath\assets\$_.png"
        }
        else {
            Copy-Item -Path "$repoRoot\tools\assets\$_.png" -Destination "$ProductSourcePath\assets\"
        }
    }

    if ($PSCmdlet.ShouldProcess("Create .msix package?")) {
        Write-Verbose "Creating priconfig.xml" -Verbose
        & $makepri createconfig /o /cf (Join-Path $ProductSourcePath "priconfig.xml") /dq en-US

        Write-Verbose "Creating resources.pri" -Verbose
        Push-Location $ProductSourcePath
        & $makepri new /v /o /pr $ProductSourcePath /cf (Join-Path $ProductSourcePath "priconfig.xml")
        Pop-Location

        Write-Verbose "Creating msix package" -Verbose
        & $makeappx pack /o /v /h SHA256 /d $ProductSourcePath /p (Join-Path -Path $CurrentLocation -ChildPath "$packageName.msix")
        Write-Verbose "Created $packageName.msix" -Verbose
    }
}

function Get-WindowsVersion {
    param (
        [parameter(Mandatory)]
        [string]$PackageName,

        [parameter(Mandatory)]
        [string]$ProductVersion
    )

    $ProductVersion = Get-PackageVersionAsMajorMinorBuildRevision -Version $ProductVersion
    if (([Version]$ProductVersion).Revision -eq -1) {
        $ProductVersion += ".0"
    }

    # The Store requires the last digit of the version to be 0 so we swap the build and revision
    # This only affects Preview versions where the last digit is the preview number
    # For stable versions, the last digit is already zero so no changes
    $pversion = [version]$ProductVersion
    if ($pversion.Revision -ne 0) {
        $revision = $pversion.Revision
        if ($packageName.Contains('-rc')) {
            # For Release Candidates, we use numbers in the 100 range
            $revision += 100
        }

        $pversion = [version]::new($pversion.Major, $pversion.Minor, $revision, 0)
        $ProductVersion = $pversion.ToString()
    }

    Write-Verbose "Version: $productversion" -Verbose
    return $productversion
}

# Builds coming out of this project can have version number as 'a.b.c' OR 'a.b.c-d-f'
# This function converts the above version into major.minor[.build[.revision]] format
function Get-PackageVersionAsMajorMinorBuildRevision
{
    [CmdletBinding()]
    param (
        # Version of the Package
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string] $Version,
        [switch] $IncrementBuildNumber
    )

    Write-Verbose "Extract the version in the form of major.minor[.build[.revision]] for $Version"
    $packageVersionTokens = $Version.Split('-')
    $packageVersion = ([regex]::matches($Version, "\d+(\.\d+)+"))[0].value

    if (1 -eq $packageVersionTokens.Count -and ([Version]$packageVersion).Revision -eq -1) {
        # In case the input is of the form a.b.c, add a '0' at the end for revision field
        $packageVersion = $packageVersion + '.0'
    } elseif (1 -lt $packageVersionTokens.Count) {
        # We have all the four fields
        $packageBuildTokens = ([regex]::Matches($packageVersionTokens[1], "\d+"))[0].value

        if ($packageBuildTokens)
        {
            if($packageBuildTokens.length -gt 4)
            {
                # MSIX will fail if it is more characters
                $packageBuildTokens = $packageBuildTokens.Substring(0,4)
            }

            if ($packageVersionTokens[1] -match 'rc' -and $IncrementBuildNumber) {
                $packageBuildTokens = [int]$packageBuildTokens + 100
            }

            $packageVersion = $packageVersion + '.' + $packageBuildTokens
        }
        else
        {
            $packageVersion = $packageVersion
        }
    }

    $packageVersion
}
