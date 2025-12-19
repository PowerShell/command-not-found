#
# Module manifest for module 'command-not-found'
#

@{
    ModuleVersion = '0.2.0'
    GUID = '47013747-CB9D-4EBC-9F02-F32B8AB19D48'
    Author = 'PowerShell'
    CompanyName = "Microsoft Corporation"
    Copyright = "Copyright (c) Microsoft Corporation."
    Description = "Provide feedback on the 'CommandNotFound' error stemmed from running an executable on Linux platform."
    PowerShellVersion = '7.4'

    NestedModules = @('ValidateOS.psm1', 'PowerShell.CommandNotFound.Feedback.dll')
    FunctionsToExport = @()
    CmdletsToExport = @()
    VariablesToExport = '*'
    AliasesToExport = @()

    PrivateData = @{
        PSData = @{
            Tags = @('Linux')
            ProjectUri = 'https://github.com/PowerShell/command-not-found'
        }
    }
}
