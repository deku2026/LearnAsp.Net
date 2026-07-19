if (args.Length != 1 ||
    !Uri.TryCreate(args[0], UriKind.Absolute, out var endpoint) ||
    endpoint.Scheme is not ("http" or "https"))
{
    Console.Error.WriteLine("Usage: Campus.HealthProbe <http-or-https-url>");
    return 2;
}

using var client = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(3),
};

try
{
    using var response = await client.GetAsync(endpoint);
    return response.IsSuccessStatusCode ? 0 : 1;
}
catch (HttpRequestException)
{
    return 1;
}
catch (TaskCanceledException)
{
    return 1;
}
