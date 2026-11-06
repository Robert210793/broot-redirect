using System.Text.Json.Serialization;

namespace Broot.Redirect.Core.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AutoRedirectMode
    {
        PerRule,

        Always,

        Never
    }
}
