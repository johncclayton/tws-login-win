using tws_login_win;
using Xunit;

namespace tws_login_win.Tests;

/// <summary>Loading credentials from an ini file or 1Password, and keeping the values out of every message.</summary>
public sealed class CredentialSourceTests : IDisposable
{
    readonly string _folder = Directory.CreateTempSubdirectory("tws-login-win-tests").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    string Ini(string content)
    {
        var path = Path.Combine(_folder, "config.ini");
        File.WriteAllText(path, content);
        return path;
    }

    sealed class FakeOnePassword(params OnePasswordField[] fields) : IOnePasswordCli
    {
        public List<string> UrlLabels { get; } = [];

        /// <summary>What the item holds now; a test replaces it to model the code moving on.</summary>
        public OnePasswordField[] Fields { get; set; } = fields;

        public (string Vault, string Item)? Asked { get; private set; }

        public OnePasswordItem GetItem(string vault, string item)
        {
            Asked = (vault, item);
            return new OnePasswordItem(Fields, UrlLabels);
        }
    }

    static OnePasswordField Field(string label, string value, string? section = null) => new(label, section, value);

    static OnePasswordField Otp(string code, string? section = "Saved on example.com") =>
        new("one-time password", section, "otpauth://totp/x?secret=ABC", "OTP", code);

    [Fact]
    public void Ini_gives_user_password_and_mode()
    {
        var path = Ini("# IBC\nIbLoginId=paperusr\nIbPassword= pw with spaces \nTradingMode=Paper\nOther=1\n");

        var credentials = new IniCredentialSource(path).Load();

        Assert.Equal(new TwsCredentials("paperusr", " pw with spaces ", TradingMode.Paper), credentials);
    }

    [Fact]
    public void Ini_trims_user_and_mode_but_keeps_the_password_as_written()
    {
        var path = Ini("IbLoginId=  paperusr \r\nIbPassword=\tpw \r\nTradingMode= live \r\n");

        var credentials = new IniCredentialSource(path).Load();

        Assert.Equal(new TwsCredentials("paperusr", "\tpw ", TradingMode.Live), credentials);
    }

