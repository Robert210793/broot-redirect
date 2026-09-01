using Broot.Redirect.API.Dtos;
using Broot.Redirect.API.Services;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Broot.Redirect.API.Configuration;
using System.Text.Json;

namespace Broot.Redirect.API.Controllers
{
    [ApiController]
    [Route("api/rules")]
    public class RulesController : ControllerBase
    {
        private readonly IRedirectRuleRepository _repository;
        private readonly IRuleCacheService _cacheService;
        private readonly IRuleMatchingService _ruleMatchingService;
        private readonly IUrlTransformService _urlTransformService;
        private readonly IAppSettingsCacheService _settingsCache;
        private readonly RuleValidationService _validationService;
        private readonly BrootRedirectOptions _options;
        private readonly ILogger<RulesController> _logger;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImportResponse> ActiveJobs = new();

        private static readonly JsonSerializerOptions CamelCaseOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public RulesController(
            IRedirectRuleRepository repository,
            IRuleCacheService cacheService,
            IRuleMatchingService ruleMatchingService,
            IUrlTransformService urlTransformService,
            IAppSettingsCacheService settingsCache,
            RuleValidationService validationService,
            IOptions<BrootRedirectOptions> options,
            ILogger<RulesController> logger)
        {
            _repository = repository;
            _cacheService = cacheService;
            _ruleMatchingService = ruleMatchingService;
            _urlTransformService = urlTransformService;
            _settingsCache = settingsCache;
            _validationService = validationService;
            _options = options.Value;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult GetPaginated(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 50,
            [FromQuery] string? search = null,
            [FromQuery] string sortBy = "createdAt",
            [FromQuery] string sortOrder = "desc")
        {
            if (page < 1)
            {
                page = 1;
            }

            if (limit < 1 || limit > 500)
            {
                limit = 50;
            }

            var allRules = _cacheService.GetAll();
            var filteredRules = RuleImportExportService.ApplySearch(allRules, search);
            filteredRules = RuleImportExportService.ApplySorting(filteredRules, sortBy, sortOrder);

            var filteredList = filteredRules.ToList();
            var total = filteredList.Count;
            var totalPages = (int)Math.Ceiling((double)total / limit);
            var pagedRules = filteredList.Skip((page - 1) * limit).Take(limit).ToList();

            return Ok(new PaginatedRulesResponse
            {
                Rules = pagedRules,
                Total = total,
                TotalPages = totalPages,
                CurrentPage = page
            });
        }

        [HttpGet("{id:guid}")]
        public IActionResult GetById(Guid id)
        {
            var rule = _cacheService.GetById(id);

            if (rule == null)
            {
                return NotFound(new { error = "Rule not found" });
            }

            return Ok(rule);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateRuleRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { error = "Invalid rule data" });
            }

            if (!TryParseRedirectType(request.RedirectType, out var redirectType))
            {
                return BadRequest(new { error = $"Invalid redirect type: {request.RedirectType}" });
            }

            request.Matcher = RuleImportExportService.NormalizeMatcher(request.Matcher);

            if (_cacheService.MatcherExists(request.Matcher))
            {
                return BadRequest(new { error = $"A rule with matcher '{request.Matcher}' already exists" });
            }

            var overlapping = _cacheService.FindOverlappingMatcher(request.Matcher, redirectType);

            if (overlapping != null)
            {
                return Conflict(new
                {
                    error = "URL matcher conflicts with existing rule",
                    code = "MATCHER_CONFLICT",
                    conflictingRule = new
                    {
                        id = overlapping.Id,
                        matcher = overlapping.Matcher
                    }
                });
            }

