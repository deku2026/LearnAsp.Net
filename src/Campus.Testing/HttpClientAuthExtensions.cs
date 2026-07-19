namespace Campus.Testing;

public static class HttpClientAuthExtensions
{
    public static HttpClient AsTestUser(
        this HttpClient client,
        string userId = "student-1",
        string role = "Student",
        string collegeId = "college-1",
        string scopes = "campus.read campus.write")
    {
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Remove("X-Test-College");
        client.DefaultRequestHeaders.Remove("X-Test-Scope");
        client.DefaultRequestHeaders.Add("X-Test-User", userId);
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-College", collegeId);
        client.DefaultRequestHeaders.Add("X-Test-Scope", scopes);
        return client;
    }
}
