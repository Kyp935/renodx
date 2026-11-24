# Lowers Detroit: Become Human's built-in Resolution Scaling minimum (0.7 -> 0.1)
# in the Epic build of DetroitBecomeHuman.exe. A backup is kept next to it as
# DetroitBecomeHuman_orig.exe. Safe to run again; it checks the exe first.
param([string]$GameFolder = "")
$ErrorActionPreference = "Stop"

$ExpectedSize = 57782272L
$DisplacementOffset = 0x15C67A      # file offset of the 4-byte displacement
$OpcodeOffset = 0x15C676            # F3 0F 10 05 (movss xmm0,[rip+disp32])
$Original = [byte[]](0xCE,0x0D,0xD4,0x01)   # -> 0.7f
$Lowered  = [byte[]](0x02,0x7C,0x77,0x01)   # -> 0.1f
$Opcode   = [byte[]](0xF3,0x0F,0x10,0x05)

function Test-GameFolder([string]$p) {
    return ($p -ne "" -and (Test-Path -LiteralPath ([System.IO.Path]::Combine($p, "DetroitBecomeHuman.exe"))))
}

if (-not (Test-GameFolder $GameFolder)) {
    $candidates = @(
        "C:\SSD Games\DetroitBecomeHuman",
        "C:\Program Files\Epic Games\DetroitBecomeHuman",
        "C:\Program Files (x86)\Epic Games\DetroitBecomeHuman",
        "D:\Games\DetroitBecomeHuman",
        "E:\Games\DetroitBecomeHuman"
    )
    foreach ($c in $candidates) { if (Test-GameFolder $c) { $GameFolder = $c; break } }
}
if (-not (Test-GameFolder $GameFolder)) {
    $GameFolder = (Read-Host "Paste the Detroit game folder (the one containing DetroitBecomeHuman.exe)").Trim('"')
    if (-not (Test-GameFolder $GameFolder)) { throw "DetroitBecomeHuman.exe was not found in that folder." }
}

$exe = [System.IO.Path]::Combine($GameFolder, "DetroitBecomeHuman.exe")
$backup = [System.IO.Path]::Combine($GameFolder, "DetroitBecomeHuman_orig.exe")
Write-Host "Game folder: $GameFolder"

$info = Get-Item -LiteralPath $exe
if ($info.Length -ne $ExpectedSize) {
    throw "This exe is $($info.Length) bytes; this patch is only for the Epic build ($ExpectedSize bytes). Nothing was changed."
}

$bytes = [System.IO.File]::ReadAllBytes($exe)
for ($i = 0; $i -lt 4; $i++) {
    if ($bytes[$OpcodeOffset + $i] -ne $Opcode[$i]) { throw "The exe does not look like the expected Epic build. Nothing was changed." }
}
$current = $bytes[$DisplacementOffset..($DisplacementOffset + 3)]
if (-not (Compare-Object $current $Lowered)) {
    Write-Host "Already patched. Resolution Scaling can go below 70%." -ForegroundColor Green
    return
}
if (Compare-Object $current $Original) { throw "The exe has unexpected bytes at the patch spot. Nothing was changed." }

if (-not (Test-Path -LiteralPath $backup)) {
    Copy-Item -LiteralPath $exe -Destination $backup
    Write-Host "Backup saved: $backup"
} else {
    Write-Host "Backup already exists, keeping it: $backup"
}
for ($i = 0; $i -lt 4; $i++) { $bytes[$DisplacementOffset + $i] = $Lowered[$i] }
[System.IO.File]::WriteAllBytes($exe, $bytes)
Write-Host "Patched. Resolution Scaling can now go below 70% (set it in the add-on, then restart the game)." -ForegroundColor Green
Write-Host "If Epic verifies or updates the game, run this again."
