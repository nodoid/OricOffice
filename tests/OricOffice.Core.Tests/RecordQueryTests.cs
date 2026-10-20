using OricOffice.Core.Base;
using OricOffice.Core.Model;

namespace OricOffice.Core.Tests;

public class RecordQueryTests
{
    private static readonly List<FieldDef> Fields = new()
    {
        new("Name", FieldType.Text),
        new("Age", FieldType.Number),
        new("City", FieldType.Text),
    };

    private static RecordRow Row(long id, string name, string age, string city)
    {
        var row = new RecordRow { Id = id };
        row.Set("Name", name);
        row.Set("Age", age);
        row.Set("City", city);
        return row;
    }

    private static List<RecordRow> Sample() => new()
    {
        Row(1, "Ann Smith", "29", "Leeds"),
        Row(2, "Bob Jones", "17", "York"),
        Row(3, "Cara DANIELS", "64", ""),
        Row(4, "Dan Evans", "42", "Leeds"),
    };

    private static List<RecordRow> Filter(string query)
    {
        var ok = RecordQuery.TryParse(query, Fields, out var compiled, out var error);
        Assert.True(ok, error);
        return RecordQuery.Apply(Sample(), compiled).ToList();
    }

    [Fact]
    public void EmptyQuery_MatchesEverything()
    {
        Assert.Equal(4, Filter("").Count);
        Assert.Equal(4, Filter("   ").Count);
    }

    [Fact]
    public void EqualsFilter()
    {
        var result = Filter("city = \"Leeds\"");
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal("Leeds", r.Get("City")));
    }

    [Fact]
    public void NumericComparison_UsesNumericOrdering()
    {
        var result = Filter("age >= 29");
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Contains_IsCaseInsensitive()
    {
        var result = Filter("name contains \"dani\"");
        Assert.Single(result);
        Assert.Equal("Cara DANIELS", result[0].Get("Name"));
    }

    [Fact]
    public void StartsWithWorks()
    {
        var result = Filter("name startswith \"b\"");
        Assert.Single(result);
        Assert.Equal(2, result[0].Id);
    }

    [Fact]
    public void IsBlankDetectsEmptyValues()
    {
        var result = Filter("city is blank");
        Assert.Single(result);
        Assert.Equal(3, result[0].Id);
    }

    [Fact]
    public void AndCombinesClauses()
    {
        var result = Filter("age > 20 AND city = \"Leeds\"");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void OrCombinesClauses()
    {
        var result = Filter("age < 18 OR age > 60");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void AndBindsTighterThanOr()
    {
        // (age > 50 AND city is blank) OR city = "York"  →  Cara (64, blank) + Bob (York)
        var result = Filter("age > 50 AND city is blank OR city = \"York\"");
        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Id == 2);
        Assert.Contains(result, r => r.Id == 3);
    }

    [Fact]
    public void QuotedValuesWithSpacesAndKeywordInside()
    {
        var result = Filter("name contains \"ANDY\" OR name = \"Ann Smith\"");
        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
    }

    [Fact]
    public void UnknownField_ProducesHelpfulError()
    {
        var ok = RecordQuery.TryParse("colour = \"red\"", Fields, out _, out var error);
        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("colour", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Name", error);
    }

    [Fact]
    public void Garbage_ProducesParseError()
    {
        var ok = RecordQuery.TryParse("just some words", Fields, out _, out var error);
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void SortAscending_UsesNumericOrder()
    {
        var rows = Sample();
        var field = Fields[1]; // Age
        var sorted = RecordQuery.Sort(rows, field, descending: false).Select(r => r.Get("Age")).ToList();
        Assert.Equal(new[] { "17", "29", "42", "64" }, sorted);
    }

    [Fact]
    public void SortDescending_UsesNumericOrder()
    {
        var rows = Sample();
        var field = Fields[1];
        var sorted = RecordQuery.Sort(rows, field, descending: true).Select(r => r.Get("Age")).ToList();
        Assert.Equal(new[] { "64", "42", "29", "17" }, sorted);
    }

    [Fact]
    public void SortByText_IgnoresCase()
    {
        var rows = Sample();
        var field = Fields[0];
        var sorted = RecordQuery.Sort(rows, field, descending: false).Select(r => r.Get("Name")).ToList();
        Assert.Equal("Ann Smith", sorted[0]);
        Assert.Equal("Bob Jones", sorted[1]);
        Assert.Equal("Cara DANIELS", sorted[2]);
        Assert.Equal("Dan Evans", sorted[3]);
    }
}
