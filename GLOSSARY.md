# TWS login

Logging Trader Workstation in through its own controls, the way an operator would: pick the account's tab, fill the fields, check what the form shows, log in.

## Language

### Accounts

**Trading mode**:
Whether an account trades real money (live) or simulated money (paper). Each set of credentials belongs to exactly one.
_Avoid_: environment, account type

**Credentials**:
The user name and password of one account, together with its trading mode.
_Avoid_: login, secrets

### Trust

**Trusted TWS**:
A running TWS whose program Interactive Brokers has signed. Only a trusted TWS's windows are looked at, and only a trusted TWS is ever given credentials or a one-time code.
_Avoid_: real TWS, genuine TWS, verified TWS

**Impostor**:
Any other program showing a window that looks like a TWS window. It is never read, pressed or typed into.
_Avoid_: fake TWS, spoof

### Screen

**TWS window**:
One window that TWS has on the desktop: the Login window, a dialog, the main window. Everything the tool sees or sends goes to one TWS window.
_Avoid_: form, screen, frame

**Login form**:
The contents of the Login window: the Live Trading and Paper Trading tabs, the user name and password fields, and the Log In button.
_Avoid_: login screen, login dialog

**Login tab**:
The Live Trading or Paper Trading tab of the Login form. The selected tab decides which trading mode the login is for.

**Paper warning**:
The dialog TWS shows after a paper login, saying it is not a brokerage account, with an "I understand and accept" button. Not every "Warning" dialog is one: TWS gives many unrelated dialogs that title.
_Avoid_: warning dialog (ambiguous)

**Existing-session dialog**:
The dialog TWS shows after a live Log In when the same user name is still logged in elsewhere, offering Continue Login (which disconnects the other session) or Cancel.
_Avoid_: duplicate login warning

### Second factor

**Second-factor screen**:
What the Login window shows, under the title "Second Factor Authentication", while a live login waits for its second factor: the code screen, the device choice, or a phone approval. It is a state of the Login window, not a TWS window of its own.
_Avoid_: second-factor window, 2FA dialog

**Code screen**:
The second-factor screen that asks for the Mobile Authenticator app code, with the code box, OK and Cancel.
_Avoid_: code prompt, OTP screen

**Code box**:
The input field below the code screen's label, where the one-time code is entered.
_Avoid_: code field

**Device choice**:
The second-factor screen listing the second-factor devices (IB Key, Mobile Authenticator app) to choose from. Reached only by the user; the tool never changes the device.
_Avoid_: device picker

**One-time code**:
The six-digit Mobile Authenticator code that a live login needs on the code screen. Each one is valid for one 30-second period.
_Avoid_: OTP, TOTP, 2FA code, token

**Code source**:
Where the one-time code for a live login comes from, kept alongside the account's credentials. Without one, the user types the code themselves.
_Avoid_: authenticator, OTP provider
