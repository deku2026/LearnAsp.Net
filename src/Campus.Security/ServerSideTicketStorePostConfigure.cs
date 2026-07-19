using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Campus.Security;

public sealed class ServerSideTicketStorePostConfigure(
    ServerSideTicketStore ticketStore,
    IOptions<ServerSideSessionOptions> sessionOptions)
    : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        if (string.Equals(name, sessionOptions.Value.CookieScheme, StringComparison.Ordinal))
        {
            options.SessionStore = ticketStore;
        }
    }
}
