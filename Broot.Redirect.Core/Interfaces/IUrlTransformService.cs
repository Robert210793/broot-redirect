using Broot.Redirect.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Interfaces
{
    public interface IUrlTransformService
    {
        /// <summary>
        /// Resolves the final target URL by applying the matched rule's transformation pipeline
        /// to the original request URL.
        /// </summary>
        /// <param name="originalUrl">The original incoming URL.</param>
        /// <param name="rule">The matched redirect rule.</param>
        /// <param name="defaultNewDomain">Fallback domain from configuration.</param>
        /// <returns>The fully resolved target URL.</returns>
        /// 
        string ResolveTargetUrl(string originalUrl, RedirectRule rule, string defaultNewDomain);

        /// <summary>
        /// Resolves the final target URL and records a step-by-step trace of every
        /// transformation applied. Used by the validate endpoint for debugging.
        /// </summary>
        /// 
        (string ResolvedUrl, List<UrlTraceStep> Trace) ResolveTargetUrlWithTrace(
            string originalUrl,
            RedirectRule rule,
            string defaultNewDomain);
    }
}