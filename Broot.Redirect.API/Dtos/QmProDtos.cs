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

    public class AreaRuleSyncRequest
    {
        public List<AreaRuleSyncEntry?>? Entries { get; set; }
    }

    public class AreaRuleSyncEntry
    {
        public string? Id { get; set; }

        public string? SourceUrl { get; set; }

        public string? TargetUrl { get; set; }
    }

    public class AreaRuleSyncResponse
    {
        public int Created { get; set; }

        public int Updated { get; set; }

        public int Deleted { get; set; }

        public int Unchanged { get; set; }

        public List<AreaRuleSyncSkippedEntry> Skipped { get; set; } = new();
    }

    public class AreaRuleSyncSkippedEntry
    {
        public string? Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Error { get; set; } = string.Empty;

        public Guid? ConflictingRuleId { get; set; }
    }
}
