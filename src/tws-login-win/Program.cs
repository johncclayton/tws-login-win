using System.Text;

namespace tws_login_win;

/// <summary>
/// Entry point; the commands are listed in <see cref="Usage"/>. Exit codes: 0 done (login-windows: also with none
/// listed), 1 aborted (the last line is "ABORT: reason"), 2 usage error.
/// Needs the Java Access Bridge: jabswitch -enable once per Windows user, then restart TWS.
/// </summary>
static class Program
{
    const int Done = 0, Aborted = 1, UsageError = 2;

    public const string Usage = """
        tws-login-win <command> [options]

          login <credentials> [login-wait-s] [logged-in-wait-s] [--mode paper|live] [--pid n [--existing]]
                                  log the TWS Login window in; with a code source, enters the one-time code too
          check-creds <credentials> [--mode paper|live]
                                  load the credentials, touching no TWS; prints lengths and mode only
          inspect                 print the Login window's controls once (fields by length only)
          login-windows           print "<pid> <screen>" per visible Login window
          watch [seconds]         log new TWS windows and their controls as they change (default 150 s)

        <credentials>: live | paper (the 1Password item in TWS_LOGIN_LIVE_1PASSWORD_ITEM or
                       TWS_LOGIN_PAPER_1PASSWORD_ITEM), op://vault/item, or the path of an IBC-style ini file

          --mode paper|live       abort before sending anything unless the credentials are for this mode
          --pid n                 only TWS process n counts; abort within about a second once it exits
          --existing              with --pid: judge n's Login window once; abort unless it shows the Login form
          --trust-unsigned        also drive a TWS not signed by Interactive Brokers (unsafe; see SECURITY.md)
          --version, --help

        Exit codes: 0 done, 1 aborted (last line "ABORT: <reason>"), 2 usage error.
        """;

    static string Version =>
        typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            is [System.Reflection.AssemblyInformationalVersionAttribute v, ..] ? v.InformationalVersion : "unknown";

    static int Main(string[] commandLine)
    {
        // Redirected (e.g. a launcher writing a log file), the output is UTF-8 so every message reads as written.
        if (Console.IsOutputRedirected)
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
        if (Console.IsErrorRedirected)
            Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
        if (commandLine is ["--version"] or ["version"])
        {
            Console.WriteLine($"tws-login-win {Version}");
            return Done;
        }
        if (commandLine is [] or ["--help"] or ["-h"] or ["help"] or ["/?"])
        {
            Console.WriteLine(Usage);
            return commandLine is [] ? UsageError : Done;
        }
        // Composition root: the only place that knows the concrete Windows adapters.
        // login-windows' stdout is only its "<pid> <screen>" lines, for launchers to parse; its log goes to stderr.
        var log = new ConsoleLog(commandLine.FirstOrDefault() == "login-windows" ? Console.Error : Console.Out);
        var signatures = new Authenticode();
        var trustUnsigned = commandLine.Contains("--trust-unsigned");
        var tools = new TwsTools(new Win32Desktop(), new JavaAccessBridge(signatures, requireSigned: !trustUnsigned),
            new MessagePumpingClock(), log);
        string[] args = commandLine;
        TradingMode? mode = null;
        ICredentialSource Credentials(int index)
        {
            var source = CredentialSources.FromSpec(
                Arg(args, index) ?? throw new FormatException($"{args[0]} needs live, paper, an ini path or op://vault/item."),
                Environment.GetEnvironmentVariable, new OpCli());
            return mode is { } wanted ? CredentialSources.ForMode(source, wanted) : source;
        }

        try
        {
            var parsed = CommandLine.Parse(commandLine);
            args = [.. parsed.Args];
            mode = parsed.Mode;
            log.Info($"tws-login-win {Version}");
            if (parsed.TrustUnsigned)
                log.Info("WARNING: --trust-unsigned: credentials may go to a program that only looks like TWS.");
            else
                tools = TrustedTws.Scope(tools, new Win32ProcessImages(), signatures);
            if (parsed.Pid is { } pid)
            {
                var process = new OneTwsProcess(pid, Win32Process.HasExited(pid));
                process.ThrowIfExited();
                tools = process.Scope(tools);
            }
            var locator = new LoginFormLocator(tools);
            switch (args[0])
            {
                case "inspect":
                    var form = locator.FindNow() ?? throw new LoginAbortedException("No visible TWS Login window found.");
                    log.Info($"login window {form.Window}");
                    using (var tree = form.Window.Controls())
                        foreach (var line in ControlDump.Lines(tree))
                            log.Info("  " + line);
                    break;
                case "login":
                    var source = Credentials(1);
                    var credentials = source.Load();
                    log.Info($"loaded {credentials} from {source.Description}");
                    var timeouts = new LoginTimeouts(
                        Seconds(args, 2) ?? LoginTimeouts.Default.LoginForm,
                        Seconds(args, 3) ?? LoginTimeouts.Default.LoggedIn);
                    new LoginFlow(locator, log).Run(credentials, timeouts, parsed.Existing);
                    break;
                case "login-windows":
                    // Plain lines, no time stamps: read by launchers.
                    foreach (var window in LoginWindows.Lines(tools))
                        Console.WriteLine(window);
                    break;
                case "watch":
                    new WindowWatcher(tools).Watch(Seconds(args, 1) ?? TimeSpan.FromSeconds(150));
                    break;
                case "check-creds":
                    var checkedSource = Credentials(1);
                    log.Info($"{checkedSource.Description}: {checkedSource.Load().Check()}");
                    break;
                default:
                    Console.Error.WriteLine($"unknown command '{args[0]}'.{Environment.NewLine}{Environment.NewLine}{Usage}");
                    return UsageError;
            }
            return Done;
        }
        catch (LoginAbortedException ex)
        {
            log.Info("ABORT: " + ex.Message);
            return Aborted;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
                                       or System.ComponentModel.Win32Exception)
        {
            log.Info($"ABORT: the Java Access Bridge or Windows failed: {ex.Message}");
            return Aborted;
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine($"{ex.Message} (tws-login-win --help)");
            return UsageError;
        }
    }

