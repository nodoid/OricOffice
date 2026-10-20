using System.Globalization;
using System.Text.RegularExpressions;
using OricOffice.Core.Model;

namespace OricOffice.Core.Base;

public enum QueryOp
{
    Equals,
    NotEquals,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
    Contains,
    StartsWith,
    IsBlank,
    IsNotBlank,
}

public readonly record struct QueryClause(string Field, QueryOp Op, string Value);

/// <summary>A compiled query: OR-groups of AND-clauses (AND binds tighter than OR).</summary>
public sealed class CompiledQuery
{
    public IReadOnlyList<IReadOnlyList<QueryClause>> OrGroups { get; }

    public CompiledQuery(IReadOnlyList<IReadOnlyList<QueryClause>> orGroups)
        => OrGroups = orGroups;

    public bool Matches(RecordRow record)
    {
        foreach (var group in OrGroups)
        {
            var all = true;
            foreach (var clause in group)
            {
                if (!MatchesClause(record, clause)) { all = false; break; }
            }
            if (all) return true;
        }
        return false;
    }

    private static bool MatchesClause(RecordRow record, QueryClause clause)
    {
        var actual = record.Get(clause.Field);

        switch (clause.Op)
        {
            case QueryOp.IsBlank:
                return string.IsNullOrWhiteSpace(actual);
            case QueryOp.IsNotBlank:
                return !string.IsNullOrWhiteSpace(actual);
            case QueryOp.Contains:
                return actual.Contains(clause.Value, StringComparison.OrdinalIgnoreCase);
            case QueryOp.StartsWith:
                return actual.StartsWith(clause.Value, StringComparison.OrdinalIgnoreCase);
        }

        var cmp = Compare(actual, clause.Value);
        return clause.Op switch
        {
            QueryOp.Equals => cmp == 0,
            QueryOp.NotEquals => cmp != 0,
            QueryOp.Greater => cmp > 0,
            QueryOp.GreaterOrEqual => cmp >= 0,
            QueryOp.Less => cmp < 0,
            QueryOp.LessOrEqual => cmp <= 0,
            _ => false,
        };
    }

    internal static int Compare(string left, string right)
    {
        var leftNumeric = double.TryParse(left?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lx);
        var rightNumeric = double.TryParse(right?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rx);
        if (leftNumeric && rightNumeric) return lx.CompareTo(rx);
        return string.Compare(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The Base query language, kept deliberately small (the original Oric Base was sold on
/// having an "easy to learn query language"):
///
///     name contains "smith" AND age &gt;= 21 OR city = "Leeds"
///
/// Operators: = ≠ != &gt; &gt;= &lt; &lt;= contains startswith "is blank" "is not blank".
/// Combine with AND / OR (AND binds tighter). Values may be quoted.
/// </summary>
public static class RecordQuery
{
    private static readonly Regex ClauseRegex = new(
        @"^(?<f>.+?)\s*(?<op><=|>=|<>|!=|=|>|<|\bcontains\b|\bstartswith\b|\bis\s+not\s+blank\b|\bis\s+blank\b)\s*(?<v>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    public static bool TryParse(string? query, IReadOnlyList<FieldDef> fields,
        out CompiledQuery? compiled, out string? error)
    {
        compiled = null;
        error = null;

        if (string.IsNullOrWhiteSpace(query))
        {
            return true; // empty query = no filtering
        }

        var orGroups = new List<IReadOnlyList<QueryClause>>();
        foreach (var orTerm in SplitTopLevel(query, "OR"))
        {
            var clauses = new List<QueryClause>();
            foreach (var andTerm in SplitTopLevel(orTerm, "AND"))
            {
                if (!TryParseClause(andTerm, fields, out var clause, out error))
                    return false;
                clauses.Add(clause);
            }
            if (clauses.Count > 0) orGroups.Add(clauses);
        }

        compiled = new CompiledQuery(orGroups);
        return true;
    }

    private static bool TryParseClause(string text, IReadOnlyList<FieldDef> fields,
        out QueryClause clause, out string? error)
    {
        clause = default;
        error = null;
        text = text.Trim().Trim('(').Trim(')').Trim();

        var match = ClauseRegex.Match(text);
        if (!match.Success)
        {
            error = $"Cannot read \"{text}\". Use for example: name contains \"smith\" AND age >= 21";
            return false;
        }

        var fieldName = match.Groups["f"].Value.Trim();
        var field = fields.FirstOrDefault(f =>
            string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            var known = fields.Count == 0
                ? "(no fields defined yet)"
                : string.Join(", ", fields.Select(f => f.Name));
            error = $"Unknown field \"{fieldName}\". Available fields: {known}.";
            return false;
        }

        var opText = match.Groups["op"].Value.Trim();
        var rawValue = match.Groups["v"].Value.Trim();

        var op = opText.ToLowerInvariant() switch
        {
            "=" => QueryOp.Equals,
            "!=" or "<>" => QueryOp.NotEquals,
            ">" => QueryOp.Greater,
            ">=" => QueryOp.GreaterOrEqual,
            "<" => QueryOp.Less,
            "<=" => QueryOp.LessOrEqual,
            "contains" => QueryOp.Contains,
            "startswith" => QueryOp.StartsWith,
            "is blank" => QueryOp.IsBlank,
            "is not blank" => QueryOp.IsNotBlank,
            _ => QueryOp.Equals,
        };

        clause = new QueryClause(field.Name, op, Unquote(rawValue));
        return true;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2)
        {
            if ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
                return value[1..^1];
        }
        return value;
    }

    /// <summary>Splits on a keyword outside of quotes (case-insensitive).</summary>
    internal static List<string> SplitTopLevel(string text, string keyword)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        char quote = '\0';
        int i = 0;
        int n = text.Length;
        int kwLength = keyword.Length;

        while (i < n)
        {
            var c = text[i];
            if (quote != '\0')
            {
                current.Append(c);
                if (c == quote) quote = '\0';
                i++;
                continue;
            }
            if (c == '"' || c == '\'')
            {
                quote = c;
                current.Append(c);
                i++;
                continue;
            }

            if ((char.IsLetter(c) || c == keyword[0]) && i + kwLength <= n &&
                string.Compare(text, i, keyword, 0, kwLength, StringComparison.OrdinalIgnoreCase) == 0 &&
                (i == 0 || !char.IsLetterOrDigit(text[i - 1])) &&
                (i + kwLength >= n || !char.IsLetterOrDigit(text[i + kwLength])))
            {
                parts.Add(current.ToString());
                current.Clear();
                i += kwLength;
                continue;
            }

            current.Append(c);
            i++;
        }
        parts.Add(current.ToString());
        return parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
    }

    public static IEnumerable<RecordRow> Apply(IEnumerable<RecordRow> records, CompiledQuery? query)
    {
        if (query is null) return records;
        return records.Where(query.Matches);
    }

    /// <summary>Stable sort: numbers before text ascending; text ignores case.</summary>
    public static IEnumerable<RecordRow> Sort(IEnumerable<RecordRow> records, FieldDef field, bool descending)
    {
        static (bool IsNumber, double Number, string Text) Key(RecordRow r, string name)
        {
            var raw = r.Get(name);
            if (double.TryParse(raw?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return (true, d, raw);
            return (false, 0, raw ?? string.Empty);
        }

        var name = field.Name;
        if (!descending)
            return records.OrderBy(r => Key(r, name));
        return records.OrderByDescending(r => Key(r, name));
    }
}
