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

        Id = Guid.NewGuid();
        Code = code.Trim();
        Title = title.Trim();
        Credits = credits;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = "";
    public string Title { get; private set; } = "";
    public int Credits { get; private set; }

    public void Rename(string title) => Title = title.Trim();
}