    static string? Arg(string[] args, int index) => index < args.Length ? args[index] : null;

    static TimeSpan? Seconds(string[] args, int index) =>
        Arg(args, index) is { } text
            ? int.TryParse(text, out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : throw new FormatException($"argument {index + 1} must be a positive number of seconds, not '{text}'.")
            : null;
}

/// <summary>
/// The command line: the positional arguments, as they always were, <c>login</c>'s options <c>--pid n</c> and
/// <c>--existing</c>, and <c>--mode paper|live</c> for <c>login</c> and <c>check-creds</c>, which may come anywhere
/// after the command. Anything wrong is a <see cref="FormatException"/>.
/// </summary>
public sealed record CommandLine(IReadOnlyList<string> Args, int? Pid, bool Existing, TradingMode? Mode = null, bool TrustUnsigned = false)
{
    public static CommandLine Parse(string[] args)
    {
        var positional = new List<string>();
        int? pid = null;
        bool existing = false;
        TradingMode? mode = null;
        bool trustUnsigned = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pid":
                    if (pid is not null)
                        throw new FormatException("--pid given twice.");
                    pid = i + 1 < args.Length && int.TryParse(args[i + 1], out var id) && id > 0
                        ? id
                        : throw new FormatException("--pid needs a process id.");
                    i++;
                    break;
                case "--existing":
                    existing = true;
                    break;
                case "--trust-unsigned":
                    trustUnsigned = true;
                    break;
                case "--mode":
                    if (mode is not null)
                        throw new FormatException("--mode given twice.");
                    mode = (i + 1 < args.Length ? args[i + 1].ToLowerInvariant() : null) switch
                    {
                        "paper" => TradingMode.Paper,
                        "live" => TradingMode.Live,
                        _ => throw new FormatException("--mode needs paper or live."),
                    };
                    i++;
                    break;
                case var option when option.StartsWith("--", StringComparison.Ordinal):
                    throw new FormatException($"unknown option '{option}'.");
                default:
                    positional.Add(args[i]);
                    break;
            }
        }
        if ((pid is not null || existing) && positional.FirstOrDefault() != "login")
            throw new FormatException("--pid and --existing are for login only.");
        if (existing && pid is null)
            throw new FormatException("--existing needs --pid.");
        if (mode is not null && positional.FirstOrDefault() is not ("login" or "check-creds"))
            throw new FormatException("--mode is for login and check-creds only.");
        if (positional.Count == 0)
            throw new FormatException("no command given.");
        return new CommandLine(positional, pid, existing, mode, trustUnsigned);
    }
}