            var rule = new RedirectRule
            {
                Id = Guid.NewGuid(),
                Matcher = request.Matcher,
                TargetUrl = request.TargetUrl,
                RedirectType = redirectType,
                Source = RuleSource.Manual,
                InfoText = request.InfoText,
                AutoRedirect = request.AutoRedirect,
                DiscardQueryParams = request.DiscardQueryParams,
                ForwardQueryParams = request.ForwardQueryParams,
                KeptQueryParams = request.KeptQueryParams,
                StaticQueryParams = request.StaticQueryParams,
                SearchAndReplace = request.SearchAndReplace,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _repository.CreateAsync(rule);

            _cacheService.AddRule(rule);

            _logger.LogInformation("Created rule {RuleId} with matcher '{Matcher}'", rule.Id, rule.Matcher);

            return CreatedAtAction(nameof(GetById), new { id = rule.Id }, rule);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRuleRequest request)
        {
            var existingRule = _cacheService.GetById(id);

            if (existingRule == null)
            {
                return NotFound(new { error = "Rule not found" });
            }

            if (request.Matcher != null)
            {
                request.Matcher = RuleImportExportService.NormalizeMatcher(request.Matcher);
            }

            if (request.Matcher != null && request.Matcher != existingRule.Matcher)
            {
                if (_cacheService.MatcherExists(request.Matcher, excludeRuleId: id))
                {
                    return BadRequest(new { error = $"A rule with matcher '{request.Matcher}' already exists" });
                }

                // The type may change in this same request, and it decides whether the new
                // matcher can hierarchically swallow existing rules.
                var effectiveType = existingRule.RedirectType;

                if (request.RedirectType != null)
                {
                    if (!TryParseRedirectType(request.RedirectType, out effectiveType))
                    {
                        return BadRequest(new { error = $"Invalid redirect type: {request.RedirectType}" });
                    }
                }

                var overlapping = _cacheService.FindOverlappingMatcher(
                    request.Matcher,
                    effectiveType,
                    excludeRuleId: id);

                if (overlapping != null)
                {
                    return Conflict(new
                    {
                        error = "URL matcher conflicts with existing rule",
                        code = "MATCHER_CONFLICT",
                        conflictingRule = new
                        {
                            id = overlapping.Id,
                            matcher = overlapping.Matcher
                        }
                    });
                }
            }

            var updatedRule = new RedirectRule
            {
                Id = existingRule.Id,
                Matcher = request.Matcher ?? existingRule.Matcher,
                TargetUrl = request.TargetUrl ?? existingRule.TargetUrl,
                RedirectType = existingRule.RedirectType,
                Source = existingRule.Source,
                InfoText = request.InfoText ?? existingRule.InfoText,
                AutoRedirect = request.AutoRedirect ?? existingRule.AutoRedirect,
                DiscardQueryParams = request.DiscardQueryParams ?? existingRule.DiscardQueryParams,
                ForwardQueryParams = request.ForwardQueryParams ?? existingRule.ForwardQueryParams,
                KeptQueryParams = request.KeptQueryParams ?? existingRule.KeptQueryParams,
                StaticQueryParams = request.StaticQueryParams ?? existingRule.StaticQueryParams,
                SearchAndReplace = request.SearchAndReplace ?? existingRule.SearchAndReplace,
                CreatedAt = existingRule.CreatedAt
            };

            if (request.RedirectType != null)
            {
                if (!TryParseRedirectType(request.RedirectType, out var redirectType))
                {
                    return BadRequest(new { error = $"Invalid redirect type: {request.RedirectType}" });
                }

                updatedRule.RedirectType = redirectType;
            }

            await _repository.UpdateAsync(updatedRule);

            _cacheService.UpdateRule(updatedRule);

            _logger.LogInformation("Updated rule {RuleId}", id);

            return Ok(updatedRule);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var existingRule = _cacheService.GetById(id);

            if (existingRule == null)
            {
                return NotFound(new { error = "Rule not found" });
            }

            await _repository.DeleteAsync(id);

            _cacheService.RemoveRule(id);

            _logger.LogInformation("Deleted rule {RuleId}", id);

            return NoContent();
        }

        [HttpDelete("all")]
        public IActionResult DeleteAll()
        {
            var jobId = Guid.NewGuid().ToString("N");
            var allIds = _cacheService.GetAll().Select(r => r.Id).ToList();
            var totalCount = allIds.Count;

            _logger.LogWarning("DeleteAll requested. Rule count: {RuleCount}, Job: {JobId}", totalCount, jobId);

            ActiveJobs[jobId] = new ImportResponse { Total = totalCount };

            _ = Task.Run(async () =>
            {
                var progress = ActiveJobs[jobId];

                try
                {
                    const int batchSize = 100;

                    for (var i = 0; i < allIds.Count; i += batchSize)
                    {
                        var batch = allIds.Skip(i).Take(batchSize).ToList();
                        var batchDeleted = await _repository.BulkDeleteAsync(batch);

                        progress.Processed += batchDeleted;
                    }

                    _cacheService.ReplaceAll(new List<RedirectRule>());

                    progress.IsComplete = true;

                    _logger.LogInformation("DeleteAll completed: {Deleted} rules deleted. Job: {JobId}", progress.Processed, jobId);
                }
                catch (Exception exception)
                {
                    progress.IsComplete = true;
                    progress.Error = exception.Message;

                    _logger.LogError(exception, "DeleteAll failed. Job: {JobId}", jobId);
                }

                _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(t => ActiveJobs.TryRemove(jobId, out var removed));
            });

            return Ok(new { jobId, total = totalCount });
        }

