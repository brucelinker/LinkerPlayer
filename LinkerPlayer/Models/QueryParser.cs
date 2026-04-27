using System.Text.RegularExpressions;

namespace LinkerPlayer.Models;

/// <summary>
/// Classification of a single lexical token for syntax highlighting.
/// </summary>
public enum QueryTokenKind
{
    /// <summary>Plain-text search — no structured query was detected.</summary>
    PlainText,
    /// <summary>A recognised field name (title, artist, bitrate, …).</summary>
    Field,
    /// <summary>A comparison or keyword operator (=, &gt;, contains, …).</summary>
    Operator,
    /// <summary>The value portion of a clause.</summary>
    Value,
    /// <summary>An OR / || / | separator.</summary>
    OrKeyword,
    /// <summary>A word that looks like a field name but is not recognised.</summary>
    UnknownField,
    /// <summary>Whitespace between tokens (rendered in normal colour).</summary>
    Whitespace,
}

/// <summary>
/// A single classified span of text produced by <see cref="QueryParser.Tokenize"/>.
/// </summary>
public sealed record QueryToken(int Start, int Length, QueryTokenKind Kind)
{
    public string Slice(string source) => source.Substring(Start, Length);
}

/// <summary>
/// Parses a mini query string typed into the Library search box into a list of
/// AND-groups that are OR'd together.
///
/// Supported syntax (case-insensitive):
///   Plain text            →  beethoven                   (falls back to keyword search)
///   Exact match           →  genre = jazz
///   Not equal             →  genre != jazz
///   Contains              →  artist contains miles       (or  artist ~ miles)
///   Greater / less        →  bitrate > 320
///   Greater-or-equal      →  year >= 1970
///   Between (chained)     →  year > 1960 &lt; 1990       (same field, merged to Between)
///   Implicit AND          →  bitrate > 200 genre = jazz  (adjacent clauses)
///   Explicit OR           →  genre = jazz OR genre = blues
///                            genre = jazz || genre = blues
///                            genre = jazz | genre = blues
///
/// Field aliases:
///   title                        → Title
///   artist                       → Artist
///   album                        → Album
///   genre, genres                → Genre
///   year, date                   → Year
///   bitrate, kbps                → Bitrate
///   codec, format, filetype, ext → FileType
///   performer, performers        → Performer
///   composer, composers          → Composer
/// </summary>
public static class QueryParser
{
    // Matches an OR separator:  OR  ||  |
    // Must be surrounded by whitespace or start/end of string to avoid matching
    // a value that happens to contain a pipe.
    private static readonly Regex OrSplitRegex = new(
        @"\s+(?:OR|\|\||\|)\s+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // One clause token: field  operator  value
    // op    = >=  <=  !=  >  <  ==  =  contains  ~
    // value = quoted string OR one-or-more non-whitespace chars
    private static readonly Regex ClauseRegex = new(
        @"(?<field>\w+)\s*(?<op>>=|<=|!=|>|<|==|=|contains|~)\s*(?<value>""[^""]*""|\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Attempts to parse <paramref name="input"/> as a structured query.
    /// Returns <c>true</c> and populates <paramref name="orGroups"/> when at least
    /// one valid clause is found.  Each inner list is an AND-group; the outer list
    /// is OR'd — a track matches if ANY group fully matches.
    /// Returns <c>false</c> when the input looks like plain text so the caller can
    /// fall back to the existing keyword search.
    /// </summary>
    public static bool TryParse(string input, out List<List<FilterCriteria>> orGroups)
    {
        orGroups = new List<List<FilterCriteria>>();

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        // Split on OR / || / |  to get individual AND-segments
        string[] segments = OrSplitRegex.Split(input);

        foreach (string segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            MatchCollection matches = ClauseRegex.Matches(segment);

            if (matches.Count == 0)
            {
                // Segment has no recognised clause → treat whole input as plain text
                return false;
            }

            List<(FilterType Type, FilterOperator Op, string Value)> raw = new();

            foreach (Match match in matches)
            {
                FilterType? type = MapField(match.Groups["field"].Value);
                if (type == null)
                {
                    return false; // unknown field — treat as plain text
                }

                FilterOperator? op = MapOperator(match.Groups["op"].Value);
                if (op == null)
                {
                    continue;
                }

                string value = match.Groups["value"].Value.Trim('"');
                raw.Add((type.Value, op.Value, value));
            }

            if (raw.Count == 0)
            {
                return false;
            }

            // Merge adjacent numeric bounds into Between where applicable
            List<FilterCriteria> andGroup = MergeBetween(raw);
            orGroups.Add(andGroup);
        }

        return orGroups.Count > 0;
    }

    // -------------------------------------------------------------------------
    // Tokenizer — produces classified spans for syntax highlighting
    // -------------------------------------------------------------------------

    // Matches an OR keyword on its own (surrounded by whitespace or boundaries)
    private static readonly Regex OrTokenRegex = new(
        @"(?<!\S)(?:OR|\|\||\|)(?!\S)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches a single word (potential field name or bare value)
    private static readonly Regex WordRegex = new(
        @"\S+",
        RegexOptions.Compiled);

    /// <summary>
    /// Walks <paramref name="input"/> and returns a flat list of classified
    /// <see cref="QueryToken"/> spans suitable for syntax highlighting.
    /// Covers the entire string with no gaps (whitespace spans are included).
    /// When the input contains no structured clauses it returns a single
    /// <see cref="QueryTokenKind.PlainText"/> token for the whole string.
    /// </summary>
    public static List<QueryToken> Tokenize(string input)
    {
        List<QueryToken> result = new();

        if (string.IsNullOrEmpty(input))
        {
            return result;
        }

        // Fast exit: if there are no clause-like patterns, treat as plain text
        MatchCollection clauseMatches = ClauseRegex.Matches(input);
        MatchCollection orMatches     = OrTokenRegex.Matches(input);

        if (clauseMatches.Count == 0 && orMatches.Count == 0)
        {
            result.Add(new QueryToken(0, input.Length, QueryTokenKind.PlainText));
            return result;
        }

        // Build a sorted list of all recognised spans so we can fill gaps
        List<(int Start, int End, QueryTokenKind Kind)> spans = new();

        foreach (Match m in clauseMatches)
        {
            Group fieldGroup = m.Groups["field"];
            Group opGroup    = m.Groups["op"];
            Group valueGroup = m.Groups["value"];

            string fieldText = fieldGroup.Value;
            QueryTokenKind fieldKind = MapField(fieldText) != null
                ? QueryTokenKind.Field
                : QueryTokenKind.UnknownField;

            spans.Add((fieldGroup.Index, fieldGroup.Index + fieldGroup.Length, fieldKind));
            spans.Add((opGroup.Index,    opGroup.Index    + opGroup.Length,    QueryTokenKind.Operator));
            spans.Add((valueGroup.Index, valueGroup.Index + valueGroup.Length, QueryTokenKind.Value));
        }

        foreach (Match m in orMatches)
        {
            spans.Add((m.Index, m.Index + m.Length, QueryTokenKind.OrKeyword));
        }

        // Sort by start position and remove overlaps (keep first)
        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        List<(int Start, int End, QueryTokenKind Kind)> deduped = new();
        int cursor = 0;
        foreach ((int start, int end, QueryTokenKind kind) in spans)
        {
            if (start < cursor)
            {
                continue; // overlaps a previously accepted span — skip
            }
            deduped.Add((start, end, kind));
            cursor = end;
        }

        // Emit tokens, filling any gaps with whitespace / unknown-field spans
        cursor = 0;
        foreach ((int start, int end, QueryTokenKind kind) in deduped)
        {
            if (start > cursor)
            {
                // Gap between recognised spans — classify each word in the gap
                EmitGap(input, cursor, start, result);
            }

            result.Add(new QueryToken(start, end - start, kind));
            cursor = end;
        }

        // Trailing gap after last recognised span
        if (cursor < input.Length)
        {
            EmitGap(input, cursor, input.Length, result);
        }

        return result;
    }

    /// <summary>
    /// Classifies the gap between two recognised spans.
    /// Whitespace runs become <see cref="QueryTokenKind.Whitespace"/>;
    /// non-whitespace words that look like field names become
    /// <see cref="QueryTokenKind.UnknownField"/>, others become
    /// <see cref="QueryTokenKind.PlainText"/>.
    /// </summary>
    private static void EmitGap(string input, int from, int to, List<QueryToken> result)
    {
        string gap = input.Substring(from, to - from);
        int localCursor = 0;

        foreach (Match word in WordRegex.Matches(gap))
        {
            // Whitespace before the word
            if (word.Index > localCursor)
            {
                result.Add(new QueryToken(from + localCursor, word.Index - localCursor, QueryTokenKind.Whitespace));
            }

            // Is it an OR separator sitting in a gap (e.g. no adjacent clauses)?
            if (OrTokenRegex.IsMatch(word.Value))
            {
                result.Add(new QueryToken(from + word.Index, word.Length, QueryTokenKind.OrKeyword));
            }
            else
            {
                // Classify as UnknownField only if it looks like someone intended
                // a field name (all alpha, reasonable length) but it's not recognised
                bool looksLikeField = word.Value.Length <= 20 &&
                                      word.Value.All(c => char.IsLetter(c) || c == '_');

                QueryTokenKind gapKind = looksLikeField
                    ? QueryTokenKind.UnknownField
                    : QueryTokenKind.PlainText;

                result.Add(new QueryToken(from + word.Index, word.Length, gapKind));
            }

            localCursor = word.Index + word.Length;
        }

        // Trailing whitespace in gap
        if (localCursor < gap.Length)
        {
            result.Add(new QueryToken(from + localCursor, gap.Length - localCursor, QueryTokenKind.Whitespace));
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static FilterType? MapField(string field)
    {
        return field.ToLowerInvariant() switch
        {
            "title"                         => FilterType.Title,
            "artist"                        => FilterType.Artist,
            "album"                         => FilterType.Album,
            "genre" or "genres"             => FilterType.Genre,
            "year" or "date"                => FilterType.Year,
            "bitrate" or "kbps"             => FilterType.Bitrate,
            "codec" or "format"
                or "filetype" or "ext"      => FilterType.FileType,
            "performer" or "performers"     => FilterType.Performer,
            "composer" or "composers"       => FilterType.Composer,
            _                               => null
        };
    }

    private static FilterOperator? MapOperator(string op)
    {
        return op.ToLowerInvariant() switch
        {
            "="  or "==" => FilterOperator.Equals,
            "!="         => FilterOperator.NotEquals,
            "contains"
                or "~"   => FilterOperator.Contains,
            ">"          => FilterOperator.GreaterThan,
            ">="         => FilterOperator.GreaterThanOrEqual,
            "<"          => FilterOperator.LessThan,
            "<="         => FilterOperator.LessThanOrEqual,
            _            => null
        };
    }

    private static bool IsNumericField(FilterType type)
        => type == FilterType.Year || type == FilterType.Bitrate;

    private static bool IsLowerBound(FilterOperator op)
        => op == FilterOperator.GreaterThan || op == FilterOperator.GreaterThanOrEqual;

    private static bool IsUpperBound(FilterOperator op)
        => op == FilterOperator.LessThan || op == FilterOperator.LessThanOrEqual;

    /// <summary>
    /// Walks the raw clause list and collapses consecutive lower+upper bound
    /// clauses on the same numeric field into a single Between criterion.
    /// </summary>
    private static List<FilterCriteria> MergeBetween(
        List<(FilterType Type, FilterOperator Op, string Value)> raw)
    {
        List<FilterCriteria> result = new();
        int i = 0;

        while (i < raw.Count)
        {
            (FilterType type, FilterOperator op, string value) = raw[i];

            // Try to pair with the next clause when this one is a numeric bound
            if (IsNumericField(type) && i + 1 < raw.Count)
            {
                (FilterType nextType, FilterOperator nextOp, string nextValue) = raw[i + 1];

                if (nextType == type)
                {
                    bool currentIsLower = IsLowerBound(op);
                    bool currentIsUpper = IsUpperBound(op);
                    bool nextIsLower = IsLowerBound(nextOp);
                    bool nextIsUpper = IsUpperBound(nextOp);

                    if ((currentIsLower && nextIsUpper) || (currentIsUpper && nextIsLower))
                    {
                        string lowerValue = currentIsLower ? value : nextValue;
                        string upperValue = currentIsUpper ? value : nextValue;

                        result.Add(new FilterCriteria
                        {
                            Type = type,
                            Operator = FilterOperator.Between,
                            Value = lowerValue,
                            ValueSecondary = upperValue
                        });

                        i += 2;
                        continue;
                    }
                }
            }

            result.Add(new FilterCriteria
            {
                Type = type,
                Operator = op,
                Value = value
            });

            i++;
        }

        return result;
    }
}
