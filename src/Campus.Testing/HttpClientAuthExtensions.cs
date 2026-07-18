namespace Campus.Testing;

public static class HttpClientAuthExtensions
{
    public static HttpClient AsTestUser(
        this HttpClient client,
        string userId = "student-1",
        string role = "Student",
        string collegeId = "college-1")
    {
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Remove("X-Test-College");
        client.DefaultRequestHeaders.Add("X-Test-User", userId);
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-College", collegeId);
        return client;
    }
}
