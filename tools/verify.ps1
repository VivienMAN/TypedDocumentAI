$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    python tools/verify.py @args
    if ($LASTEXITCODE -ne 0) { throw "Verification failed with exit code $LASTEXITCODE." }
}
finally { Pop-Location }
