using System.Globalization;

namespace OricOffice.Core.Calc;

// ---------------------------------------------------------------------------
// Public entry points
// ---------------------------------------------------------------------------

/// <summary>
/// A small spreadsheet formula engine: arithmetic, comparisons, cell references (A1),
/// ranges (A1:B3), functions (SUM, AVERAGE, MIN, MAX, COUNT, ABS, ROUND, IF, AND, OR,
/// NOT, CONCAT, LEN, UPPER, LOWER), string concatenation (&amp;) and percent (%).
///
/// Errors are reported Excel-style: #DIV/0!, #NAME?, #VALUE!, #REF!, #CIRC!, #ERROR?.
/// </summary>
public static class FormulaEngine
{
    public const string ErrDiv0 = "#DIV/0!";
    public const string ErrName = "#NAME?";
    public const string ErrValue = "#VALUE!";
    public const string ErrRef = "#REF!";
    public const string ErrCirc = "#CIRC!";
    public const string ErrSyntax = "#ERROR?";

    public static bool IsFormula(string? raw)
        => !string.IsNullOrEmpty(raw) && raw[0] == '=';

    /// <summary>Evaluates a formula with an optional resolver for referenced cells' raw input.</summary>
    public static string Evaluate(string formula, Func<int, int, string?>? cellResolver = null)
    {
        var calculator = new SheetCalculator((row, col) => cellResolver?.Invoke(row, col) ?? string.Empty);
        return calculator.EvaluateFormula(formula);
    }

    /// <summary>Formulas come from user input; a leading '=' marks one.</summary>
    public static string StripEquals(string raw) => IsFormula(raw) ? raw[1..] : raw;
}

// ---------------------------------------------------------------------------
// Evaluation values
// ---------------------------------------------------------------------------

internal sealed class EvalError : Exception
{
    public string Code { get; }
    public EvalError(string code) => Code = code;
}

internal enum EvKind { Number, Text, Error, Range }

