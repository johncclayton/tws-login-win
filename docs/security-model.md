# Security model

tws-login-win handles three secrets: a TWS **user name**, its **password**, and, for live accounts, the
**one-time code** of the second factor (or rather the secret that generates it, which 1Password holds). This page
says what the tool protects, what it cannot, and how to set it up so a mistake costs as little as possible.

To report a vulnerability, see [SECURITY.md](../SECURITY.md).

## What the tool guarantees

- **Secrets never appear in output.** Credentials print as lengths only (`user 9 chars, password 20 chars`); the
  records holding them override `ToString` so they cannot slip into a log by accident. Error messages name *where*
  a value came from, never the value: a bad `trading mode` says `(value not shown)`; output from `op` that fails to
  parse is not echoed, as it holds the item.
- **Secrets never go on a command line.** The command line names an environment variable, an `op://` reference or
  a file path. `op` is started with the vault and item names only and returns the item on a pipe.
- **The password is never read back from TWS**: the Java Access Bridge reports a password field's length, and that
  is all the tool asks for. The user name and the code *are* read back, to confirm TWS holds exactly what was set,
  and compared in memory only.
- **The code is fetched at the moment it is needed**, on the code screen, not when the login starts, and only its
  length is shown (`check-creds` fetches one to prove the setup works).
- **Nothing is sent to an unexpected screen.** Every press and every field set is preceded by checks that TWS shows
  the expected screen; anything unknown aborts with nothing more sent. `--mode` aborts before anything is sent
  when the credentials are for the other trading mode, so paper credentials never reach a live TWS or the reverse.
- **TWS's own text is masked** in logs: account numbers in window titles become `[account]`, digits in dialogs
  become `#`. `watch` and `inspect`, made for sharing in bug reports, show any field that may hold typed text by
  length only. The main TWS window, which shows account data, is never read.

## Which TWS gets the credentials

Any program can name itself `tws.exe` and open a window titled "Login" that looks like TWS's. Without a check, the
tool would type the user name, password and a valid one-time code into it, which is worse than the program reading
the 1Password item itself when 1Password would ask you to approve that.

So **the tool only sees a trusted TWS**: a process whose executable carries a valid Authenticode signature from
**`Interactive Brokers Group, Inc.`** (checked by Windows' `WinVerifyTrust`, certificate chained to a trusted root,
the organisation in the signer certificate compared exactly). Windows of any other process are left out before
anything reads them, and logged once:

```
ignoring pid 4242 (C:\Users\me\Downloads\tws.exe): not validly signed: no signature, not by 'Interactive Brokers Group, Inc.'. ...
```

How the executable is found matters as much as the signature:

- Its path comes from Windows (`QueryFullProcessImageName`), not from the process's own memory, which the process
  could rewrite. Windows follows renames: a program that renames its running executable and puts a copy of the
  signed TWS at the old path is judged by its own, renamed, unsigned file.
- The file mapped at the process's image base must be that same file (same volume and file id).
- The file is held open while it is judged, so it cannot be changed, renamed or replaced meanwhile, and the signer
  is read from the same verification rather than from the file a second time.
- A process is judged once per process start; its executable cannot change while it runs.

