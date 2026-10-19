# nauty natív bridge (Windows / Linux)

A C# program egy kicsi C bridge-en keresztül hívja a nauty `densenauty()` rutinját.

## Windows + MinGW/UCRT64

A build script a felhasználó által megadott nauty forrásfájljait közvetlenül a DLL-be fordítja.
A `densenauty()` használatához a nauty 2.9.3 dokumentációja szerint ezek a core fájlok szükségesek:

- `nauty.c`
- `nautil.c`
- `naugraph.c`
- `schreier.c`
- `naurng.c`

A projekt ezt az öt fájlt használja, nem a teljes nauty csomagot.

### Diagnosztika

```powershell
powershell -ExecutionPolicy Bypass -File .\native\diagnose-win.ps1 "C:\Users\Tamás\Downloads\nauty2_9_3"
```

### Build

```powershell
powershell -ExecutionPolicy Bypass -File .\native\build-win-mingw.ps1 "C:\Users\Tamás\Downloads\nauty2_9_3"
```

A DLL helye:

```text
native\win-x64\orderednauty.dll
```

A teljes fordítási napló:

```text
native\win-x64\build.log
```

Ha a `gcc` nincs PATH-ban, add át a teljes elérési utat:

```powershell
powershell -ExecutionPolicy Bypass -File .\native\build-win-mingw.ps1 `
  "C:\Users\Tamás\Downloads\nauty2_9_3" `
  -Gcc "C:\msys64\ucrt64\bin\gcc.exe"
```

## Miért auxiliary graph?

A rendezett pozíciók `0..m-1` egy utat alkotnak. Minden eredeti élhez készül egy új csúcs,
amely a két végpontjához kapcsolódik; az új csúcs piros vagy kék színosztályba kerül.

Az így létrehozott színezett segédgráf automorfizmusa megőrzi a pozíciók útját, ezért az eredeti
sorrend szempontjából csak az identitás és a teljes megfordítás marad.
