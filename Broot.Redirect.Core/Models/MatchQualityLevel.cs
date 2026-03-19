using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Broot.Redirect.Core.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MatchQualityLevel
    {
        Red,
        Yellow,
        Green
    }
}