The Java Access Bridge DLL is loaded only from that trusted TWS's own `jre\bin`, only if it too is validly signed
(TWS's Java runtime is signed by its vendor, currently Azul Systems), held open from the check until it is loaded,
and with its own dependencies taken from its folder or System32 only.

**`op`**, the 1Password CLI, is looked for only in the folders on `PATH`, never in the current folder or the
tool's own (where a download could plant an `op.exe` that would receive `OP_SERVICE_ACCOUNT_TOKEN`), and must be
validly signed by AgileBits.

Revocation is checked from Windows' local cache only, never fetched from the network: a slow or absent network
never blocks a login. A revocation status not in the cache is accepted.

`--trust-unsigned` turns all of these signature checks off and says so in the log. Use it only to try a TWS build
you obtained from IBKR that is somehow not signed, never routinely.

### What the check does not stop

A program already running as your Windows user can do almost anything you can. The signature check makes the
cheap attack (a look-alike window named `tws.exe`) fail; it is not a sandbox, and these remain open to such a
program:

- reading `OP_SERVICE_ACCOUNT_TOKEN` from your environment, or an ini file;
- **changing the real TWS**: `C:\Jts` is normally writable by your user, so a program can edit TWS's
  `.vmoptions` file or its jars (e.g. add a Java agent), and the signed `tws1.exe` then runs its code;
- **answering for TWS through the bridge**: the Java Access Bridge finds Java programs through window messages, and
  a program that speaks its protocol could, in principle, answer for TWS's window. There is no Windows API to tell
  which process answered;
- patching another process's memory, or its own to look like TWS.

Keep the machine itself clean, keep the token out of the permanent environment where you can (below), and consider
installing TWS where your user cannot write (and keeping it updated by hand) if that matters to you.

The decision and its trade-offs: [ADR 0002](adr/0002-trust-only-ibkr-signed-tws.md).

## The second factor

A live login needs the Mobile Authenticator one-time code. For the tool to enter it, 1Password must hold the
**secret** the code is made from, in the same item as the password, or at least where the same token can read it.

**That turns two factors into one.** Anyone who can read that item, or who holds a service account token that can,
has everything needed to log into the account from anywhere. This is the price of an unattended live login. It may
be worth paying for you; decide deliberately:

- If someone is always at the machine when TWS starts, **leave the one-time password field out**: the tool fills the
  form and waits for you to type the code or approve on your phone. The password alone is then not enough.
- If you do store it, follow the setup below, and consider keeping IB Key on your phone as well so that you can
  still log in when this machine is unavailable.
- Paper accounts have no second factor; their item holds no secret worth this concern beyond the paper password.

## 1Password setup

- **A vault just for this.** Put the TWS items, and nothing else, in a vault of their own (e.g. `Brokerage`).
- **A service account scoped to that vault, read-only**, for unattended runs: *Developer → Service Accounts* in
  1Password, access to that one vault, *read items* only. Then a leaked token exposes the TWS items and nothing
  else in your 1Password.
- **Where the token lives.** `OP_SERVICE_ACCOUNT_TOKEN` as a permanent user environment variable is readable by every
  program you run. Prefer setting it only in the scheduled task or launcher that runs the tool. On a machine that
  is not dedicated to trading, prefer your own 1Password sign-in (the app's CLI integration), which asks you to
  approve access.
- **Separate items for paper and live**, and pass `--mode` from your launcher, so a mistake aborts instead of
  sending live credentials to a paper TWS or the reverse.

## Ini files

An IBC-style ini file holds the password **in plain text** and no second factor. If you use one:

- keep it under your user profile and remove access for anyone else:
  `icacls tws-paper.ini /inheritance:r /grant:r "${env:USERNAME}:R"` (PowerShell);
- never commit it (the repository's `.gitignore` ignores `*.ini`);
- prefer 1Password where you can.

## What stays in memory

The user name, password and code are held as ordinary .NET strings while the tool runs: they cannot be wiped, and
could appear in a crash dump or the page file. The tool runs for seconds to minutes and writes no dumps itself.
If Windows Error Reporting is set to keep local dumps (`LocalDumps` in the registry), a crash could leave one
under your profile.

## The Java Access Bridge

`jabswitch -enable` turns the Java Access Bridge on for every Java application your Windows user starts, not only
TWS. The bridge is how screen readers work with Java applications: any program running as you can then read and
operate those applications' controls, as this tool does with TWS. `jabswitch -disable` turns it off again.

## The release binary

Releases are built by GitHub Actions from a tagged commit, and carry a `SHA256SUMS` file and a
[build provenance attestation](https://docs.github.com/actions/security-for-github-actions/using-artifact-attestations):

```powershell
gh attestation verify tws-login-win.exe --repo <owner>/tws-login-win
```

The exe is not Authenticode-signed, so Windows SmartScreen may warn the first time you run it. Building from source
(`build\publish.ps1`, with the SDK pinned in `global.json` and packages pinned in `packages.lock.json`) gives the
same program, though not necessarily a byte-identical file.
