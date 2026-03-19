namespace Broot.Redirect.API.Dtos
{
    using System.ComponentModel.DataAnnotations;

    namespace Broot.Redirect.API.Dtos
    {
        public class CreateGlobalRuleRequest
        {
            [Required]
            public string Search { get; set; } = string.Empty;

            [Required]
            public string Replace { get; set; } = string.Empty;

            public bool CaseSensitive { get; set; } = false;

            public int Priority { get; set; } = 0;
        }

        /// <summary>
        /// Partial update request. Only provided (non-null) fields are merged.
        /// </summary>
        /// 
        public class UpdateGlobalRuleRequest
        {
            public string? Search { get; set; }

            public string? Replace { get; set; }

            public bool? CaseSensitive { get; set; }

            public int? Priority { get; set; }
        }
    }
}
