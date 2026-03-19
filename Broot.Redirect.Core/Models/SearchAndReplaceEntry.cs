using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Models
{
    public sealed class SearchAndReplaceEntry
    {
        public required string Search { get; set; }

        public string Replace { get; set; } = string.Empty;

        public bool CaseSensitive { get; set; }
    }
}
