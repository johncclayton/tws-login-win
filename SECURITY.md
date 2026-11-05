# Security policy

tws-login-win types brokerage credentials and one-time codes into another program, so security reports are
welcome and taken seriously.

## Reporting a vulnerability

Please **do not open a public issue**. Use GitHub's private reporting instead: the repository's **Security** tab →
**Report a vulnerability**. Include what you found, how to reproduce it, and what an attacker would gain. The maintainer
aims to answer within a week.

Never include real credentials, one-time-password secrets, account numbers or unredacted TWS screenshots in a
report. The output of `tws-login-win watch` and `inspect` is made to be shared (typed text by length, digits masked), but
read it before posting: TWS may show your name or other details in a label.

## Supported versions

Only the latest release gets fixes.

## Scope

In scope: anything that makes the tool reveal a user name, password, one-time code or account number (in logs,
messages, files or memory it controls); send credentials to a program other than a trusted TWS; or press, set or
accept something in TWS that its documentation says it never does.

Out of scope: the residual risks listed under "What the check does not stop" in the security model (code already
running as the same Windows user that changes TWS's own files, answers through the Java Access Bridge, patches
process memory, or reads the 1Password token or ini file directly), and the deliberate trade-off of storing the
second factor in 1Password. Both are described in [docs/security-model.md](docs/security-model.md).
