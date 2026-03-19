using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Models
{
    public sealed class UrlTransformationResult
    {
        public required string OriginalUrl { get; set; }

        public required string ResolvedUrl { get; set; }

        public int MatchQuality { get; set; }
    }
}
