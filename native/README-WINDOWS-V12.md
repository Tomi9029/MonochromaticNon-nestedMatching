# Windows build: nauty 2.9.3 + MSYS2 UCRT64 (v13)

This version builds the native DLL from the **MSYS2 UCRT64 terminal**.
It deliberately passes **relative paths** to GCC. This avoids assembler failures caused by
non-ASCII Windows usernames such as `Tamás` in absolute `-o` paths.

## 1. UCRT64 compiler

```bash
pacman -S --needed mingw-w64-ucrt-x86_64-gcc
```

Check:

```bash
gcc -dumpmachine
```

Expected:

```text
x86_64-w64-mingw32
```

## 2. Build the DLL

From the project directory in the UCRT64 terminal:

```bash
bash native/build-win-ucrt64.sh /c/Users/Tamas/Downloads/nauty2_9_3
```

Use the actual path to your nauty directory. The project and nauty directory should be on the same drive.

The DLL is created at:

```text
native/win-x64/orderednauty.dll
```

Per-step logs are created under `native/win-x64/`.

## 3. Run C# tests

PowerShell is fine for .NET:

```powershell
dotnet clean
dotnet run -- --self-test --require-nauty
```

The project targets x64 and copies the native DLL into the .NET output folder after a successful build.
