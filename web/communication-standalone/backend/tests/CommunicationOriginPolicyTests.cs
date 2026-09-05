using KetoanMini.Communication.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace KetoanMini.Communication.Tests;

public sealed class CommunicationOriginPolicyTests
{
    [Theory]
    [InlineData("https://app.ketoancp.click", true)]
    [InlineData("http://app.ketoancp.click", true)]
    [InlineData("https://foreign.example", false)]
    [InlineData("https://app.ketoancp.click.foreign.example", false)]
    [InlineData("", true)]
    public void DistinguishesSameAndForeignOrigins(string origin, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("app.ketoancp.click");
        if (origin.Length > 0) context.Request.Headers.Origin = origin;

        Assert.Equal(expected, CommunicationOriginPolicy.IsAllowedOrigin(context, []));
    }
}
