using System.Text.Json.Serialization;

namespace Broot.Redirect.Core.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RuleSource
    {
        /// <summary>
        /// Source could not be determined (e.g. legacy rules stored before source tracking existed).
        /// </summary>
        Unknown,

        /// <summary>
        /// Rule was created manually through the rule editor.
        /// </summary>
        Manual,

        /// <summary>
        /// Rule was created or updated via a file import.
        /// </summary>
        Import
    }
}
