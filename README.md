A program a következő problémát vizsgálja:

- `m = 3n - 1` rendezett csúcs: `1 < 2 < ... < m`;
- minden él piros vagy kék;
- keresünk `n` élből álló egyszínű párosítást;
- két él ölelő/nested, ha `a < b < c < d` mellett `(a,d)` és `(b,c)`;
- az ölelő párok tiltottak.


### B modell: független matching-generátor + közvetlen állapottér-kereső

A `IndependentMatchingModel` nem a `CnfInstance.Build()` logikáját használja. Először kiválasztja a `2n`
csúcsból álló részhalmazokat, majd azokon külön generátorral előállítja az összes tökéletes párosítást,
és csak utána szűri ki az ölelő párokat.

Az `IndependentSearchSolver` nem CNF-et, watchereket vagy clause-arrayeket használ. Közvetlenül minden
matchingnél a piros/kék/még ismeretlen élek számát vezeti, és szükség esetén unit-szerű kényszert propagál.

Ez nem formális matematikai bizonyítás, de erős programhibakereső kontroll.

## Futtatás

Normál keresés:

```powershell
dotnet run -- -n 3
dotnet run -- -n 4
dotnet run -- -n 5
```

```powershell
dotnet run -- -n 6
dotnet run -- --verify -n 6
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


## Módszertani korlát

A `UNSAT` eredmény azt jelenti, hogy a program matematikai modellje szerint nincs ellenpélda-színezés.
Ez az edition két külön implementációval ellenőrzi ezt a modellt kis/n=5-ig terjedő esetekben,
de nem állít elő DRAT/LRAT formális SAT-bizonyítványt.


Az n=6 esethez a program külön tömör modellt használ, mert a 17 csúcsú gráfban 816816 nem-nested M6 található. A matchingek lapos tömbben vannak, az A solver implicit klauzulákat és watcholt DPLL-t használ, a B solver pedig CSR incidence-listás közvetlen hipergráf-keresést. A két generátor exact packed 64-bit key halmazát is összehasonlítjuk.

A célkimenet két solvernél azonos `UNSAT`, majd `VERIFICATION: PASS`. Ez számítási ellenőrzés, nem formális SAT-proof certificate.


A régi `CnfInstance` objektum-alapú modellje 1 633 632 klauzulát és több millió watcher-referenciát tartana együtt. Ez az edition ezért a matchingeket egyetlen lapos `int[]` tömbben tárolja, az A solver pedig a klauzulákat implicit módon kezeli. A B solver CSR incidence-listát használ.

A két generátor packed, rendezett él-ID-kből képzett 64 bites kulcsok rendezett tömbjét hasonlítja össze, így n=6-nál sem kell több százezer stringet létrehozni.
