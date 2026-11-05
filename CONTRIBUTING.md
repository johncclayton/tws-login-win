# Contributing

## Reporting a problem

Open an issue with:

- `tws-login-win --version`, your TWS version (Help → About) and Windows version;
- the tool's full output, which never holds credentials (check it anyway before posting);
- for a screen it did not recognise: the output of `tws-login-win watch` while you go through that screen by hand.
  It shows controls with typed text by length only and digits masked; still, read it before posting.

Never post credentials, one-time-password secrets, account numbers or screenshots of TWS. Security problems go to
[SECURITY.md](SECURITY.md), not an issue.

## Changing the code

```powershell
dotnet test            # no TWS needed: tests run against a fake TWS (tests/tws-login-win.Tests/FakeTws.cs)
.\build\publish.ps1    # the release exe and its SHA256SUMS in artifacts\
```

- Every step that sends something to TWS first confirms the screen it expects, and aborts
  (`LoginAbortedException`) when anything is off. Keep it that way: an abort is always safer than a guess.
- Never log a field's text, a credential or a code; log lengths. New log lines with TWS text go through
  `AccountNumbers.Redact` or `ControlTree.MaskDigits`.
- A new TWS screen or dialog: record what `watch` shows in [docs/tws-notes.md](docs/tws-notes.md), add it to
  `FakeTws.cs`/`TwsScreens`, and test it.
- Use the terms in [GLOSSARY.md](GLOSSARY.md). A decision that is hard to reverse gets an ADR in
  [docs/adr](docs/adr).

## Releasing

Releases are cut by pushing a tag; [`.github/workflows/release.yml`](.github/workflows/release.yml) builds both exes,
attests them and creates a **draft** release. Nothing is public until the draft is published by hand.

1. Set `<Version>` in `src/tws-login-win/tws-login-win.csproj` (e.g. `0.2.0`) and give its entry in
   [CHANGELOG.md](CHANGELOG.md) that version and the date; the changelog is the release notes.
2. Commit, push to `main`, and wait for CI to pass.
3. Tag and push the tag; it must be `v` plus `<Version>` exactly, or the release workflow stops:

   ```powershell
   git tag -a v0.2.0 -m "tws-login-win 0.2.0"
   git push origin v0.2.0
   ```

4. When the workflow has finished, check the draft in an empty folder:

   ```powershell
   gh release download v0.2.0 --repo johncclayton/tws-login-win -D $env:TEMP\tws-login-win-v0.2.0
   cd $env:TEMP\tws-login-win-v0.2.0
   sha256sum -c SHA256SUMS            # or compare (Get-FileHash <file>).Hash with each line
   gh attestation verify tws-login-win.exe --repo johncclayton/tws-login-win
   gh attestation verify tws-login-win-net10.exe --repo johncclayton/tws-login-win
   .\tws-login-win.exe --version; .\tws-login-win-net10.exe --version   # both print the new version
   ```

5. Publish it: `gh release edit v0.2.0 --repo johncclayton/tws-login-win --draft=false --latest`.

Anyone who pins a release by its hash, as the README suggests for downloads, has to move that pin to the new
version's `SHA256SUMS` line after each release; a pin left on the old hash keeps installing the old version.
