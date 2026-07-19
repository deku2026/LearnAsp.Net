using System.Net;
using System.Text.RegularExpressions;

namespace Part05_Security.IntegrationTests;

public sealed record BrowserLoginResult(
    HttpClient Browser,
    Uri AuthorizationUri,
    IReadOnlyList<string> AuthenticationSetCookies);

public static partial class OidcBrowser
{
    public static async Task<BrowserLoginResult> LoginAsync(
        string applicationBaseUrl,
        string loginPath,
        string username,
        string password,
        Func<string>? diagnostics = null)
    {
        var cookies = new CookieContainer();
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = cookies,
            UseCookies = true,
        };
        var browser = new HttpClient(handler)
        {
            BaseAddress = new Uri(applicationBaseUrl),
            Timeout = TimeSpan.FromSeconds(20),
        };

        using var challenge = await browser.GetAsync(loginPath);
        Assert.True(
            challenge.StatusCode == HttpStatusCode.Redirect,
            $"Expected OIDC redirect, got {(int)challenge.StatusCode}.{Environment.NewLine}{diagnostics?.Invoke()}");
        var authorizationUri = challenge.Headers.Location
            ?? throw new InvalidOperationException("OIDC challenge returned no Location header.");
        var pushedRequestUri = QueryValue(authorizationUri, "request_uri");
        if (pushedRequestUri is not null)
        {
            Assert.StartsWith("urn:ietf:params:oauth:request_uri:", pushedRequestUri);
        }
        else
        {
            Assert.Equal("S256", QueryValue(authorizationUri, "code_challenge_method"));
            Assert.False(string.IsNullOrWhiteSpace(QueryValue(authorizationUri, "code_challenge")));
            Assert.False(string.IsNullOrWhiteSpace(QueryValue(authorizationUri, "state")));
            Assert.False(string.IsNullOrWhiteSpace(QueryValue(authorizationUri, "nonce")));
        }

