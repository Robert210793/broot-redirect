using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Models
{
    public sealed class GlobalRule
    {
        public Guid Id { get; set; }

        public string Search { get; set; } = string.Empty;

        public string Replace { get; set; } = string.Empty;

        public bool CaseSensitive { get; set; }

        public int Priority { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
