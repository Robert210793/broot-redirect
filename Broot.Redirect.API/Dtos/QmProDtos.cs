namespace Broot.Redirect.API.Dtos
{
    public class QmProPublishRequest
    {
        public string? DocumentId { get; set; }

        public string? OldUrl { get; set; }

        public string? PublicationUrl { get; set; }
    }

    public class QmProUnpublishRequest
    {
        public string? DocumentId { get; set; }
    }

    public class QmProRuleResponse
    {
        public Guid? RuleId { get; set; }

        public string Result { get; set; } = string.Empty;
    }
}
