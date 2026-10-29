namespace Broot.Redirect.Core.Services
{
    public static class MatcherEquivalence
    {
        private const char SegmentSeparator = '\u001f';
        private const char PairSeparator = '\u001d';
        private const char QuerySeparator = '\u001e';

        public static string[] SplitPathSegments(string matcher)
        {
            var normalized = matcher.Trim().Split('?', 2)[0].ToLowerInvariant().TrimEnd('/');

            var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

            for (var index = 0; index < segments.Length; index++)
            {
                try { segments[index] = Uri.UnescapeDataString(segments[index]); }
                catch { }
            }

            return segments;
        }

        public static HashSet<(string Key, string Value)> ParseQueryPairs(string matcher)
        {
            var pairs = new HashSet<(string Key, string Value)>();
            var queryIndex = matcher.IndexOf('?');

            if (queryIndex < 0)
            {
                return pairs;
            }

            var query = matcher[(queryIndex + 1)..];
            var fragmentIndex = query.IndexOf('#');

            if (fragmentIndex >= 0)
            {
                query = query[..fragmentIndex];
            }

            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                pairs.Add(ParseQueryPair(part));
            }

            return pairs;
        }

        public static (string Key, string Value) ParseQueryPair(string part)
        {
            var separatorIndex = part.IndexOf('=');
            var key = separatorIndex >= 0 ? part[..separatorIndex] : part;
            var value = separatorIndex >= 0 ? part[(separatorIndex + 1)..] : string.Empty;

            return (DecodeAndLower(key), DecodeAndLower(value));
        }

        public static bool AreEquivalent(string left, string right)
        {
            return SplitPathSegments(left).SequenceEqual(SplitPathSegments(right), StringComparer.OrdinalIgnoreCase)
                && ParseQueryPairs(left).SetEquals(ParseQueryPairs(right));
        }

        public static string GetKey(string matcher)
        {
            var segments = SplitPathSegments(matcher).Select(segment => segment.ToLowerInvariant());

            var pairs = ParseQueryPairs(matcher)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ThenBy(pair => pair.Value, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}{PairSeparator}{pair.Value}");

            return string.Join(SegmentSeparator, segments) + QuerySeparator + string.Join(SegmentSeparator, pairs);
        }

        private static string DecodeAndLower(string value)
        {
            try { value = Uri.UnescapeDataString(value); }
            catch (UriFormatException) { }

            return value.ToLowerInvariant();
        }
    }
}
