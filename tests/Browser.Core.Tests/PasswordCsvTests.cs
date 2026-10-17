using Browser.Core;

public class PasswordCsvTests
{
    [Theory]
    [InlineData("name,url,username,password,note\r\nExample,https://example.test/login,ada,secret,\r\n")]
    [InlineData("\uFEFFurl,username,password,httpRealm,formActionOrigin,guid,timeCreated,timeLastUsed,timePasswordChanged\nhttps://example.test/login,ada,secret,,https://example.test,{},1,1,1\n")]
    [InlineData("Title,URL,Username,Password,Notes,OTPAuth\nExample,https://example.test/login,ada,secret,,\n")]
    [InlineData("folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp\n,,login,Example,,,,https://example.test/login,ada,secret,\n")]
    [InlineData("url;username;password\r\nhttps://example.test/login;ada;secret\r\n")]
    [InlineData("url\tusername\tpassword\nhttps://example.test/login\tada\tsecret\n")]
    public void Browser_and_password_manager_exports_import_the_same_login(string csv)
    {
        Assert.Equal(new ImportedLogin("https://example.test/login", "", "", "ada", "", "secret", "https://example.test/"), Assert.Single(PasswordImport.ReadCsv(csv)));
    }

    [Fact]
    public void Localized_exports_keep_delimiters_quotes_and_newlines_inside_passwords()
    {
        var csv = "Adresse;Identifiant;Mot de passe\r\nhttps://example.test/;ada;\"a;b,c\t\"\"d\"\"\nsecond line\"\r\n";
        Assert.Equal("a;b,c\t\"d\"\nsecond line", Assert.Single(PasswordImport.ReadCsv(csv)).Password);
    }

    [Fact]
    public void An_unclosed_quoted_password_is_not_imported_with_a_changed_value()
    {
        Assert.Empty(PasswordImport.ReadCsv("url,username,password\nhttps://example.test/,ada,\"unfinished"));
    }
}
