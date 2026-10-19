## Futtatás

Normál keresés:

```powershell
dotnet run -- -n 3
dotnet run -- -n 4
dotnet run -- -n 5
```

Független keresztellenőrzés:

```powershell
dotnet run -- --verify -n 3
dotnet run -- --verify -n 4
dotnet run -- --verify -n 5
```

Az összes jelenlegi kulcseset:

```powershell
dotnet run -- --verify-all
```

Ez `n=2..5` eseteket futtatja a két solverrel, és összehasonlítja az eredményeket.

nauty-val együtt egy SAT esetben:

```powershell
dotnet run -- --verify -n 2 --nauty
```

## Self-test

```powershell
dotnet run -- --self-test --require-nauty
```

A self-test tartalmazza:

- nested predicate teszteket;
- az eredeti és a független matching-generátor darabszámának kontrollját `n=1..5` esetén;
- ismert `3n-2` ellenpélda-színezéseket;
- az eredeti DPLL regressziókat;
- az independent solver regresszióit;
- a nauty kanonikusítási teszteket.

Az ismert kis darabszámok:

```text
n=1: 1
n=2: 10
n=3: 140
n=4: 2310
n=5: 42042
```

## Módszertani korlát

A `UNSAT` eredmény azt jelenti, hogy a program matematikai modellje szerint nincs ellenpélda-színezés.
A Verification Edition két külön implementációval ellenőrzi ezt a modellt kis/n=5-ig terjedő esetekben,
de nem állít elő DRAT/LRAT formális SAT-bizonyítványt. Ezért a projekt számítási ellenőrzés, nem formális
proof certificate.


Futtatás:

```powershell
dotnet run -- -n 6
dotnet run -- --verify -n 6
```


- `-m M` / `--m M` allows the vertex count to be set independently of `n`; without it, `m=3n-1` remains the default.
- `--case K` (1-based) prints the K-th generated matching, its vertices and edges, and the matching-edge color patterns.
- `--max-colorings K` limits how many of the matching's `2^n` color patterns are printed (default 64).
- `--show-coloring` prints a complete `K_m` edge coloring when SAT finds one; `-o FILE` saves it.



```powershell
dotnet run -- -n 6 -m 16
dotnet run -- -n 6 -m 17
dotnet run -- --verify -n 5 -m 14
```

Egy konkrét, 1-től indexelt matching-eset megtekintése:

```powershell
dotnet run -- -n 6 -m 17 --case 123
dotnet run -- -n 6 -m 17 --case 816816
dotnet run -- -n 6 -m 17 --case 123 --max-colorings 12
```


Ha a solver SAT eredményt talál, a teljes `K_m` élszínezése is kiíratható/menthető:

```powershell
dotnet run -- -n 3 -m 7 --show-coloring
dotnet run -- -n 3 -m 7 -o counterexample-coloring.txt
```

A `--max-colorings` a `--case` esetén kiírt matching-szintű színminták számát korlátozza; ha csak egy részük jelenik meg, a kimenet kiírja az összes színminta kiírásához szükséges értéket.