    [Theory]
    [InlineData("IbPassword=pw\nTradingMode=paper\n", "IbLoginId")]
    [InlineData("IbLoginId=u\nTradingMode=paper\n", "IbPassword")]
    [InlineData("IbLoginId=u\nIbPassword=pw\n", "TradingMode missing")]
    public void Ini_missing_a_setting_aborts(string content, string expected)
    {
        var ex = Assert.Throws<LoginAbortedException>(() => new IniCredentialSource(Ini(content)).Load());

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Ini_bad_mode_is_reported_without_its_value()
    {
        var ex = Assert.Throws<LoginAbortedException>(() => new IniCredentialSource(Ini("IbLoginId=u\nIbPassword=pw\nTradingMode=secretish\n")).Load());

        Assert.DoesNotContain("secretish", ex.Message);
    }

    [Fact]
    public void Missing_ini_file_aborts()
    {
        var ex = Assert.Throws<LoginAbortedException>(() => new IniCredentialSource(Path.Combine(_folder, "none.ini")).Load());

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void OnePassword_reads_three_fields_of_the_item()
    {
        var cli = new FakeOnePassword(Field("username", "liveusr"), Field("password", "pw"), Field("trading mode", "live"), Otp("123456"));

        var credentials = new OnePasswordCredentialSource("op://Brokerage/TWS Live/", cli).Load();

        Assert.Equal(("liveusr", "pw", TradingMode.Live), (credentials.User, credentials.Password, credentials.Mode));
        Assert.NotNull(credentials.OneTimeCode);
        Assert.Equal(("Brokerage", "TWS Live"), cli.Asked);
    }

    [Fact]
    public void OnePassword_field_in_a_section_is_found()
    {
        var cli = new FakeOnePassword(Field("username", "u"), Field("password", "pw"), Field("Trading Mode", "paper", "Automation"));

        Assert.Equal(TradingMode.Paper, new OnePasswordCredentialSource("op://V/I", cli).Load().Mode);
    }

    [Fact]
    public void OnePassword_field_outside_sections_wins_over_one_in_a_section()
    {
        var cli = new FakeOnePassword(
            Field("username", "u"), Field("password", "saved-by-browser", "Saved on example.com"), Field("password", "pw"),
            Field("trading mode", "live"), Otp("123456"));

        Assert.Equal("pw", new OnePasswordCredentialSource("op://V/I", cli).Load().Password);
    }

    [Fact]
    public void OnePassword_field_in_two_sections_aborts_naming_them_not_the_values()
    {
        var cli = new FakeOnePassword(
            Field("username", "u"), Field("password", "first-secret", "A"), Field("password", "second-secret", "B"),
            Field("trading mode", "live"));

        var ex = Assert.Throws<LoginAbortedException>(() => new OnePasswordCredentialSource("op://V/I", cli).Load());

        Assert.Contains("'A', 'B'", ex.Message);
        Assert.DoesNotContain("secret", ex.Message);
    }

    [Fact]
    public void OnePassword_website_entry_instead_of_a_field_is_explained()
    {
        var cli = new FakeOnePassword(Field("username", "u"), Field("password", "pw"));
        cli.UrlLabels.Add("trading mode");

        var ex = Assert.Throws<LoginAbortedException>(() => new OnePasswordCredentialSource("op://V/I", cli).Load());

        Assert.Contains("website entry", ex.Message);
    }

    [Fact]
    public void OnePassword_live_item_without_a_one_time_code_field_aborts()
    {
        var cli = new FakeOnePassword(Field("username", "u"), Field("password", "pw"), Field("trading mode", "live"));

        var ex = Assert.Throws<LoginAbortedException>(() => new OnePasswordCredentialSource("op://V/I", cli).Load());

        Assert.Contains("no field of type OTP", ex.Message);
    }

    [Fact]
    public void OnePassword_live_code_is_read_from_the_item_each_time_it_is_asked_for()
    {
        OnePasswordField[] Item(string code) => [Field("username", "u"), Field("password", "pw"), Field("trading mode", "live"), Otp(code)];
        var cli = new FakeOnePassword(Item("111111"));
        var credentials = new OnePasswordCredentialSource("op://V/I", cli).Load();
        cli.Fields = Item("222222");

        Assert.Equal("222222", credentials.OneTimeCode!.Current());
    }

    [Fact]
    public void OnePassword_code_that_is_not_six_digits_aborts_without_showing_it()
    {
        var cli = new FakeOnePassword(Field("username", "u"), Field("password", "pw"), Field("trading mode", "live"), Otp("12a456"));
        var credentials = new OnePasswordCredentialSource("op://V/I", cli).Load();

        var ex = Assert.Throws<LoginAbortedException>(() => credentials.OneTimeCode!.Current());

        Assert.DoesNotContain("12a456", ex.Message);
    }

    [Fact]
    public void OnePassword_live_item_with_two_one_time_code_fields_aborts_naming_their_sections()
    {
        var cli = new FakeOnePassword(
            Field("username", "u"), Field("password", "pw"), Field("trading mode", "live"), Otp("111111", "A"), Otp("222222", "B"));

        var ex = Assert.Throws<LoginAbortedException>(() => new OnePasswordCredentialSource("op://V/I", cli).Load());

        Assert.Contains("'A', 'B'", ex.Message);
        Assert.DoesNotContain("111111", ex.Message);
    }

    [Fact]
    public void OnePassword_paper_item_has_no_code()
    {
        var cli = new FakeOnePassword(Field("username", "u"), Field("password", "pw"), Field("trading mode", "paper"));

        Assert.Null(new OnePasswordCredentialSource("op://V/I", cli).Load().OneTimeCode);
    }

    [Fact]
    public void OnePassword_empty_field_aborts()
    {
        var cli = new FakeOnePassword(Field("username", ""), Field("password", "pw"), Field("trading mode", "live"));

        Assert.Throws<LoginAbortedException>(() => new OnePasswordCredentialSource("op://V/I", cli).Load());
    }

    [Theory]
    [InlineData("op://V")]
    [InlineData("op://V/I/field")]
    public void OnePassword_reference_must_name_a_vault_and_an_item(string reference)
    {
        var ex = Assert.Throws<LoginAbortedException>(() => new OnePasswordCredentialSource(reference, new FakeOnePassword()).Load());

        Assert.Contains("op://vault/item", ex.Message);
    }

    [Fact]
    public void OnePassword_item_json_from_op_is_read()
    {
        const string json = """
            {
              "id": "abc", "title": "TWS",
              "fields": [
                { "id": "username", "type": "STRING", "purpose": "USERNAME", "label": "username", "value": "u" },
                { "id": "notesPlain", "type": "STRING", "purpose": "NOTES", "label": "notesPlain" },
                { "id": "x1", "section": { "id": "s1", "label": "Saved on example.com" }, "type": "CONCEALED", "label": "password", "value": "p2" },
                { "id": "x2", "section": { "id": "add more" }, "type": "STRING", "label": "trading mode", "value": "live" }
              ],
              "urls": [ { "label": "website", "primary": true, "href": "https://example.com" }, { "href": "https://other.example.com" } ]
            }
            """;

        var item = OnePasswordItem.FromJson(json);

        Assert.Equal(
            [new("username", null, "u"), new("notesPlain", null, ""), new("password", "Saved on example.com", "p2", "CONCEALED"),new("trading mode", null, "live")],
            item.Fields);
        Assert.Equal(["website"], item.UrlLabels);
    }

    [Fact]
    public void OnePassword_item_json_keeps_field_types_and_the_current_code()
    {
        const string json = """
            {
              "fields": [
                { "id": "password", "type": "CONCEALED", "purpose": "PASSWORD", "label": "password", "value": "pw" },
                { "id": "x3", "section": { "id": "s1", "label": "Saved on example.com" }, "type": "OTP", "label": "one-time password",
                  "value": "otpauth://totp/x?secret=ABC", "totp": "123456" }
              ]
            }
            """;

        var item = OnePasswordItem.FromJson(json);

        Assert.Equal("CONCEALED", item.Fields[0].Type);
        Assert.Null(item.Fields[0].Totp);
        Assert.Equal("OTP", item.Fields[1].Type);
        Assert.Equal("123456", item.Fields[1].Totp);
    }

    [Fact]
    public void Spec_chooses_the_source()
    {
        var cli = new FakeOnePassword();

        Assert.IsType<OnePasswordCredentialSource>(CredentialSources.FromSpec("op://V/I", NoVariables, cli));
        Assert.IsType<IniCredentialSource>(CredentialSources.FromSpec(@"C:\x\config.ini", NoVariables, cli));
    }

    static string? NoVariables(string name) => null;

    static Func<string, string?> Variables(params (string Name, string Value)[] set) =>
        name => set.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void Live_loads_the_item_named_by_its_environment_variable()
    {
        var cli = new FakeOnePassword(Field("username", "u"), Field("password", "pw"), Field("trading mode", "live"), Otp("123456"));
        var environment = Variables(("TWS_LOGIN_LIVE_1PASSWORD_ITEM", "op://Brokerage/L"), ("TWS_LOGIN_PAPER_1PASSWORD_ITEM", "op://Brokerage/P"));

        var credentials = CredentialSources.FromSpec("live", environment, cli).Load();

        Assert.Equal(TradingMode.Live, credentials.Mode);
        Assert.Equal(("Brokerage", "L"), cli.Asked);
    }

    [Fact]
    public void Paper_with_its_environment_variable_unset_aborts_naming_the_variable()
    {
        var ex = Assert.Throws<LoginAbortedException>(() => CredentialSources.FromSpec("paper", NoVariables, new FakeOnePassword()).Load());

        Assert.Contains("TWS_LOGIN_PAPER_1PASSWORD_ITEM", ex.Message);
    }

    [Fact]
    public void Paper_aborts_when_its_item_is_for_live_trading()
    {
        var cli = new FakeOnePassword(Field("username", "u"), Field("password", "pw"), Field("trading mode", "live"), Otp("123456"));
        var source = CredentialSources.FromSpec("paper", Variables(("TWS_LOGIN_PAPER_1PASSWORD_ITEM", "op://V/L")), cli);

        var ex = Assert.Throws<LoginAbortedException>(source.Load);

        Assert.Contains("Live", ex.Message);
        Assert.Contains("op://V/L", ex.Message);
    }

    [Fact]
    public void A_source_held_to_a_mode_aborts_when_its_file_is_for_the_other_mode()
    {
        var path = Ini("IbLoginId=u\nIbPassword=pw\nTradingMode=live\n");
        var source = CredentialSources.ForMode(new IniCredentialSource(path), TradingMode.Paper);

        var ex = Assert.Throws<LoginAbortedException>(source.Load);

        Assert.Equal($"Paper login asked for, but ini file {path} is for Live trading.", ex.Message);
    }

    [Fact]
    public void A_source_held_to_its_own_mode_loads()
    {
        var source = CredentialSources.ForMode(new IniCredentialSource(Ini("IbLoginId=u\nIbPassword=pw\nTradingMode=paper\n")), TradingMode.Paper);

        Assert.Equal(TradingMode.Paper, source.Load().Mode);
    }

    [Fact]
    public void Credentials_print_lengths_not_values()
    {
        var text = new TwsCredentials("paperusr", "s3cret", TradingMode.Paper).ToString();

        Assert.Equal("Paper credentials (user 8 chars, password 6 chars)", text);
    }

    [Fact]
    public void Check_fetches_the_code_and_prints_its_length_only()
    {
        var cli = new FakeOnePassword(Field("username", "liveusr"), Field("password", "pw"), Field("trading mode", "live"), Otp("123456"));
        var credentials = new OnePasswordCredentialSource("op://V/I", cli).Load();

        Assert.Equal("Live credentials (user 7 chars, password 2 chars, code 6 digits)", credentials.Check());
    }

    [Fact]
    public void Check_without_a_code_source_says_so()
    {
        var text = new TwsCredentials("paperusr", "s3cret", TradingMode.Paper).Check();

        Assert.Equal("Paper credentials (user 8 chars, password 6 chars, no code)", text);
    }

    [Fact]
    public void One_password_fields_and_items_never_print_their_values()
    {
        var field = new OnePasswordField("password", "Saved", "hunter2-secret", "CONCEALED", "123456");
        var item = new OnePasswordItem([field], []);

        foreach (var text in new[] { field.ToString(), item.ToString(), $"{field}", $"{item}" })
        {
            Assert.DoesNotContain("hunter2", text);
            Assert.DoesNotContain("123456", text);
        }
    }

    [Fact]
    public void Op_is_found_only_in_fully_qualified_PATH_folders()
    {
        var bin = Directory.CreateDirectory(Path.Combine(_folder, "bin")).FullName;
        File.WriteAllBytes(Path.Combine(bin, "op.exe"), [0x4D, 0x5A]);
        var here = Directory.GetCurrentDirectory();

        Assert.Equal(Path.Combine(bin, "op.exe"), OpCli.Find($@".;relative\dir;""{bin}"";C:\nowhere"));
        Assert.Null(OpCli.Find(@".;.\bin"));
        Assert.Null(OpCli.Find(null));
        Assert.Equal(here, Directory.GetCurrentDirectory());
    }

    [Theory]
    [InlineData(true, "Agilebits", true)]
    [InlineData(true, "AgileBits Inc.", true)]
    [InlineData(true, "Someone Else", false)]
    [InlineData(false, "Agilebits", false)]
    public void Op_must_be_validly_signed_by_AgileBits(bool valid, string organization, bool trusted)
    {
        Assert.Equal(trusted, OpCli.IsPublisher(new Signature(valid, organization, "")));
    }

    [Fact]
    public void The_user_name_never_appears_in_a_log_line_once_known()
    {
        Assert.Equal("dialog: welcome [user] / [user]", ConsoleLog.Redacted("dialog: welcome jdoe42 / JDOE42", "jdoe42"));
        Assert.Equal("nothing to hide", ConsoleLog.Redacted("nothing to hide", null));
    }
}
