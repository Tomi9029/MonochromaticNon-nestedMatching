# n=6 validation notes

For m=17 and k=6, the compact primary generator produces exactly 816816 non-nested matchings.
The independent vertex-subset/perfect-matching generator is required to produce the same number,
and ResearchEdition compares the exact set after canonical sorting of each matching's edge IDs.

The resulting SAT problem has 816816 forbidden all-blue clauses and 816816 forbidden all-red clauses,
for a total of 1633632 clauses. There are 17 choose 2 = 136 edge-color variables.

A successful `dotnet run -- --verify -n 6` reports:
- both matching counts equal to 816816;
- exact packed matching set equal;
- A solver result and B solver result equal;
- `VERIFICATION: PASS`.