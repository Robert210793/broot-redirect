namespace Broot.Redirect.API.Configuration
{
    public static class TeamsEmbedding
    {
        public const string FrameAncestorsPolicy =
            "frame-ancestors 'self' https://teams.microsoft.com https://*.teams.microsoft.com https://*.cloud.microsoft https://*.office.com https://*.microsoft365.com https://*.skype.com";

        public const string PartitionedCookieAttribute = "Partitioned";

        public static void ConfigureSessionCookie(CookieBuilder cookie, bool allowTeamsEmbedding, bool isProduction)
        {
            if (allowTeamsEmbedding)
            {
                cookie.SameSite = SameSiteMode.None;
                cookie.SecurePolicy = CookieSecurePolicy.Always;
                cookie.Extensions.Add(PartitionedCookieAttribute);

                return;
            }

            cookie.SameSite = SameSiteMode.Lax;
            cookie.SecurePolicy = isProduction
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
        }

        public static void ApplyFrameAncestors(IHeaderDictionary headers)
        {
            headers.ContentSecurityPolicy = FrameAncestorsPolicy;
        }
    }
}
