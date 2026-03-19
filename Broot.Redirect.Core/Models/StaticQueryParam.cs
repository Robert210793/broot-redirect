using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Models
{
    public sealed class StaticQueryParam
    {
        public required string Key { get; set; }

        public string Value { get; set; } = string.Empty;

        public bool SkipEncoding { get; set; }
    }
}
