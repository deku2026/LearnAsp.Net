namespace Part11_1_PerformanceAdvanced.Tests;

public class CourseCodeParserTests
{
    [Theory]
    [InlineData("CS-1010-A-2026F", "CS", 1010, "A", "2026F")]
    [InlineData("PHYS-2200-B-2027S", "PHYS", 2200, "B", "2027S")]
    public void BaselineAndSpanAgree(string code, string subject, int number, string section, string term)
    {
        var b = CourseCodeParser.ParseBaseline(code);
        var s = CourseCodeParser.ParseSpan(code);
        Assert.NotNull(b);
        Assert.NotNull(s);
        Assert.Equal(subject, b!.Subject);
        Assert.Equal(number, b.Number);
        Assert.Equal(section, b.Section);
        Assert.Equal(term, b.Term);
        Assert.Equal(b.Subject, s!.Subject);
        Assert.Equal(b.Number, s.Number);
        Assert.Equal(b.Section, s.Section);
        Assert.Equal(b.Term, s.Term);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad")]
    [InlineData("CS-ABC-A-2026F")]
    [InlineData("CS-1010-A")]
    public void InvalidCodesReturnNullForBoth(string code)
    {
        Assert.Null(CourseCodeParser.ParseBaseline(code));
        Assert.Null(CourseCodeParser.ParseSpan(code));
    }
}
