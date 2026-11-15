# Windows v9 note

The v9 build script fixes a PowerShell 5.1 compatibility bug from v8.
`System.Diagnostics.ProcessStartInfo.ArgumentList` is not available in Windows PowerShell 5.1, so v8 started GCC without any command-line arguments. v9 uses the legacy `Arguments` property and quotes paths correctly.

Recommended command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\native\build-win-mingw.ps1 `
  -NautyDir "C:\Users\Tamás\Downloads\nauty2_9_3" `
  -Gcc "C:\codeshit\ucrt64\bin\gcc.exe"
```
