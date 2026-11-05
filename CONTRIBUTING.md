# Contributing

## Reporting a problem

Open an issue with:

- `tws-login-win --version`, your TWS version (Help → About) and Windows version;
- the tool's full output, which never holds credentials (check it anyway before posting);
- for a screen it did not recognise: the output of `tws-login-win watch` while you go through that screen by hand.
  It shows controls with typed text by length only and digits masked.

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
