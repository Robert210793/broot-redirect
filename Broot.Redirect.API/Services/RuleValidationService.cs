using Broot.Redirect.API.Dtos;
using Broot.Redirect.Core.Models;
using System.Text.RegularExpressions;

namespace Broot.Redirect.API.Services
{
    public class RuleValidationService
    {
        private static readonly Regex MatcherPattern = new(
            @"^(\/|[a-zA-Z0-9])([a-zA-Z0-9\-._~:/?#\[\]@!$&'()*+,;=%]*)$",
            RegexOptions.Compiled);

        public List<string> ValidateCreate(CreateRuleRequest request)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(request.Matcher))
            {
                errors.Add("URL-Muster darf nicht leer sein");

                return errors;
            }

            if (request.Matcher.Length > 500)
            {
                errors.Add("URL-Muster ist zu lang");
            }

            if (!MatcherPattern.IsMatch(request.Matcher))
            {
                errors.Add("URL-Muster muss mit '/' beginnen oder eine Domain sein (z.B. example.com)");
            }

            if (request.TargetUrl != null && request.TargetUrl.Length > 2000)
            {
                errors.Add("Ziel-URL ist zu lang");
            }

            if (request.InfoText != null && request.InfoText.Length > 5000)
            {
                errors.Add("Info-Text ist zu lang");
            }

            if (Enum.TryParse<RedirectType>(request.RedirectType, ignoreCase: true, out var redirectType))
            {
                ValidateTargetUrlForType(request.TargetUrl, redirectType, errors);
            }

            ValidateKeptQueryParams(request.KeptQueryParams, errors);

            ValidateStaticQueryParams(request.StaticQueryParams, errors);

            ValidateSearchAndReplace(request.SearchAndReplace, errors);

            return errors;
        }

        private static void ValidateTargetUrlForType(string? targetUrl, RedirectType redirectType, List<string> errors)
        {
            if (string.IsNullOrEmpty(targetUrl))
            {
                return;
            }

            switch (redirectType)
            {
                case RedirectType.Wildcard:
                    {
                        if (!targetUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                            && !targetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add("Bei Typ 'Vollstaendig' muss die Ziel-URL eine vollstaendige URL mit http:// oder https:// sein");
                        }

                        break;
                    }

                case RedirectType.Partial:
                    {
                        if (!targetUrl.StartsWith('/')
                            && !targetUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                            && !targetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add("Bei Typ 'Teilweise' muss die Ziel-URL mit '/' beginnen oder eine vollstaendige URL sein");
                        }

                        break;
                    }

                case RedirectType.Domain:
                    {
                        if (!targetUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                            && !targetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add("Bei Typ 'Domain-Ersatz' muss die Ziel-URL eine vollstaendige URL mit http:// oder https:// sein");

                            break;
                        }

                        if (Uri.TryCreate(targetUrl, UriKind.Absolute, out var parsedUri))
                        {
                            if (parsedUri.AbsolutePath != "/" && parsedUri.AbsolutePath != string.Empty)
                            {
                                errors.Add("Bei Typ 'Domain-Ersatz' darf die Ziel-URL keine Unterordner enthalten");
                            }
                        }

                        break;
                    }
            }
        }

        private static void ValidateKeptQueryParams(List<KeptQueryParam>? keptQueryParams, List<string> errors)
        {
            if (keptQueryParams == null)
            {
                return;
            }

            for (var index = 0; index < keptQueryParams.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(keptQueryParams[index].KeyPattern))
                {
                    errors.Add($"keptQueryParams[{index}]: KeyPattern darf nicht leer sein");
                }
            }
        }

        private static void ValidateStaticQueryParams(List<StaticQueryParam>? staticQueryParams, List<string> errors)
        {
            if (staticQueryParams == null)
            {
                return;
            }

            for (var index = 0; index < staticQueryParams.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(staticQueryParams[index].Key))
                {
                    errors.Add($"staticQueryParams[{index}]: Key darf nicht leer sein");
                }
            }
        }

        private static void ValidateSearchAndReplace(List<SearchAndReplaceEntry>? searchAndReplace, List<string> errors)
        {
            if (searchAndReplace == null)
            {
                return;
            }

            for (var index = 0; index < searchAndReplace.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(searchAndReplace[index].Search))
                {
                    errors.Add($"searchAndReplace[{index}]: Suchbegriff darf nicht leer sein");
                }
            }
        }
    }
}