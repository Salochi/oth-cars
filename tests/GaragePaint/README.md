Run the pure palette, item identity, bit-capacity and colour checks without loading the game:

```powershell
dotnet run --project tests/GaragePaint/GaragePaint.csproj
```

In-game verification still required (the IL2CPP menu and mount calls cannot execute in this harness):

1. In each car's garage Liveries tab, check that every named paint has a colour swatch, including cars without bundled image liveries.
2. Hover several paints with fixed trim, custom trim and Body colour trim. The dummy should preview them; F7's saved paint and the driven car should change only on confirmation. Leave the tab without confirming and check the saved look returns.
3. Confirm a paint while a skin is equipped. The skin should come off and F7 should show the selected paint. Confirm Custom paint and check its F7 RGB value is used.
4. Change paint through F7 while the garage is open and while driving. Reopen the livery tab and check the mounted tick follows the setting. Edit Custom RGB and check the custom swatch updates.
5. Mount a transparent image livery, change its underlying paint through F7, and check the livery remains mounted over that paint.

Save migration, network layout/version updates, and multiplayer compatibility testing are deferred. Build with `-p:NoDeploy=true` until ready to install for local in-game testing.
