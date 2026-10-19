# Windows build notes

This project is intended to work with nauty 2.9.x and MinGW GCC.

PowerShell 5.1 can misread UTF-8 scripts without a BOM. The PowerShell scripts in this version are ASCII-only so they can be run without changing the system execution policy.

Recommended build command:

    powershell -ExecutionPolicy Bypass -File .\native\build-win-mingw.ps1 "C:\Users\Tamás\Downloads\nauty2_9_3"

Diagnosis command:

    powershell -ExecutionPolicy Bypass -File .\native\diagnose-win.ps1 "C:\Users\Tamás\Downloads\nauty2_9_3"

The build should create:

    native\win-x64\orderednauty.dll

A successful build with MinGW GCC 6.3.0 is acceptable. The DLL is loaded by the C# code through P/Invoke.