internal readonly struct EvalValue
{
    public EvKind Kind { get; }
    public double Number { get; }
    public string Text { get; }
    public string Error { get; }
    public IReadOnlyList<EvalValue>? Items { get; }

    private EvalValue(EvKind kind, double number, string text, string error, IReadOnlyList<EvalValue>? items)
    {
        Kind = kind;
        Number = number;
        Text = text;
        Error = error;
        Items = items;
    }

    public static EvalValue Num(double d) => new(EvKind.Number, d, string.Empty, string.Empty, null);
    public static EvalValue Txt(string s) => new(EvKind.Text, 0, s ?? string.Empty, string.Empty, null);
    public static EvalValue Err(string code) => new(EvKind.Error, 0, string.Empty, code, null);
    public static EvalValue Range(IReadOnlyList<EvalValue> items) => new(EvKind.Range, 0, string.Empty, string.Empty, items);

    public bool IsError => Kind == EvKind.Error;

    public void ThrowIfError()
    {
        if (Kind == EvKind.Error) throw new EvalError(Error);
    }

    /// <summary>Coerces to a number, Excel-style: numeric text converts, other text is #VALUE!.</summary>
    public double AsNumber()
    {
        switch (Kind)
        {
            case EvKind.Number:
                return Number;
            case EvKind.Error:
                throw new EvalError(Error);
            case EvKind.Text:
                // Blank cells read as 0 in arithmetic (Excel behaviour).
                if (Text.Length == 0) return 0;
                if (double.TryParse(Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    return d;
                throw new EvalError(FormulaEngine.ErrValue);
            default:
                throw new EvalError(FormulaEngine.ErrValue);
        }
    }

    public string Display()
    {
        switch (Kind)
        {
            case EvKind.Number:
                return FormatNumber(Number);
            case EvKind.Text:
                return Text;
            case EvKind.Error:
                return Error;
            default:
                if (Items is { Count: > 0 }) return Items[0].Display();
                return string.Empty;
        }
    }

    public static string FormatNumber(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return FormulaEngine.ErrDiv0;
        if (d == Math.Truncate(d) && Math.Abs(d) < 1e15)
            return d.ToString("0", CultureInfo.InvariantCulture);
        return d.ToString("0.##########", CultureInfo.InvariantCulture);
    }
}

// ---------------------------------------------------------------------------
// Expression context + AST
// ---------------------------------------------------------------------------

internal interface IExprContext
{
    EvalValue Cell(int row, int column);
}

internal abstract class Node
{
    public abstract EvalValue Eval(IExprContext ctx);
}

internal sealed class NumNode : Node
{
    private readonly double _value;
    public NumNode(double value) => _value = value;
    public override EvalValue Eval(IExprContext ctx) => EvalValue.Num(_value);
}

internal sealed class StrNode : Node
{
    private readonly string _value;
    public StrNode(string value) => _value = value;
    public override EvalValue Eval(IExprContext ctx) => EvalValue.Txt(_value);
}

internal sealed class RefNode : Node
{
    private readonly int _row;
    private readonly int _column;
    public RefNode(int row, int column) { _row = row; _column = column; }
    public override EvalValue Eval(IExprContext ctx) => ctx.Cell(_row, _column);
}

internal sealed class RangeNode : Node
{
    private readonly int _row1, _col1, _row2, _col2;
    public RangeNode(int row1, int col1, int row2, int col2)
    {
        _row1 = Math.Min(row1, row2);
        _row2 = Math.Max(row1, row2);
        _col1 = Math.Min(col1, col2);
        _col2 = Math.Max(col1, col2);
    }

    public override EvalValue Eval(IExprContext ctx)
    {
        // Guard against absurd ranges (e.g. A1:ZZ9999) blowing memory.
        const int maxCells = 100_000;
        long count = (long)(_row2 - _row1 + 1) * (_col2 - _col1 + 1);
        if (count > maxCells) throw new EvalError(FormulaEngine.ErrRef);

        var items = new List<EvalValue>((int)count);
        for (int r = _row1; r <= _row2; r++)
            for (int c = _col1; c <= _col2; c++)
                items.Add(ctx.Cell(r, c));
        return EvalValue.Range(items);
    }
}

internal sealed class UnaryNode : Node
{
    private readonly char _op;
    private readonly Node _inner;
    public UnaryNode(char op, Node inner) { _op = op; _inner = inner; }

    public override EvalValue Eval(IExprContext ctx)
    {
        var v = _inner.Eval(ctx);
        v.ThrowIfError();
        return _op switch
        {
            '-' => EvalValue.Num(-v.AsNumber()),
            '+' => EvalValue.Num(v.AsNumber()),
            '%' => EvalValue.Num(v.AsNumber() / 100.0),
            _ => v,
        };
    }
}

internal sealed class BinaryNode : Node
{
    private readonly Node _left;
    private readonly string _op;
    private readonly Node _right;
    public BinaryNode(Node left, string op, Node right) { _left = left; _op = op; _right = right; }

    public override EvalValue Eval(IExprContext ctx)
    {
        var a = _left.Eval(ctx);
        a.ThrowIfError();
        var b = _right.Eval(ctx);
        b.ThrowIfError();

        switch (_op)
        {
            case "&":
                return EvalValue.Txt(a.Display() + b.Display());

            case "=":
            case "<>":
            case "<":
            case ">":
            case "<=":
            case ">=":
                var cmp = Compare(a, b);
                var result = _op switch
                {
                    "=" => cmp == 0,
                    "<>" => cmp != 0,
                    "<" => cmp < 0,
                    ">" => cmp > 0,
                    "<=" => cmp <= 0,
                    _ => cmp >= 0,
                };
                return EvalValue.Num(result ? 1 : 0);
        }

        var x = a.AsNumber();
        var y = b.AsNumber();
        return _op switch
        {
            "+" => EvalValue.Num(x + y),
            "-" => EvalValue.Num(x - y),
            "*" => EvalValue.Num(x * y),
            "/" => y == 0 ? throw new EvalError(FormulaEngine.ErrDiv0) : EvalValue.Num(x / y),
            "^" => EvalValue.Num(Math.Pow(x, y)),
            _ => throw new EvalError(FormulaEngine.ErrSyntax),
        };
    }

    private static int Compare(EvalValue a, EvalValue b)
    {
        // Numeric when both sides are numbers (or numeric text); otherwise case-insensitive text.
        var aNumeric = IsNumeric(a, out var xa);
        var bNumeric = IsNumeric(b, out var xb);
        if (aNumeric && bNumeric) return xa.CompareTo(xb);
        return string.Compare(a.Display(), b.Display(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumeric(EvalValue v, out double value)
    {
        if (v.Kind == EvKind.Number) { value = v.Number; return true; }
        if (v.Kind == EvKind.Text)
            return double.TryParse(v.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        value = 0;
        return false;
    }
}

internal sealed class FuncNode : Node
{
    private readonly string _name;
    private readonly List<Node> _args;
    public FuncNode(string name, List<Node> args) { _name = name; _args = args; }

    public override EvalValue Eval(IExprContext ctx)
    {
        var name = _name.ToUpperInvariant();

        // IF is lazy: only the taken branch is evaluated (so errors there don't leak).
        if (name == "IF")
        {
            if (_args.Count is < 2 or > 3) throw new EvalError(FormulaEngine.ErrValue);
            var cond = _args[0].Eval(ctx);
            cond.ThrowIfError();
            var truthy = cond.Kind switch
            {
                EvKind.Number => cond.Number != 0,
                EvKind.Text => cond.Text.Length > 0,
                _ => false,
            };
            if (truthy) return EvalValueOf(_args[1], ctx);
            return _args.Count == 3 ? EvalValueOf(_args[2], ctx) : EvalValue.Txt("FALSE");
        }

        var values = new List<EvalValue>(_args.Count);
        foreach (var arg in _args)
        {
            var v = arg.Eval(ctx);
            v.ThrowIfError();
            values.Add(v);
        }
        return Invoke(name, values);
    }

    private static EvalValue EvalValueOf(Node node, IExprContext ctx)
    {
        var v = node.Eval(ctx);
        v.ThrowIfError();
        return v;
    }

    internal static EvalValue Invoke(string name, List<EvalValue> args)
    {
        switch (name)
        {
            case "SUM":
            case "AVERAGE":
            case "MIN":
            case "MAX":
            case "COUNT":
            {
                var numbers = FlattenNumbers(args);
                switch (name)
                {
                    case "SUM":
                        return EvalValue.Num(numbers.Sum());
                    case "AVERAGE":
                        if (numbers.Count == 0) throw new EvalError(FormulaEngine.ErrDiv0);
                        return EvalValue.Num(numbers.Average());
                    case "MIN":
                        return EvalValue.Num(numbers.Count == 0 ? 0 : numbers.Min());
                    case "MAX":
                        return EvalValue.Num(numbers.Count == 0 ? 0 : numbers.Max());
                    default:
                        return EvalValue.Num(numbers.Count);
                }
            }

            case "ABS":
                return EvalValue.Num(Math.Abs(Single(args, name)));
            case "ROUND":
            {
                var v = Single(args, name);
                var digits = args.Count > 1 ? (int)args[1].AsNumber() : 0;
                digits = Math.Clamp(digits, -15, 15);
                var factor = Math.Pow(10, digits);
                return EvalValue.Num(Math.Round(v * factor, MidpointRounding.AwayFromZero) / factor);
            }

            case "AND":
                return EvalValue.Num(Flatten(args).All(IsTruthy) ? 1 : 0);
            case "OR":
                return EvalValue.Num(Flatten(args).Any(IsTruthy) ? 1 : 0);
            case "NOT":
                if (args.Count < 1) throw new EvalError(FormulaEngine.ErrValue);
                return EvalValue.Num(IsTruthy(args[0]) ? 0 : 1);

            case "CONCAT":
            case "CONCATENATE":
                return EvalValue.Txt(string.Concat(Flatten(args).Select(v => v.Display())));

            case "LEN":
                return EvalValue.Num(Text(args).Length);
            case "UPPER":
                return EvalValue.Txt(Text(args).ToUpperInvariant());
            case "LOWER":
                return EvalValue.Txt(Text(args).ToLowerInvariant());

            default:
                throw new EvalError(FormulaEngine.ErrName);
        }
    }

    private static double Single(List<EvalValue> args, string name)
    {
        if (args.Count < 1) throw new EvalError(FormulaEngine.ErrValue);
        return args[0].AsNumber();
    }

    private static string Text(List<EvalValue> args)
        => args.Count < 1 ? string.Empty : args[0].Display();

    private static bool IsTruthy(EvalValue v)
        => v.Kind switch
        {
            EvKind.Number => v.Number != 0,
            EvKind.Text => v.Text.Length > 0,
            _ => false,
        };

    private static List<EvalValue> Flatten(List<EvalValue> args)
    {
        var result = new List<EvalValue>();
        foreach (var a in args)
        {
            if (a.Kind == EvKind.Range && a.Items is not null) result.AddRange(a.Items);
            else result.Add(a);
        }
        return result;
    }

    private static List<double> FlattenNumbers(List<EvalValue> args)
    {
        var result = new List<double>();
        foreach (var v in Flatten(args))
        {
            v.ThrowIfError();
            if (v.Kind == EvKind.Number) result.Add(v.Number);
            else if (v.Kind == EvKind.Text &&
                     double.TryParse(v.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                result.Add(d);
            // Blank cells and non-numeric text are skipped, like Excel's SUM.
        }
        return result;
    }
}

// ---------------------------------------------------------------------------
// Lexer
// ---------------------------------------------------------------------------

internal enum Tok
{
    End, Number, StringLit, Ref, Ident,
    Plus, Minus, Star, Slash, Caret, Amp, Percent,
    Eq, Ne, Lt, Gt, Le, Ge,
    LParen, RParen, Comma, Colon,
}

internal readonly struct Token
{
    public Tok Kind { get; init; }
    public string Text { get; init; }
    public double Number { get; init; }
    public int Row { get; init; }
    public int Column { get; init; }
}

internal sealed class FormulaException : Exception
{
    public FormulaException(string message) : base(message) { }
}

internal static class Lexer
{
    public static List<Token> Tokenize(string input)
    {
        var tokens = new List<Token>();
        int i = 0;
        int n = input.Length;

        while (i < n)
        {
            var ch = input[i];
            if (char.IsWhiteSpace(ch)) { i++; continue; }

            // String literal
            if (ch == '"')
            {
                i++;
                var sb = new System.Text.StringBuilder();
                bool closed = false;
                while (i < n)
                {
                    if (input[i] == '"')
                    {
                        if (i + 1 < n && input[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                        i++; closed = true; break;
                    }
                    sb.Append(input[i++]);
                }
                if (!closed) throw new FormulaException("Unterminated string literal.");
                tokens.Add(new Token { Kind = Tok.StringLit, Text = sb.ToString() });
                continue;
            }

            // Number (leading digit or leading dot)
            if (char.IsDigit(ch) || (ch == '.' && i + 1 < n && char.IsDigit(input[i + 1])))
            {
                int start = i;
                while (i < n && char.IsDigit(input[i])) i++;
                if (i < n && input[i] == '.')
                {
                    i++;
                    while (i < n && char.IsDigit(input[i])) i++;
                }
                if (i < n && (input[i] == 'e' || input[i] == 'E'))
                {
                    int save = i;
                    i++;
                    if (i < n && (input[i] == '+' || input[i] == '-')) i++;
                    if (i < n && char.IsDigit(input[i]))
                    {
                        while (i < n && char.IsDigit(input[i])) i++;
                    }
                    else
                    {
                        i = save;
                    }
                }
                var text = input[start..i];
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    throw new FormulaException($"Bad number \"{text}\".");
                tokens.Add(new Token { Kind = Tok.Number, Text = text, Number = value });
                continue;
            }

            // Reference or identifier
            if (ch == '$' || char.IsLetter(ch) || ch == '_')
            {
                if (TryReference(input, i, out var token, out var next))
                {
                    tokens.Add(token);
                    i = next;
                    continue;
                }

                // Identifier / function name: [A-Za-z_][A-Za-z0-9_.]*
                if (ch != '$' && (char.IsLetter(ch) || ch == '_'))
                {
                    int start = i;
                    i++;
                    while (i < n && (char.IsLetterOrDigit(input[i]) || input[i] == '_' || input[i] == '.')) i++;
                    tokens.Add(new Token { Kind = Tok.Ident, Text = input[start..i] });
                    continue;
                }

                throw new FormulaException($"Unexpected character '{ch}'.");
            }

            // Operators
            switch (ch)
            {
                case '+': tokens.Add(new Token { Kind = Tok.Plus, Text = "+" }); i++; continue;
                case '-': tokens.Add(new Token { Kind = Tok.Minus, Text = "-" }); i++; continue;
                case '*': tokens.Add(new Token { Kind = Tok.Star, Text = "*" }); i++; continue;
                case '/': tokens.Add(new Token { Kind = Tok.Slash, Text = "/" }); i++; continue;
                case '^': tokens.Add(new Token { Kind = Tok.Caret, Text = "^" }); i++; continue;
                case '&': tokens.Add(new Token { Kind = Tok.Amp, Text = "&" }); i++; continue;
                case '%': tokens.Add(new Token { Kind = Tok.Percent, Text = "%" }); i++; continue;
                case '(': tokens.Add(new Token { Kind = Tok.LParen, Text = "(" }); i++; continue;
                case ')': tokens.Add(new Token { Kind = Tok.RParen, Text = ")" }); i++; continue;
                case ',': tokens.Add(new Token { Kind = Tok.Comma, Text = "," }); i++; continue;
                case ':': tokens.Add(new Token { Kind = Tok.Colon, Text = ":" }); i++; continue;
                case '=': tokens.Add(new Token { Kind = Tok.Eq, Text = "=" }); i++; continue;
                case '<':
                    if (i + 1 < n && input[i + 1] == '=') { tokens.Add(new Token { Kind = Tok.Le, Text = "<=" }); i += 2; }
                    else if (i + 1 < n && input[i + 1] == '>') { tokens.Add(new Token { Kind = Tok.Ne, Text = "<>" }); i += 2; }
                    else { tokens.Add(new Token { Kind = Tok.Lt, Text = "<" }); i++; }
                    continue;
                case '>':
                    if (i + 1 < n && input[i + 1] == '=') { tokens.Add(new Token { Kind = Tok.Ge, Text = ">=" }); i += 2; }
                    else { tokens.Add(new Token { Kind = Tok.Gt, Text = ">" }); i++; }
                    continue;
                case '!':
                    if (i + 1 < n && input[i + 1] == '=') { tokens.Add(new Token { Kind = Tok.Ne, Text = "!=" }); i += 2; continue; }
                    throw new FormulaException($"Unexpected character '!'.");
                default:
                    throw new FormulaException($"Unexpected character '{ch}'.");
            }
        }

        tokens.Add(new Token { Kind = Tok.End, Text = string.Empty });
        return tokens;
    }

    /// <summary>
    /// Recognises $?$?LETTERS$?DIGIT (e.g. A1, $B$12, ABC99) — but only when the run is not
    /// followed by '(' / letter / digit / '_', so function names like LOG10( stay idents.
    /// </summary>
    private static bool TryReference(string input, int start, out Token token, out int next)
    {
        token = default;
        next = start;
        int i = start;
        int n = input.Length;

        if (i < n && input[i] == '$') i++;

        int letterStart = i;
        while (i < n && char.IsLetter(input[i])) i++;
        int letters = i - letterStart;
        if (letters is < 1 or > 3) return false;

        if (i < n && input[i] == '$')
        {
            i++;
            int digitStart = i;
            while (i < n && char.IsDigit(input[i])) i++;
            if (i == digitStart) return false;
            if (!FinishCheck(input, i, n)) return false;
            if (!BuildAddress(input, letterStart, letters, digitStart, i, out token)) return false;
            next = i;
            return true;
        }

        int digitsStart = i;
        while (i < n && char.IsDigit(input[i])) i++;
        if (i == digitsStart) return false;
        if (!FinishCheck(input, i, n)) return false;
        if (!BuildAddress(input, letterStart, letters, digitsStart, i, out token)) return false;
        next = i;
        return true;
    }

    private static bool FinishCheck(string input, int i, int n)
        => i >= n || !(char.IsLetterOrDigit(input[i]) || input[i] == '_' || input[i] == '$' || input[i] == '(');

    private static bool BuildAddress(string input, int letterStart, int letters, int digitStart, int digitEnd, out Token token)
    {
        token = default;
        var column = 0;
        for (int k = letterStart; k < letterStart + letters; k++)
        {
            var c = char.ToUpperInvariant(input[k]);
            column = column * 26 + (c - 'A' + 1);
        }
        column -= 1;

        if (!int.TryParse(input[digitStart..digitEnd], NumberStyles.None, CultureInfo.InvariantCulture, out var rowNumber)
            || rowNumber < 1 || rowNumber > 1_000_000)
            return false;
        if (column < 0 || column > 16_383) return false;

        token = new Token
        {
            Kind = Tok.Ref,
            Text = input[letterStart..digitEnd],
            Row = rowNumber - 1,
            Column = column,
        };
        return true;
    }
}

// ---------------------------------------------------------------------------
// Parser (recursive descent)
// ---------------------------------------------------------------------------

internal sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;

    private Parser(List<Token> tokens) => _tokens = tokens;

    private Token Current => _tokens[_pos];

    public static Node Parse(string formula)
    {
        var body = FormulaEngine.IsFormula(formula) ? formula[1..] : formula;
        if (string.IsNullOrWhiteSpace(body)) throw new FormulaException("Empty formula.");
        var parser = new Parser(Lexer.Tokenize(body));
        var node = parser.ParseComparison();
        if (parser.Current.Kind != Tok.End)
            throw new FormulaException($"Unexpected token \"{parser.Current.Text}\".");
        return node;
    }

    private Node ParseComparison()
    {
        var left = ParseConcat();
        while (Current.Kind is Tok.Eq or Tok.Ne or Tok.Lt or Tok.Gt or Tok.Le or Tok.Ge)
        {
            var op = Current.Text;
            _pos++;
            var right = ParseConcat();
            left = new BinaryNode(left, op, right);
        }
        return left;
    }

    private Node ParseConcat()
    {
        var left = ParseAdditive();
        while (Current.Kind == Tok.Amp)
        {
            _pos++;
            var right = ParseAdditive();
            left = new BinaryNode(left, "&", right);
        }
        return left;
    }

    private Node ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (Current.Kind is Tok.Plus or Tok.Minus)
        {
            var op = Current.Text;
            _pos++;
            var right = ParseMultiplicative();
            left = new BinaryNode(left, op, right);
        }
        return left;
    }

    private Node ParseMultiplicative()
    {
        var left = ParseUnary();
        while (Current.Kind is Tok.Star or Tok.Slash)
        {
            var op = Current.Text;
            _pos++;
            var right = ParseUnary();
            left = new BinaryNode(left, op, right);
        }
        return left;
    }

    private Node ParseUnary()
    {
        if (Current.Kind is Tok.Minus or Tok.Plus)
        {
            var op = Current.Kind == Tok.Minus ? '-' : '+';
            _pos++;
            return new UnaryNode(op, ParsePower());
        }
        return ParsePower();
    }

    private Node ParsePower()
    {
        var left = ParsePostfix();
        if (Current.Kind == Tok.Caret)
        {
            _pos++;
            var right = ParseUnary(); // right-associative: 2^3^2 = 2^(3^2)
            return new BinaryNode(left, "^", right);
        }
        return left;
    }

    private Node ParsePostfix()
    {
        var node = ParsePrimary();
        while (Current.Kind == Tok.Percent)
        {
            _pos++;
            node = new UnaryNode('%', node);
        }
        return node;
    }

    private Node ParsePrimary()
    {
        switch (Current.Kind)
        {
            case Tok.Number:
            {
                var value = Current.Number;
                _pos++;
                return new NumNode(value);
            }

            case Tok.StringLit:
            {
                var text = Current.Text;
                _pos++;
                return new StrNode(text);
            }

            case Tok.Ref:
            {
                var row = Current.Row;
                var column = Current.Column;
                _pos++;
                if (Current.Kind == Tok.Colon)
                {
                    _pos++;
                    if (Current.Kind != Tok.Ref)
                        throw new FormulaException("Expected cell address after ':'.");
                    var row2 = Current.Row;
                    var column2 = Current.Column;
                    _pos++;
                    return new RangeNode(row, column, row2, column2);
                }
                return new RefNode(row, column);
            }

            case Tok.Ident:
            {
                var name = Current.Text;
                _pos++;
                if (Current.Kind != Tok.LParen)
                    throw new FormulaException($"Unknown name \"{name}\".");
                _pos++;
                var args = new List<Node>();
                if (Current.Kind != Tok.RParen)
                {
                    while (true)
                    {
                        args.Add(ParseComparison());
                        if (Current.Kind == Tok.Comma) { _pos++; continue; }
                        break;
                    }
                }
                if (Current.Kind != Tok.RParen)
                    throw new FormulaException("Expected ')'.");
                _pos++;
                return new FuncNode(name, args);
            }

            case Tok.LParen:
            {
                _pos++;
                var inner = ParseComparison();
                if (Current.Kind != Tok.RParen)
                    throw new FormulaException("Expected ')'.");
                _pos++;
                return inner;
            }

            default:
                throw new FormulaException($"Unexpected token \"{Current.Text}\".");
        }
    }
}

// ---------------------------------------------------------------------------
// Sheet calculator (memoised, cycle-aware)
// ---------------------------------------------------------------------------

/// <summary>
/// Evaluates every cell of a sheet against its raw inputs, with memoisation and
/// cycle detection (#CIRC!). Create one instance per recalculation pass.
/// </summary>
public sealed class SheetCalculator : IExprContext
{
    private readonly Func<int, int, string> _raw;
    private readonly Dictionary<(int Row, int Column), EvalValue> _memo = new();
    private readonly HashSet<(int Row, int Column)> _stack = new();

    public SheetCalculator(Func<int, int, string> rawResolver)
        => _raw = rawResolver ?? ((_, _) => string.Empty);

    /// <summary>Display value of a cell (works for literals and formulas alike).</summary>
    public string DisplayAt(int row, int column)
    {
        try
        {
            return ValueAt(row, column).Display();
        }
        catch (EvalError e)
        {
            return e.Code;
        }
    }

    internal EvalValue ValueAt(int row, int column)
    {
        var key = (row, column);
        if (_memo.TryGetValue(key, out var cached)) return cached;
        if (_stack.Contains(key)) throw new EvalError(FormulaEngine.ErrCirc);

        _stack.Add(key);
        try
        {
            var raw = _raw(row, column) ?? string.Empty;
            EvalValue value;
            if (raw.Length == 0)
            {
                value = EvalValue.Txt(string.Empty);
            }
            else if (FormulaEngine.IsFormula(raw))
            {
                try
                {
                    var node = Parser.Parse(raw);
                    value = node.Eval(this);
                }
                catch (FormulaException)
                {
                    value = EvalValue.Err(FormulaEngine.ErrSyntax);
                }
            }
            else if (double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                value = EvalValue.Num(d);
            }
            else
            {
                value = EvalValue.Txt(raw);
            }

            _memo[key] = value;
            return value;
        }
        finally
        {
            _stack.Remove(key);
        }
    }

    /// <summary>Evaluates a standalone formula string (referenced cells resolve via the raw resolver).</summary>
    public string EvaluateFormula(string formula)
    {
        try
        {
            var node = Parser.Parse(formula);
            return node.Eval(this).Display();
        }
        catch (FormulaException)
        {
            return FormulaEngine.ErrSyntax;
        }
        catch (EvalError e)
        {
            return e.Code;
        }
    }

    EvalValue IExprContext.Cell(int row, int column) => ValueAt(row, column);
}
