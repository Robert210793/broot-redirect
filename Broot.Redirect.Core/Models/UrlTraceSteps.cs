using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Models
{
    public sealed class UrlTraceStep
    {
        public required string Type { get; set; }

        public required string Description { get; set; }

        public string? Before { get; set; }

        public string? After { get; set; }
    }
}