        var keycloakCookies = new Dictionary<string, string>(StringComparer.Ordinal);
        using var loginPage = await FollowKeycloakRedirectsToPageAsync(
            browser,
            authorizationUri,
            keycloakCookies,
            diagnostics);
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var loginAction = FormAction(loginHtml, "kc-form-login");
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, loginAction)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username,
                ["password"] = password,
                ["credentialId"] = "",
            }),
        };
        AddKeycloakCookies(loginRequest, keycloakCookies);

        using var loginResponse = await browser.SendAsync(loginRequest);
        CaptureKeycloakCookies(loginResponse, keycloakCookies);

        HttpResponseMessage callbackResponse;
        if (loginResponse.StatusCode == HttpStatusCode.Redirect)
        {
            var callback = ResolveLocation(
                loginAction,
                loginResponse.Headers.Location
                ?? throw new InvalidOperationException("Keycloak returned no callback location."));
            callbackResponse = await FollowRedirectsToApplicationAsync(
                browser,
                callback,
                new Uri(applicationBaseUrl),
                keycloakCookies,
                diagnostics);
        }
        else
        {
            var responseHtml = await loginResponse.Content.ReadAsStringAsync();
            Assert.True(
                loginResponse.IsSuccessStatusCode,
                $"Keycloak login returned {(int)loginResponse.StatusCode}: " +
                $"action={loginAction}; " +
                $"cookie-names=[{string.Join(", ", keycloakCookies.Keys)}]. " +
                $"{VisibleText(responseHtml)}.{Environment.NewLine}" +
                $"{diagnostics?.Invoke()}");
            var callback = FormAction(responseHtml, null);
            var fields = HiddenFields(responseHtml);
            callbackResponse = await browser.PostAsync(callback, new FormUrlEncodedContent(fields));
        }

        using (callbackResponse)
        {
            Assert.Equal(HttpStatusCode.Redirect, callbackResponse.StatusCode);
            var setCookies = callbackResponse.Headers.TryGetValues("Set-Cookie", out var values)
                ? values.ToArray()
                : [];
            var returnLocation = callbackResponse.Headers.Location ?? new Uri("/", UriKind.Relative);
            using var completed = await browser.GetAsync(returnLocation);
            completed.EnsureSuccessStatusCode();
            return new BrowserLoginResult(browser, authorizationUri, setCookies);
        }
    }

    private static async Task<HttpResponseMessage> FollowKeycloakRedirectsToPageAsync(
        HttpClient browser,
        Uri initialUri,
        IDictionary<string, string> keycloakCookies,
        Func<string>? diagnostics)
    {
        var currentUri = initialUri;
        for (var redirectCount = 0; redirectCount < 8; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            AddKeycloakCookies(request, keycloakCookies);
            var response = await browser.SendAsync(request);
            CaptureKeycloakCookies(response, keycloakCookies);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            if (response.StatusCode != HttpStatusCode.Redirect ||
                response.Headers.Location is null)
            {
                var statusCode = (int)response.StatusCode;
                var location = response.Headers.Location;
                response.Dispose();
                Assert.Fail(
                    $"Keycloak authorize returned {statusCode}; location={location}. " +
                    $"cookie-names=[{string.Join(", ", keycloakCookies.Keys)}]." +
                    $"{Environment.NewLine}{diagnostics?.Invoke()}");
            }

            currentUri = ResolveLocation(currentUri, response.Headers.Location);
            response.Dispose();
        }

        throw new InvalidOperationException(
            "Keycloak authorization exceeded the expected redirect limit.");
    }

    private static async Task<HttpResponseMessage> FollowRedirectsToApplicationAsync(
        HttpClient browser,
        Uri initialUri,
        Uri applicationBaseUri,
        IDictionary<string, string> keycloakCookies,
        Func<string>? diagnostics)
    {
        var currentUri = initialUri;
        for (var redirectCount = 0; redirectCount < 8; redirectCount++)
        {
            if (currentUri.Scheme == applicationBaseUri.Scheme &&
                currentUri.Authority == applicationBaseUri.Authority)
            {
                return await browser.GetAsync(currentUri);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            AddKeycloakCookies(request, keycloakCookies);
            using var response = await browser.SendAsync(request);
            CaptureKeycloakCookies(response, keycloakCookies);
            if (response.StatusCode != HttpStatusCode.Redirect ||
                response.Headers.Location is null)
            {
                Assert.Fail(
                    $"Keycloak login did not return to the application; " +
                    $"status={(int)response.StatusCode}; location={response.Headers.Location}. " +
                    $"cookie-names=[{string.Join(", ", keycloakCookies.Keys)}]." +
                    $"{Environment.NewLine}{diagnostics?.Invoke()}");
            }

            currentUri = ResolveLocation(currentUri, response.Headers.Location);
        }

        throw new InvalidOperationException(
            "Keycloak login exceeded the expected redirect limit.");
    }

    private static void CaptureKeycloakCookies(
        HttpResponseMessage response,
        IDictionary<string, string> keycloakCookies)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            return;
        }

        foreach (var header in headers)
        {
            var nameValue = header.Split(';', 2)[0];
            var separator = nameValue.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            keycloakCookies[nameValue[..separator]] = nameValue[(separator + 1)..];
        }
    }

    private static void AddKeycloakCookies(
        HttpRequestMessage request,
        IDictionary<string, string> keycloakCookies)
    {
        if (keycloakCookies.Count == 0)
        {
            return;
        }

        // Keycloak deliberately marks its session cookies Secure for a numeric
        // loopback host. HttpClient correctly refuses to persist those over HTTP.
        // The real local URL is localhost; this explicit header lets the protocol
        // test use 127.0.0.1 without weakening Keycloak or application cookies.
        request.Headers.Add(
            "Cookie",
            string.Join("; ", keycloakCookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
    }

    private static Uri ResolveLocation(Uri currentUri, Uri location)
    {
        return location.IsAbsoluteUri ? location : new Uri(currentUri, location);
    }

    public static string? QueryValue(Uri uri, string name)
    {
        var query = uri.Query.TrimStart('?').Split(
            '&',
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var item in query)
        {
            var pair = item.Split('=', 2);
            if (Uri.UnescapeDataString(pair[0]) == name)
            {
                return pair.Length == 2
                    ? Uri.UnescapeDataString(pair[1].Replace('+', ' '))
                    : "";
            }
        }

        return null;
    }

    private static Uri FormAction(string html, string? formId)
    {
        foreach (Match form in FormRegex().Matches(html))
        {
            var tag = form.Value;
            if (formId is not null &&
                !tag.Contains($"id=\"{formId}\"", StringComparison.Ordinal))
            {
                continue;
            }

            var action = ActionRegex().Match(tag);
            if (action.Success)
            {
                return new Uri(WebUtility.HtmlDecode(action.Groups[1].Value));
            }
        }

        throw new InvalidOperationException("Could not find the expected Keycloak form action.");
    }

    private static Dictionary<string, string> HiddenFields(string html)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match input in HiddenInputRegex().Matches(html))
        {
            var name = NameRegex().Match(input.Value);
            var value = ValueRegex().Match(input.Value);
            if (name.Success)
            {
                result[WebUtility.HtmlDecode(name.Groups[1].Value)] =
                    value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : "";
            }
        }

        return result;
    }

    private static string VisibleText(string html)
    {
        var withoutTags = Regex.Replace(html, "<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var normalized = Regex.Replace(decoded, "\\s+", " ").Trim();
        return normalized[..Math.Min(normalized.Length, 2000)];
    }

    [GeneratedRegex("<form\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FormRegex();

    [GeneratedRegex("\\baction=\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ActionRegex();

    [GeneratedRegex("<input\\b(?=[^>]*\\btype=\"hidden\")[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HiddenInputRegex();

    [GeneratedRegex("\\bname=\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();

    [GeneratedRegex("\\bvalue=\"([^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ValueRegex();
}
