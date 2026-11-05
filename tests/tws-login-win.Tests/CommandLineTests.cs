using tws_login_win;
using Xunit;

namespace tws_login_win.Tests;

/// <summary>
/// The command line: the positional arguments as they always were, plus <c>login</c>'s <c>--pid n</c> and
/// <c>--existing</c>, which may come anywhere after the command.
/// </summary>
public class CommandLineTests
{
    [Fact]
    public void Without_options_the_arguments_are_kept_as_given()
    {
        var line = CommandLine.Parse(["login", "paper", "60", "120"]);

        Assert.Equal(["login", "paper", "60", "120"], line.Args);
        Assert.Null(line.Pid);
        Assert.False(line.Existing);
    }

    [Fact]
    public void Login_options_are_taken_out_wherever_they_are()
    {
        var line = CommandLine.Parse(["login", "--existing", "op://Brokerage/tws-paper", "--pid", "4242", "60"]);

        Assert.Equal(["login", "op://Brokerage/tws-paper", "60"], line.Args);
        Assert.Equal(4242, line.Pid);
        Assert.True(line.Existing);
    }

    [Theory]
    [InlineData("login paper --pid")]
    [InlineData("login paper --pid 0")]
    [InlineData("login paper --pid x")]
    [InlineData("login paper --pid 1 --pid 2")]
    [InlineData("login paper --existing")]
    [InlineData("login paper --wait")]
    [InlineData("watch --pid 4242")]
    [InlineData("login-windows --existing")]
    [InlineData("login paper --mode")]
    [InlineData("login paper --mode demo")]
    [InlineData("login paper --mode paper --mode live")]
    [InlineData("watch --mode paper")]
    public void Bad_options_are_usage_errors(string command)
    {
        Assert.Throws<FormatException>(() => CommandLine.Parse(command.Split(' ')));
    }

    [Theory]
    [InlineData("login x.ini --mode paper", TradingMode.Paper)]
    [InlineData("check-creds --mode LIVE x.ini", TradingMode.Live)]
    public void Mode_names_the_trading_mode_the_credentials_must_be_for(string command, TradingMode mode)
    {
        var line = CommandLine.Parse(command.Split(' '));

        Assert.Equal(mode, line.Mode);
        Assert.Equal(["x.ini"], line.Args.Skip(1));
    }

    [Fact]
    public void Without_mode_any_trading_mode_will_do()
    {
        Assert.Null(CommandLine.Parse(["login", "paper"]).Mode);
    }

    [Fact]
    public void Trust_unsigned_is_off_unless_asked_for_and_works_with_any_command()
    {
        Assert.False(CommandLine.Parse(["login", "paper"]).TrustUnsigned);
        Assert.True(CommandLine.Parse(["login-windows", "--trust-unsigned"]).TrustUnsigned);
        Assert.Equal(["watch", "30"], CommandLine.Parse(["watch", "--trust-unsigned", "30"]).Args);
    }

    [Fact]
    public void No_command_is_a_usage_error()
    {
        Assert.Throws<FormatException>(() => CommandLine.Parse(["--trust-unsigned"]));
    }
}
