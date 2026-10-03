[CmdletBinding()]
param([Parameter(Mandatory=$true, Position=0)][string]$NautyDir)
$ErrorActionPreference="Stop"
$ScriptDir=Split-Path -Parent $MyInvocation.MyCommand.Path
$Out=Join-Path $ScriptDir "win-x64\orderednauty.dll"
Write-Host "=== OrderedMatching / Windows diagnosis v8 ==="
Write-Host "Nauty: $NautyDir"
$gcc=Get-Command gcc -ErrorAction SilentlyContinue
if($gcc){
  Write-Host "GCC: $($gcc.Source)"
  & $gcc.Source --version | Select-Object -First 1
  Write-Host "Target: $((& $gcc.Source -dumpmachine 2>&1 | Select-Object -First 1).ToString().Trim())"
}else{Write-Host "[WARN] gcc not found on PATH"}
$required=@("nauty.c","nautil.c","naugraph.c","nausparse.c","schreier.c","naurng.c")
foreach($f in $required){if(Test-Path (Join-Path $NautyDir $f)){Write-Host "[OK] $f"}else{Write-Host "[FAIL] $f" -ForegroundColor Red}}
if(Test-Path (Join-Path $NautyDir "nauty.h")){Write-Host "[OK] nauty.h"}else{Write-Host "[FAIL] nauty.h" -ForegroundColor Red}
if(Test-Path $Out){Write-Host "[OK] DLL: $Out ($(Get-Item $Out).Length bytes)"}else{Write-Host "[INFO] DLL not built: $Out"}
