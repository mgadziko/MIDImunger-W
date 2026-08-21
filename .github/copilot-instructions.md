# Build output

For every MIDImunger-W build intended for the user to run, publish the Windows application to `dist\` from the repository root, overwriting the prior published version:

```powershell
dotnet publish src\MIDImunger.W -c Release -o dist
```
