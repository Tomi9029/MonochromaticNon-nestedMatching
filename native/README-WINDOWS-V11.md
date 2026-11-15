# Windows build: nauty 2.9.3 + MSYS2 UCRT64

The Windows native build is intentionally done from the **MSYS2 UCRT64 terminal**, not from PowerShell. This avoids Windows PowerShell 5.1 argument quoting and encoding issues.

## 1. Install GCC in MSYS2 UCRT64

```bash
pacman -S --needed mingw-w64-ucrt-x86_64-gcc
```

Check:

```bash
gcc -dumpmachine
```

It must start with `x86_64-w64-mingw32`.

## 2. Build the nauty DLL

From the project directory in the UCRT64 terminal:

```bash
bash native/build-win-ucrt64.sh /c/Users/Tamas/Downloads/nauty2_9_3
```

For a path containing spaces, quote it.

The script logs each compilation separately under `native/win-x64/`.

## 3. Build/run the C# project

PowerShell is fine for this part:

```powershell
dotnet clean
dotnet run -- --self-test --require-nauty
```

The `.csproj` copies `orderednauty.dll` to the .NET output directory after a successful build.
