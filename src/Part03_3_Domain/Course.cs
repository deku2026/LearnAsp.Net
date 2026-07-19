namespace Part03_3.Domain;

public sealed class Course
{
    private Course()
    {
    }

    public Course(string code, string title, int credits)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("code required", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("title required", nameof(title));
        }

        if (credits is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(credits), "credits must be between 1 and 10");
        }

        Id = Guid.NewGuid();
        Code = code.Trim();
        Title = title.Trim();
        Credits = credits;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = "";
    public string Title { get; private set; } = "";
    public int Credits { get; private set; }

    public void Rename(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("title required", nameof(title));
        }

        Title = title.Trim();
    }
}
