using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Models
{
    public sealed class KeptQueryParam
    {
        public required string KeyPattern { get; set; }

        public string? ValuePattern { get; set; }

        public string? TargetKey { get; set; }

        public bool SkipEncoding { get; set; }
    }
}
