using Broot.Redirect.API.Dtos;
using Broot.Redirect.Core.Models;
using System.Text.RegularExpressions;

namespace Broot.Redirect.API.Services
{
    public sealed class RuleValidationService
    {
        private static readonly Regex MatcherPattern = new(
            @"^(/|[a-zA-Z0-9])([a-zA-Z0-9\-._~:/?#\[\]@!$&'()*+,;=%]*)$",
            RegexOptions.Compiled);

        private const int MatcherMaxLength = 500;
        private const int TargetUrlMaxLength = 2000;
        private const int InfoTextMaxLength = 5000;

        public List<ValidationError> ValidateCreate(CreateRuleRequest request)
        {
            var errors = new List<ValidationError>();

            ValidateMatcher(request.Matcher, errors);

            if (!TryParseRedirectType(request.RedirectType, out var redirectType))
            {
                errors.Add(new ValidationError("redirectType", $"Ungueltiger Weiterleitungstyp: '{request.RedirectType}'"));
            }
            else
            {
                ValidateTargetUrl(request.TargetUrl, redirectType, errors);
            }

            ValidateInfoText(request.InfoText, errors);

            ValidateKeptQueryParams(request.KeptQueryParams, errors);

            ValidateStaticQueryParams(request.StaticQueryParams, errors);

            ValidateSearchAndReplace(request.SearchAndReplace, errors);

            return errors;
        }

        public List<ValidationError> ValidateUpdate(UpdateRuleRequest request, RedirectType existingRedirectType)
        {
            var errors = new List<ValidationError>();

            if (request.Matcher != null)
            {
                ValidateMatcher(request.Matcher, errors);
            }

            var effectiveRedirectType = existingRedirectType;

            if (request.RedirectType != null)
            {
                if (!TryParseRedirectType(request.RedirectType, out var parsedType))
                {
                    errors.Add(new ValidationError("redirectType", $"Ungueltiger Weiterleitungstyp: '{request.RedirectType}'"));
                }
                else
                {
                    effectiveRedirectType = parsedType;
                }
            }

            if (request.TargetUrl != null)
            {
                ValidateTargetUrl(request.TargetUrl, effectiveRedirectType, errors);
            }

            if (request.InfoText != null)
            {
                ValidateInfoText(request.InfoText, errors);
            }

            if (request.KeptQueryParams != null)
            {
                ValidateKeptQueryParams(request.KeptQueryParams, errors);
            }

            if (request.StaticQueryParams != null)
            {
                ValidateStaticQueryParams(request.StaticQueryParams, errors);
            }

            if (request.SearchAndReplace != null)
            {
                ValidateSearchAndReplace(request.SearchAndReplace, errors);
            }

            return errors;
        }

        private static void ValidateMatcher(string matcher, List<ValidationError> errors)
        {
            if (string.IsNullOrWhiteSpace(matcher))
            {
                errors.Add(new ValidationError("matcher", "URL-Muster darf nicht leer sein."));

                return;
            }

            var trimmed = matcher.Trim();

            if (trimmed.Length > MatcherMaxLength)
            {
                errors.Add(new ValidationError("matcher", $"URL-Muster ist zu lang (maximal {MatcherMaxLength} Zeichen)."));

                return;
            }

            if (!MatcherPattern.IsMatch(trimmed))
            {
                errors.Add(new ValidationError("matcher", "URL-Muster muss mit '/' beginnen oder eine Domain sein (z.B. example.com)."));
            }
        }

        private static void ValidateTargetUrl(string? targetUrl, RedirectType redirectType, List<ValidationError> errors)
        {
            if (string.IsNullOrEmpty(targetUrl))
            {
                return;
            }

            if (targetUrl.Length > TargetUrlMaxLength)
            {
                errors.Add(new ValidationError("targetUrl", $"Ziel-URL ist zu lang (maximal {TargetUrlMaxLength} Zeichen)."));

                return;
            }

            switch (redirectType)
            {
                case RedirectType.Wildcard:
                    {
                        if (!StartsWithHttpOrHttps(targetUrl))
                        {
                            errors.Add(new ValidationError(
                                "targetUrl",
                                "Bei Typ 'Wildcard' muss die Ziel-URL eine vollstaendige URL mit http:// oder https:// sein (z.B. https://beispiel.com/neue-seite)."));
                        }

                        break;
                    }

                case RedirectType.Partial:
                    {
                        if (!targetUrl.StartsWith('/') && !StartsWithHttpOrHttps(targetUrl))
                        {
                            errors.Add(new ValidationError(
                                "targetUrl",
                                "Bei Typ 'Teilweise' muss die Ziel-URL mit '/' beginnen (z.B. /neue-sektion/) oder eine vollstaendige URL sein."));
                        }

                        break;
                    }

                case RedirectType.Domain:
                    {
                        if (!StartsWithHttpOrHttps(targetUrl))
                        {
                            errors.Add(new ValidationError(
                                "targetUrl",
                                "Bei Typ 'Domain' muss die Ziel-URL eine vollstaendige URL mit http:// oder https:// sein (z.B. https://neue-domain.com)."));

                            break;
                        }

                        if (Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
                        {
                            if (uri.AbsolutePath != "/" && uri.AbsolutePath != string.Empty)
                            {
                                errors.Add(new ValidationError(
                                    "targetUrl",
                                    "Bei Typ 'Domain' darf die Ziel-URL keine Unterordner enthalten (nur https://domain.com)."));
                            }
                        }

                        break;
                    }

                case RedirectType.Regex:
                    {
                        break;
                    }
            }
        }

        private static void ValidateInfoText(string? infoText, List<ValidationError> errors)
        {
            if (string.IsNullOrEmpty(infoText))
            {
                return;
            }

            if (infoText.Length > InfoTextMaxLength)
            {
                errors.Add(new ValidationError("infoText", $"Info-Text ist zu lang (maximal {InfoTextMaxLength} Zeichen)."));
            }
        }

        private static void ValidateKeptQueryParams(List<KeptQueryParam>? items, List<ValidationError> errors)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            for (var index = 0; index < items.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(items[index].KeyPattern))
                {
                    errors.Add(new ValidationError(
                        $"keptQueryParams[{index}].keyPattern",
                        "Schluessel-Muster ist erforderlich."));
                }
            }
        }

        private static void ValidateStaticQueryParams(List<StaticQueryParam>? items, List<ValidationError> errors)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            for (var index = 0; index < items.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(items[index].Key))
                {
                    errors.Add(new ValidationError(
                        $"staticQueryParams[{index}].key",
                        "Key ist erforderlich."));
                }
            }
        }

        private static void ValidateSearchAndReplace(List<SearchAndReplaceEntry>? items, List<ValidationError> errors)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            for (var index = 0; index < items.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(items[index].Search))
                {
                    errors.Add(new ValidationError(
                        $"searchAndReplace[{index}].search",
                        "Suchbegriff ist erforderlich."));
                }
            }
        }

        private static bool StartsWithHttpOrHttps(string url)
        {
            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseRedirectType(string value, out RedirectType redirectType)
        {
            return Enum.TryParse(value, ignoreCase: true, out redirectType);
        }
    }

    public sealed class ValidationError
    {
        public string Field { get; }

        public string Message { get; }

        public ValidationError(string field, string message)
        {
            Field = field;
            Message = message;
        }
    }
}