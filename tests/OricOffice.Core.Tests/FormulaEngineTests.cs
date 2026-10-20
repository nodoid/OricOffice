using OricOffice.Core.Calc;

namespace OricOffice.Core.Tests;

public class FormulaEngineTests
{
    private static string Eval(string formula, params (int Row, int Col, string Value)[] cells)
    {
        string Resolve(int row, int col)
        {
            foreach (var (r, c, v) in cells)
                if (r == row && c == col)
                    return v;
            return string.Empty;
        }
        return FormulaEngine.Evaluate(formula, Resolve);
    }

    // ------------------------------------------------------------ literals

    [Theory]
    [InlineData("=1+2", "3")]
    [InlineData("=2*3+4", "10")]
    [InlineData("=(2+3)*4", "20")]
    [InlineData("=2^3", "8")]
    [InlineData("=10/4", "2.5")]
    [InlineData("=7-2-1", "4")]
    [InlineData("=50%", "0.5")]
    [InlineData("=10-20%", "9.8")]
    [InlineData("=1.5*2", "3")]
    [InlineData("=1e2+1", "101")]
    [InlineData("=3>2", "1")]
    [InlineData("=2>3", "0")]
    [InlineData("=2<>2", "0")]
    [InlineData("=2<=2", "1")]
    [InlineData("=\"abc\"=\"ABC\"", "1")]
    [InlineData("=\"10\">\"9\"", "1")]
    [InlineData("=IF(1>0,\"yes\",\"no\")", "yes")]
    [InlineData("=IF(1<0,\"yes\",\"no\")", "no")]
    public void Evaluate_Expressions(string formula, string expected)
        => Assert.Equal(expected, Eval(formula));

    [Fact]
    public void IsFormula_DetectsLeadingEquals()
    {
        Assert.True(FormulaEngine.IsFormula("=A1+1"));
        Assert.False(FormulaEngine.IsFormula("A1+1"));
        Assert.False(FormulaEngine.IsFormula(""));
        Assert.False(FormulaEngine.IsFormula(null));
    }

    // ------------------------------------------------------------ references

    [Fact]
    public void Evaluate_ResolvesCellReferences()
        => Assert.Equal("30", Eval("=A1+B1", (0, 0, "10"), (0, 1, "20")));

    [Fact]
    public void Evaluate_ChainedReferences()
        => Assert.Equal("4", Eval("=A1*2", (0, 0, "=1+1")));

    [Fact]
    public void Evaluate_TextCellsConcatenate()
        => Assert.Equal("Hello, world", Eval("=A1&\", world\"", (0, 0, "Hello")));

    [Fact]
    public void Evaluate_EmptyCellsAreZeroInArithmetic()
        => Assert.Equal("5", Eval("=A1+5", (9, 9, "")));

    // ------------------------------------------------------------ ranges + functions

    [Theory]
    [InlineData("=SUM(A1:A3)", "6")]
    [InlineData("=AVERAGE(A1:A3)", "2")]
    [InlineData("=MIN(A1:A3)", "1")]
    [InlineData("=MAX(A1:A3)", "3")]
    [InlineData("=COUNT(A1:A3)", "3")]
    [InlineData("=SUM(A1:B1)", "11")]
    [InlineData("=ABS(-5)", "5")]
    [InlineData("=ROUND(3.14159,2)", "3.14")]
    [InlineData("=CONCAT(\"ab\",\"cd\")", "abcd")]
    [InlineData("=LEN(\"hello\")", "5")]
    [InlineData("=UPPER(\"ab\")", "AB")]
    [InlineData("=LOWER(\"AB\")", "ab")]
    [InlineData("=AND(1,1)", "1")]
    [InlineData("=AND(1,0)", "0")]
    [InlineData("=OR(0,1)", "1")]
    [InlineData("=NOT(0)", "1")]
    [InlineData("=SUM(A1:A3)/COUNT(A1:A3)", "2")]
    public void Evaluate_Functions(string formula, string expected)
        => Assert.Equal(expected, Eval(formula,
            (0, 0, "1"), (1, 0, "2"), (2, 0, "3"),   // A1..A3
            (0, 1, "10"), (1, 1, "20")));            // B1..B2

    [Fact]
    public void Evaluate_SumSkipsTextAndBlankCells()
        => Assert.Equal("3", Eval("=SUM(A1:A4)", (0, 0, "1"), (1, 0, "n/a"), (2, 0, "2"), (3, 0, "")));

    [Fact]
    public void Evaluate_NestedFunctions()
        => Assert.Equal("23", Eval("=SUM(A1:A2,MAX(B1:B2))",
            (0, 0, "1"), (1, 0, "2"), (0, 1, "10"), (1, 1, "20")));

    // ------------------------------------------------------------ errors

    [Theory]
    [InlineData("=1/0", "#DIV/0!")]
    [InlineData("=AVERAGE(D1:D2)", "#DIV/0!")]
    [InlineData("=FOO(1)", "#NAME?")]
    [InlineData("=1+", "#ERROR?")]
    [InlineData("=SUM(A1", "#ERROR?")]
    [InlineData("=\"", "#ERROR?")]
    [InlineData("=\"abc\"+1", "#VALUE!")]
    [InlineData("=A1+", "#ERROR?")]
    public void Evaluate_ReportsErrors(string formula, string expected)
        => Assert.Equal(expected, Eval(formula));

    [Fact]
    public void Evaluate_DetectsCircularReferences()
    {
        var result = Eval("=B1", (0, 0, "=B1"), (0, 1, "=A1"));
        Assert.Equal(FormulaEngine.ErrCirc, result);
    }

    [Fact]
    public void Evaluate_PropagatesErrorsThroughReferences()
        => Assert.Equal("#DIV/0!", Eval("=A1*2", (0, 0, "=1/0")));

    [Fact]
    public void Evaluate_FormulaOnTextCell_ProducesValueError()
        => Assert.Equal("#VALUE!", Eval("=A1+1", (0, 0, "not a number")));
}