        [HttpGet("jobs/{jobId}")]
        public IActionResult GetJobProgress(string jobId)
        {
            if (!ActiveJobs.TryGetValue(jobId, out var progress))
            {
                return NotFound(new { error = "Job not found or expired" });
            }

            return Ok(progress);
        }

        [HttpDelete("bulk")]
        public async Task<IActionResult> BulkDelete([FromBody] BulkDeleteRequest request)
        {
            if (request.Ids == null || request.Ids.Count == 0)
            {
                return BadRequest(new { error = "No rule IDs provided" });
            }

            var deleted = 0;
            var notFound = 0;
            var deletedIds = new HashSet<Guid>();

            foreach (var idString in request.Ids)
            {
                if (!Guid.TryParse(idString, out var id))
                {
                    notFound++;

                    continue;
                }

                var existingRule = _cacheService.GetById(id);

                if (existingRule == null)
                {
                    notFound++;

                    continue;
                }

                try
                {
                    await _repository.DeleteAsync(id);

                    deletedIds.Add(id);

                    deleted++;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to delete rule {RuleId} during bulk delete", id);

                    notFound++;
                }
            }

            if (deletedIds.Count > 0)
            {
                _cacheService.RemoveRules(deletedIds);
            }

            _logger.LogInformation("Bulk delete completed: {Deleted} deleted, {NotFound} not found", deleted, notFound);

            return Ok(new BulkDeleteResponse
            {
                Deleted = deleted,
                NotFound = notFound
            });
        }

        [HttpPost("import/preview")]
        public async Task<IActionResult> ImportPreview()
        {
            var contentType = Request.ContentType ?? string.Empty;

            var files = contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase)
                ? Request.Form.Files
                : null;

            var (entries, parseError) = await RuleImportExportService.ParseImportEntries(
                contentType, files, Request.Body, supportJson: true);

            if (parseError != null)
            {
                return BadRequest(new { error = parseError });
            }

            if (entries!.Count == 0)
            {
                return BadRequest(new { error = "No rules found in file" });
            }

            var matcherLookup = RuleImportExportService.BuildMatcherLookup(_cacheService.GetAll());

            var previewEntries = new List<ImportPreviewEntry>();
            var counts = new ImportPreviewCounts();

            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];

                var previewEntry = new ImportPreviewEntry
                {
                    Matcher = entry.Matcher,
                    TargetUrl = entry.TargetUrl,
                    RedirectType = entry.RedirectType ?? "partial",
                    InfoText = entry.InfoText
                };

                var (mappedRequest, validationError) = RuleImportExportService.ValidateImportEntry(entry, _validationService);

                if (validationError != null)
                {
                    previewEntry.Status = "invalid";
                    previewEntry.Reason = validationError;
                    counts.Invalid++;

                    previewEntries.Add(previewEntry);

                    continue;
                }

                var existingRule = RuleImportExportService.ResolveExistingRule(entry, _cacheService, matcherLookup);

                if (existingRule != null)
                {
                    previewEntry.ExistingRuleId = existingRule.Id.ToString();

                    if (RuleImportExportService.HasImportChanges(existingRule, mappedRequest!))
                    {
                        previewEntry.Status = "update";
                        counts.Update++;
                    }
                    else
                    {
                        previewEntry.Status = "unchanged";
                        counts.Unchanged++;
                    }
                }
                else
                {
                    previewEntry.Status = "new";
                    counts.New++;
                }

