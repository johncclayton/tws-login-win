# tws-login-win

Logs **Interactive Brokers Trader Workstation (TWS)** in on Windows, unattended: it picks the Live or Paper tab,
fills in the user name and password, presses Log In, enters the Mobile Authenticator one-time code, and answers the
dialogs TWS shows on the way (paper warning, existing session, re-login).

It drives TWS's own controls through the **Java Access Bridge** that ships with TWS's Java runtime: no screen
reading, no mouse or keyboard input, and TWS need not be in front. Window size, display scaling and layout shifts do
not matter.

> **Not affiliated with Interactive Brokers.** "Trader Workstation" and "TWS" are Interactive Brokers' names, used
> here only to say what this tool works with. Automating a live login, and storing a second factor next to its
> password, is your decision and your risk: read [Security model](docs/security-model.md) first, and check that it
> fits your account's terms.

| | |
|---|---|
| Works with | TWS **1051** on Windows 10/11 x64, **English** user interface ([why](#limits)) |
| Not supported | IB Gateway, other TWS languages, macOS/Linux |
| Credentials from | a 1Password item (with the one-time code), or an IBC-style ini file (you type the code) |
| Status | 0.1.0, early: verified by its author on paper and live logins |

## Quick start

These steps log a **paper** account in. They take about ten minutes, most of it the 1Password item.

1. **Download** `tws-login-win.exe` from [Releases](../../releases) and check it against `SHA256SUMS`:

   ```powershell
   (Get-FileHash .\tws-login-win.exe -Algorithm SHA256).Hash   # must match the line in SHA256SUMS
   ```

   It is one self-contained file: no .NET install needed. Put it anywhere, e.g. `C:\Tools\tws-login-win.exe`.

2. **Install the 1Password CLI** and sign it in (the 1Password app's *Settings → Developer → Integrate with
   1Password CLI* is the easiest way):

   ```powershell
   winget install AgileBits.1Password.CLI
   op whoami            # in a new terminal: must show your account
   ```

3. **Turn on the Java Access Bridge** for your Windows user, once, then **restart TWS**:

   ```powershell
   C:\Jts\1051\jre\bin\jabswitch -enable
   ```

4. **Create a 1Password item** for the paper account (type *Login* is fine), with these fields:

   | Field label | Type | Value |
   |---|---|---|
   | `username` | text | your paper account's TWS user name |
   | `password` | password | its password |
   | `trading mode` | text | `paper` |

   Then point the tool at it (use the vault and item names, or their IDs):

   ```powershell
   [Environment]::SetEnvironmentVariable('TWS_LOGIN_PAPER_1PASSWORD_ITEM', 'op://Brokerage/TWS Paper', 'User')
   ```

5. **Check the credentials** without touching TWS (open a new terminal first so the variable is set). It prints
   lengths and the mode, never the values:

   ```powershell
   tws-login-win check-creds paper
   # [09:00:01.234] 1Password item op://Brokerage/TWS Paper: Paper credentials (user 9 chars, password 20 chars, no code)
   ```

6. **Log in**: start TWS so it shows its Login window, then:

   ```powershell
   tws-login-win login paper --mode paper
   # ... set user name (9 chars) ... pressed Paper Log In ... paper warning accepted ...
   # [09:00:14.567] paper login complete
   ```

   Exit code 0 means logged in. Anything else ends with a line `ABORT: <reason>`, having sent nothing more: see
   [Troubleshooting](#troubleshooting).

For a **live** account, do the same with `trading mode` = `live`, a **one-time password** field holding the
Mobile Authenticator secret (see [Live accounts](#live-accounts)), and `TWS_LOGIN_LIVE_1PASSWORD_ITEM`.

## Prerequisites

| What | Needed for | Check |
|---|---|---|
| Windows 10 or 11, x64 | everything | |
| TWS 1051, English UI | everything | `C:\Jts\1051\jre\bin` exists |
| Java Access Bridge on (`jabswitch -enable`, then restart TWS) | everything that reads TWS | `%USERPROFILE%\.accessibility.properties` exists |
| [1Password CLI](https://developer.1password.com/docs/cli/) `op`, signed in | `live`, `paper`, `op://…` credentials | `op whoami` |
| .NET 10 SDK | only building from source | `dotnet --version` |

`jabswitch -enable` writes `%USERPROFILE%\.accessibility.properties`, which turns the bridge on for **every** Java
application that Windows user starts; `jabswitch -disable` undoes it. TWS loads the bridge only when it starts, so
restart TWS after enabling it.

## Configuration

### Credentials

Every `login` and `check-creds` names its credentials; there is no default:

| Credentials | Meaning |
|---|---|
| `paper` | the 1Password item in `TWS_LOGIN_PAPER_1PASSWORD_ITEM`; its `trading mode` must be `paper` |
| `live` | the 1Password item in `TWS_LOGIN_LIVE_1PASSWORD_ITEM`; its `trading mode` must be `live` |
| `op://vault/item` | that 1Password item, either mode (vault and item by name or ID) |
| any other text | the path of an IBC-style ini file |

### Environment variables

They hold references and a token, never the credentials themselves.

| Variable | Needed for | Value |
|---|---|---|
| `TWS_LOGIN_PAPER_1PASSWORD_ITEM` | `paper` | `op://<vault>/<item>` of the paper account |
| `TWS_LOGIN_LIVE_1PASSWORD_ITEM` | `live` | `op://<vault>/<item>` of the live account |
| `OP_SERVICE_ACCOUNT_TOKEN` | unattended runs (no one there to unlock 1Password) | a [1Password service account](https://developer.1password.com/docs/service-accounts/) token, read by `op` itself |

Without a service account token, `op` uses your own sign-in and may ask you to unlock 1Password. Before using a
service account, read [Security model](docs/security-model.md#1password-setup).

### The 1Password item

| Field label (any case) | Type | Value | |
|---|---|---|---|
| `username` | any | the TWS user name | required |
| `password` | any | the TWS password | required |
| `trading mode` | text | `paper` or `live` | required |
| *(any label)* | **one-time password** | the Mobile Authenticator secret | live only: exactly one |

Fields are found by label, ignoring case: outside any section first, otherwise in the one section that has it. A
login saved by a browser adds sections repeating these labels; that is fine unless two sections both have one and
none is outside a section. A *website* entry labelled `username` does not count: add a text field instead (the error
message says so).

#### Live accounts

TWS asks a live login for a second factor. Set up IBKR's **Mobile Authenticator app** option for the account (in
IBKR's Client Portal, under its Secure Login System settings) and, when it shows the QR code, add it to the 1Password item
as a **one-time password** field instead of, or as well as, a phone app. The tool then reads the current code from
1Password on the code screen. Doing this puts both factors in one place: read
[Security model](docs/security-model.md#the-second-factor) before you do.

With no one-time password field (or with an ini file), a live login fills the form, presses Log In and waits for you
to approve or type the code yourself.

### IBC-style ini file

The same keys as [IBC](https://github.com/IbcAlpha/IBC)'s `config.ini`, so an existing file works:

```ini
IbLoginId=your-user-name
IbPassword=your-password
TradingMode=paper
```

`IbPassword` is taken exactly as written after `=`, spaces included; the other two are trimmed. Other keys are
ignored. The file holds the password in plain text: restrict it to your user (see
[Security model](docs/security-model.md#ini-files)). It holds no second factor.

## Commands

```
tws-login-win <command> [options]

  login <credentials> [login-wait-s] [logged-in-wait-s] [--mode paper|live] [--pid n [--existing]]
  check-creds <credentials> [--mode paper|live]
  inspect
  login-windows
  watch [seconds]
  --version | --help
```

| Command | Does |
|---|---|
| `login` | waits up to `login-wait-s` (default 300) for a Login window, logs it in, and waits up to `logged-in-wait-s` (default 180) after Log In for the main window |
| `check-creds` | loads the credentials (and, for live, fetches a code) without touching TWS; prints lengths and mode only |
| `inspect` | prints the Login window's controls once; fields that may hold typed text by length only |
| `login-windows` | prints one line `<pid> <screen>` per visible Login window to stdout (screen: `form`, `second-factor`, `other`, `unknown`); its log goes to stderr. For launchers |
| `watch` | logs new TWS windows and the Login window's and dialogs' controls as they change: for learning what TWS shows, e.g. while you log in by hand |

| Option | Does |
|---|---|
| `--mode paper\|live` | aborts before sending anything unless the credentials are for this mode |
| `--pid n` | only TWS process `n` counts, so another TWS's Login window is never taken; aborts within about a second once `n` exits |
| `--existing` | with `--pid`: judges `n`'s Login window once instead of waiting; aborts unless it shows the Login form |
| `--trust-unsigned` | also drives a TWS not signed by Interactive Brokers, and runs an `op` not signed by AgileBits. **Unsafe**: see [Security model](docs/security-model.md#which-tws-gets-the-credentials) |

**Exit codes:** `0` done (`login-windows`: also with none listed), `1` aborted (the last line is
`ABORT: <reason>`), `2` usage error. With its output redirected, the tool writes UTF-8.

### Running it from a launcher

To start TWS and log it in from a script, start TWS yourself and tie the login to that process:

```powershell
$tws = Start-Process C:\Jts\1051\tws1.exe -PassThru
tws-login-win login paper --mode paper --pid $tws.Id
```

Start the next TWS only once the previous one has finished logging in: with two Login windows showing and no
`--pid`, `login` stops without sending anything, as which one to log in would be a guess.

## What it does, and what it never does

**On the Login form** it presses the tab for the mode and confirms it is selected (with the SIMULATED TRADING banner
for paper only), sets the user name and reads it back, sets the password and checks its length (a password is never
read back), and presses Log In only once it is enabled **and** the form has not changed for a second.

**After Log In** only windows of that TWS process count. It is done when TWS's main window shows (and, for paper,
the paper warning is accepted). On the way:
- the **paper warning** is accepted;
- **Existing session detected** is answered with *Continue Login*, which **disconnects your other session** (e.g. on
  your phone);
- **Re-login is required** is answered with *Re-login* once per run; a second time aborts, so it never fights
  another session;
- on the **code screen** with a code source, it fetches the code (waiting for the next one if fewer than 5 s of the
  current one are left), sets it, reads all 6 digits back and presses OK, once per login;
- on any other **second-factor screen** (device choice, IB Key approval), or the code screen without a code
  source, it logs that it is waiting for you and sends nothing.

**It aborts, pressing nothing more,** on a TWS *Login Error*, an unrecognised dialog, a Login form that comes back
after Log In, a code TWS asks for again, or a Login window that stops changing for 60 s.

**It never** presses Cancel or *Enter Read Only*, changes your second-factor device, reads TWS's main window (it
shows account data), or logs a user name, password or code. Account numbers in window titles and digits in TWS's
messages are masked in every log line.

## Troubleshooting

Every failure ends with `ABORT: <reason>`. The common ones:

| Message contains | Fix |
|---|---|
| `the Java Access Bridge is off` | `C:\Jts\1051\jre\bin\jabswitch -enable`, then **restart TWS** |
| `no visible TWS Login window` / `no ready TWS Login form within …` | start TWS, or give it longer: `login paper 600` |
| `ignoring pid … not by 'Interactive Brokers Group, Inc.'` (logged before) | the window belongs to a program not signed by IBKR: see [Security model](docs/security-model.md#which-tws-gets-the-credentials) |
| `TWS_LOGIN_PAPER_1PASSWORD_ITEM is not set` | set it (step 4 of the quick start) and open a new terminal |
| `1Password CLI 'op' not found on PATH` | install it and open a new terminal |
| `op.exe … not by 'AgileBits'` | the first `op.exe` on `PATH` is not 1Password's: remove it or reorder `PATH` |
| `op item get … failed` | the reason from `op` follows: not signed in, wrong vault or item name, or no access |
| `has no field 'username'` / `has 'username' as a website entry` | add or rename the field as in [The 1Password item](#the-1password-item) |
| `is live but has no field of type OTP` | add the one-time password field, or log in with an ini file and type the code |
| `paper login asked for, but … is for Live trading` | `--mode` and the item's `trading mode` differ: the right item for the right TWS |
| `TWS refused the login: 'Login failed - incorrect security code'` | this PC's clock is off, or the stored secret is not the account's; compare the code 1Password shows with TWS's expectations by logging in by hand once |
| `unrecognised dialog` | TWS showed something new; its text is in the message. Please [open an issue](CONTRIBUTING.md#reporting-a-problem) with the `watch` output |
| `TWS Login windows show … log in one TWS at a time` | log one TWS in at a time, or pass `--pid` |

To see exactly what TWS shows, run `tws-login-win watch` in one terminal and log in by hand: it prints each
screen's controls with typed text shown by length only.

## Limits

- **English TWS only.** It finds controls by their English names ("Log In", "Paper Trading", "Existing session
  detected"). Another language aborts safely, but does not log in.
- **TWS 1051.** Other versions probably work while IBKR keeps the Login window's controls the same; reports welcome.
- **No IB Gateway.** Gateway's login window is different.
- **One login at a time** per Login window; live and paper may run side by side as separate TWS processes.

## How it compares to IBC

[IBC](https://github.com/IbcAlpha/IBC) is the established open-source way to run TWS and IB Gateway unattended: it
runs inside TWS's Java process, handles far more dialogs, restarts and shutdowns, and supports Gateway. Use it if
it does what you need. tws-login-win is smaller: an outside program that only logs in, through the accessibility
interface, and can enter the one-time code from 1Password. It reads IBC's ini keys, so the two can share a file.

## Building from source

```powershell
dotnet test                                  # about 150 tests; no TWS needed
.\build\publish.ps1                          # artifacts\tws-login-win.exe and artifacts\SHA256SUMS
```

Design notes: [GLOSSARY.md](GLOSSARY.md) (terms), [docs/adr](docs/adr) (decisions),
[docs/tws-notes.md](docs/tws-notes.md) (what TWS shows through the bridge).

## License

[MIT](LICENSE).
