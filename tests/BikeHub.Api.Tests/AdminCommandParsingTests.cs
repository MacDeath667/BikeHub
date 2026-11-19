using BikeHub.Api.Authentication;

namespace BikeHub.Api.Tests;

public sealed class AdminCommandParsingTests
{
    [Fact]
    public void Operator_requires_one_exact_email_and_never_accepts_extra_server_options()
    {
        Assert.Equal("rider@example.test", AdminAccountCommand.ParseEmail(["--grant-admin", " Rider@Example.Test "]));
        Assert.Throws<ArgumentException>(() => AdminAccountCommand.ParseEmail([]));
        Assert.Throws<ArgumentException>(() => AdminAccountCommand.ParseEmail(["--grant-admin"]));
        Assert.Throws<ArgumentException>(() => AdminAccountCommand.ParseEmail(["--grant-admin", "not an email"]));
        Assert.Throws<ArgumentException>(() => AdminAccountCommand.ParseEmail(["--grant-admin", "rider@example.test", "--urls=http://localhost:1"]));
        Assert.Throws<ArgumentException>(() => AdminAccountCommand.ParseEmail(["--urls=http://localhost:1", "rider@example.test"]));
    }
}
