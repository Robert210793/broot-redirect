using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Models
{
    /// <summary>
    /// A global search-and-replace rule applied to all resolved target URLs
    /// after per-rule transforms. Applied in Priority order (lower = first).
    /// </summary>
    /// 
    public sealed class GlobalRule
    {
        public Guid Id { get; set; }

        /// <summary>
        /// Text or regex pattern to find in the resolved URL.
        /// </summary>
        /// 
        public string Search { get; set; } = string.Empty;

        /// <summary>
        /// Replacement string. May contain regex group references ($1, $2, etc.)
        /// if the Search value is a regex pattern.
        /// </summary>
        /// 
        public string Replace { get; set; } = string.Empty;

        public bool CaseSensitive { get; set; }

        /// <summary>
        /// Execution order. Lower values are applied first.
        /// </summary>
        /// 
        public int Priority { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
