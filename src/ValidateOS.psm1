function Test-Utility([string] $path) {
    $target = [System.IO.FileInfo]::new($path)
    $mode = [System.IO.UnixFileMode]@('OtherExecute', 'GroupExecute', 'UserExecute')
    $target.Exists -and $target.UnixFileMode.HasFlag($mode)
}

if (!$IsLinux -or (
    !(Test-Utility "/usr/lib/command-not-found") -and
    !(Test-Utility "/usr/share/command-not-found/command-not-found"))) {
    $exception = [System.PlatformNotSupportedException]::new(
        "This module only works on Linux and depends on the utility 'command-not-found' to be available under the folder '/usr/lib' or '/usr/share/command-not-found'.")
    $err = [System.Management.Automation.ErrorRecord]::new($exception, "PlatformNotSupported", "InvalidOperation", $null)
    throw $err
}