                previewEntries.Add(previewEntry);
            }

            const int previewLimit = 1000;

            var response = new ImportPreviewResponse
            {
                Total = previewEntries.Count,
                Limit = previewLimit,
                IsLimited = previewEntries.Count > previewLimit,
                Preview = previewEntries.Take(previewLimit).ToList(),
                Counts = counts
            };

            return Ok(response);
        }

        [HttpPost("import")]
        public async Task<IActionResult> Import()
        {
            var contentType = Request.ContentType ?? string.Empty;

            var files = contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase)
                ? Request.Form.Files
                : null;

            var (entries, parseError) = await RuleImportExportService.ParseImportEntries(
                contentType, files, Request.Body, supportJson: true);

            if (parseError != null)
            {
                return BadRequest(new { error = parseError });
            }

            if (entries!.Count == 0)
            {
                return BadRequest(new { error = "No rules to import" });
            }

            var jobId = Guid.NewGuid().ToString("N");
            var totalCount = entries.Count;

            ActiveJobs[jobId] = new ImportResponse { Total = totalCount };

            var repository = _repository;
            var cacheService = _cacheService;
            var validationService = _validationService;
            var logger = _logger;

            _ = Task.Run(async () =>
            {
                var progress = ActiveJobs[jobId];

                try
                {
                    var matcherLookup = RuleImportExportService.BuildMatcherLookup(cacheService.GetAll());

                    for (var index = 0; index < entries.Count; index++)
                    {
                        var entry = entries[index];

                        try
                        {
                            var (mappedRequest, validationError) = RuleImportExportService.ValidateImportEntry(
                                entry, validationService);

                            if (validationError != null)
                            {
                                progress.Errors.Add($"Entry {index}: {validationError}");
                                progress.Processed++;

                                continue;
                            }

                            var existingRule = RuleImportExportService.ResolveExistingRule(entry, cacheService, matcherLookup);

                            if (existingRule != null
                                && !RuleImportExportService.HasImportChanges(existingRule, mappedRequest!))
                            {
                                progress.Unchanged++;
                                progress.Processed++;

                                continue;
                            }

                            var rule = BuildImportRule(entry, mappedRequest!, existingRule);

                            if (existingRule != null)
                            {
                                await repository.UpdateAsync(rule);

                                progress.Updated++;
                            }
                            else
                            {
                                await repository.CreateAsync(rule);

                                progress.Imported++;
                            }

                            RuleImportExportService.AddToMatcherLookup(
                                matcherLookup,
                                rule,
                                existingRule?.Matcher);
                        }
                        catch (Exception exception)
                        {
                            progress.Errors.Add($"Entry {index}: {exception.Message}");
                        }

                        progress.Processed++;
                    }

                    var allRules = await repository.GetAllAsync();

                    cacheService.ReplaceAll(allRules);

                    progress.IsComplete = true;

                    logger.LogInformation(
                        "Import completed: {Imported} imported, {Updated} updated, {Unchanged} unchanged, {ErrorCount} errors. Job: {JobId}",
                        progress.Imported, progress.Updated, progress.Unchanged, progress.Errors.Count, jobId);
                }
                catch (Exception exception)
                {
                    progress.IsComplete = true;
                    progress.Error = exception.Message;

                    logger.LogError(exception, "Import failed. Job: {JobId}", jobId);
                }

                _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(t => ActiveJobs.TryRemove(jobId, out var removed));
            });

            return Ok(new { jobId, total = totalCount });
        }

        private static RedirectRule BuildImportRule(
            ImportRuleEntry entry,
            CreateRuleRequest mappedRequest,
            RedirectRule? existingRule)
        {
            var createdAt = !string.IsNullOrEmpty(entry.CreatedAt) && DateTimeOffset.TryParse(entry.CreatedAt, out var parsedCreatedAt)
                ? parsedCreatedAt
                : DateTimeOffset.UtcNow;

            Guid ruleId;

            if (existingRule != null)
            {
                ruleId = existingRule.Id;
            }
            else if (!string.IsNullOrEmpty(entry.Id) && Guid.TryParse(entry.Id, out var parsedId))
            {
                ruleId = parsedId;
            }
            else
            {
                ruleId = Guid.NewGuid();
            }

            return new RedirectRule
            {
                Id = ruleId,
                Matcher = mappedRequest.Matcher,
                TargetUrl = mappedRequest.TargetUrl ?? string.Empty,
                RedirectType = Enum.Parse<RedirectType>(mappedRequest.RedirectType, ignoreCase: true),
                Source = existingRule?.Source ?? RuleSource.Import,
                InfoText = mappedRequest.InfoText ?? string.Empty,
                AutoRedirect = mappedRequest.AutoRedirect,
                DiscardQueryParams = mappedRequest.DiscardQueryParams,
                ForwardQueryParams = mappedRequest.ForwardQueryParams,
                KeptQueryParams = mappedRequest.KeptQueryParams,
                StaticQueryParams = mappedRequest.StaticQueryParams,
                SearchAndReplace = mappedRequest.SearchAndReplace,
                CreatedAt = existingRule?.CreatedAt ?? createdAt
            };
        }

        [HttpGet("export")]
        public IActionResult Export([FromQuery] string format = "json")
        {
            var allRules = _cacheService.GetAll();

            switch (format.ToLowerInvariant())
            {
                case "csv":
                    {
                        var csvBytes = RuleImportExportService.GenerateCsv(allRules);

                        return File(csvBytes, "text/csv", "rules.csv");
                    }

                case "xlsx":
                    {
                        var xlsxBytes = RuleImportExportService.GenerateXlsx(allRules);

                        return File(
                            xlsxBytes,
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            "rules.xlsx");
                    }

                default:
                    {
                        var json = JsonSerializer.Serialize(allRules, CamelCaseOptions);

                        return File(
                            System.Text.Encoding.UTF8.GetBytes(json),
                            "application/json",
                            "rules.json");
                    }
            }
        }

        [HttpPost("validate")]
        public IActionResult Validate([FromBody] ValidateUrlsRequest request)
        {
            if (request.Urls == null || request.Urls.Count == 0)
            {
                return BadRequest(new { error = "At least one URL is required" });
            }

            const int maxUrls = 500;

            if (request.Urls.Count > maxUrls)
            {
                return BadRequest(new { error = $"Maximum {maxUrls} URLs per request" });
            }

            var matchingConfig = RuleMatchingConfigFactory.Create(_options);
            var appSettings = _settingsCache.GetSettings();
            var results = new List<ValidateUrlResult>();
            var matchedCount = 0;

            foreach (var url in request.Urls)
            {
                var trimmedUrl = url?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(trimmedUrl))
                {
                    continue;
                }

                try
                {
                    var matchResult = _ruleMatchingService.ResolveMatch(trimmedUrl, matchingConfig);

                    if (matchResult != null)
                    {
                        var (resolvedUrl, trace) = _urlTransformService.ResolveTargetUrlWithTrace(
                            trimmedUrl,
                            matchResult.Rule,
                            appSettings.DefaultNewDomain);

                        trace.Insert(1, new UrlTraceStep
                        {
                            Type = "rule-match",
                            Description = $"Matched rule '{matchResult.Rule.Matcher}' ({matchResult.Rule.RedirectType}, score: {matchResult.Score}, quality: {matchResult.Quality}%)",
                            After = matchResult.Rule.TargetUrl
                        });

                        matchedCount++;

                        results.Add(new ValidateUrlResult
                        {
                            Url = trimmedUrl,
                            Matched = true,
                            RuleId = matchResult.Rule.Id,
                            Matcher = matchResult.Rule.Matcher,
                            RedirectType = matchResult.Rule.RedirectType.ToString().ToLowerInvariant(),
                            Score = matchResult.Score,
                            Quality = matchResult.Quality,
                            Level = matchResult.Level,
                            ResolvedUrl = resolvedUrl,
                            Trace = trace
                        });
                    }
                    else
                    {
                        results.Add(new ValidateUrlResult
                        {
                            Url = trimmedUrl,
                            Matched = false,
                            Level = MatchQualityLevel.Red
                        });
                    }
                }
                catch (Exception exception)
                {
                    results.Add(new ValidateUrlResult
                    {
                        Url = trimmedUrl,
                        Matched = false,
                        Level = MatchQualityLevel.Red,
                        Error = exception.Message
                    });
                }
            }

            _logger.LogInformation(
                "Validate: {Total} URLs processed, {Matched} matched, {Unmatched} unmatched",
                results.Count,
                matchedCount,
                results.Count - matchedCount);

            return Ok(new ValidateUrlsResponse
            {
                Total = results.Count,
                Matched = matchedCount,
                Unmatched = results.Count - matchedCount,
                Results = results
            });
        }

        private static bool TryParseRedirectType(string value, out RedirectType redirectType)
        {
            return RuleImportExportService.TryParseRedirectType(value, out redirectType);
        }

    }
}
