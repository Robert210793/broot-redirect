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
        private readonly BrootRedirectOptions _options;
        private readonly ILogger<RulesController> _logger;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, JobProgress> ActiveJobs = new();

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
            IOptions<BrootRedirectOptions> options,
            ILogger<RulesController> logger)
        {
            _repository = repository;
            _cacheService = cacheService;
            _ruleMatchingService = ruleMatchingService;
            _urlTransformService = urlTransformService;
            _settingsCache = settingsCache;
            _options = options.Value;
            _logger = logger;
        }

        /// <summary>
        /// GET /api/rules?page=1&amp;limit=50&amp;search=&amp;sortBy=createdAt&amp;sortOrder=desc
        /// Returns paginated rule list from cache with server-side search and sort.
        /// </summary>
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
            var filteredRules = allRules.AsEnumerable();

            var cleanSearch = search?.Trim();

            if (!string.IsNullOrEmpty(cleanSearch))
            {
                filteredRules = filteredRules.Where(rule =>
                    (rule.Matcher?.Contains(cleanSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (rule.TargetUrl?.Contains(cleanSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (rule.InfoText?.Contains(cleanSearch, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            filteredRules = sortBy.ToLowerInvariant() switch
            {
                "matcher" => sortOrder == "asc"
                    ? filteredRules.OrderBy(rule => rule.Matcher, StringComparer.OrdinalIgnoreCase)
                    : filteredRules.OrderByDescending(rule => rule.Matcher, StringComparer.OrdinalIgnoreCase),

                "targeturl" => sortOrder == "asc"
                    ? filteredRules.OrderBy(rule => rule.TargetUrl, StringComparer.OrdinalIgnoreCase)
                    : filteredRules.OrderByDescending(rule => rule.TargetUrl, StringComparer.OrdinalIgnoreCase),

                "redirecttype" => sortOrder == "asc"
                    ? filteredRules.OrderBy(rule => rule.RedirectType.ToString())
                    : filteredRules.OrderByDescending(rule => rule.RedirectType.ToString()),

                _ => sortOrder == "asc"
                    ? filteredRules.OrderBy(rule => rule.CreatedAt)
                    : filteredRules.OrderByDescending(rule => rule.CreatedAt)
            };

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

        /// <summary>
        /// GET /api/rules/{id}
        /// Returns a single rule by ID from cache.
        /// </summary>
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

        /// <summary>
        /// POST /api/rules
        /// Creates a new redirect rule. Validates duplicate matcher.
        /// Writes to Table Storage first, then updates cache.
        /// </summary>
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

            if (_cacheService.MatcherExists(request.Matcher))
            {
                return BadRequest(new { error = $"A rule with matcher '{request.Matcher}' already exists" });
            }

            var rule = new RedirectRule
            {
                Id = Guid.NewGuid(),
                Matcher = request.Matcher,
                TargetUrl = request.TargetUrl,
                RedirectType = redirectType,
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

        /// <summary>
        /// PUT /api/rules/{id}
        /// Updates an existing redirect rule with partial update semantics.
        /// Validates duplicate matcher if changed.
        /// Writes to Table Storage first, then updates cache.
        /// </summary>
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRuleRequest request)
        {
            var existingRule = _cacheService.GetById(id);

            if (existingRule == null)
            {
                return NotFound(new { error = "Rule not found" });
            }

            if (request.Matcher != null && request.Matcher != existingRule.Matcher)
            {
                if (_cacheService.MatcherExists(request.Matcher, excludeRuleId: id))
                {
                    return BadRequest(new { error = $"A rule with matcher '{request.Matcher}' already exists" });
                }
            }

            var updatedRule = new RedirectRule
            {
                Id = existingRule.Id,
                Matcher = request.Matcher ?? existingRule.Matcher,
                TargetUrl = request.TargetUrl ?? existingRule.TargetUrl,
                RedirectType = existingRule.RedirectType,
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

        /// <summary>
        /// DELETE /api/rules/{id}
        /// Deletes a single rule. Returns 204 on success, 404 if not found.
        /// </summary>
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

        /// <summary>
        /// DELETE /api/rules/all
        /// Starts deleting all rules in the background. Returns a job ID for polling progress.
        /// </summary>
        [HttpDelete("all")]
        public IActionResult DeleteAll()
        {
            var jobId = Guid.NewGuid().ToString("N");
            var allIds = _cacheService.GetAll().Select(r => r.Id).ToList();
            var totalCount = allIds.Count;

            _logger.LogWarning("DeleteAll requested. Rule count: {RuleCount}, Job: {JobId}", totalCount, jobId);

            ActiveJobs[jobId] = new JobProgress { Total = totalCount };

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

        /// <summary>
        /// GET /api/rules/jobs/{jobId}
        /// Returns progress for a long-running operation.
        /// </summary>
        [HttpGet("jobs/{jobId}")]
        public IActionResult GetJobProgress(string jobId)
        {
            if (!ActiveJobs.TryGetValue(jobId, out var progress))
            {
                return NotFound(new { error = "Job not found or expired" });
            }

            return Ok(new
            {
                processed = progress.Processed,
                total = progress.Total,
                isComplete = progress.IsComplete,
                imported = progress.Imported,
                updated = progress.Updated,
                errors = progress.Errors,
                error = progress.Error
            });
        }

        /// <summary>
        /// DELETE /api/rules/bulk
        /// Bulk deletes rules by ID array. Returns count of deleted and not found.
        /// </summary>
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

        /// <summary>
        /// POST /api/rules/import/preview
        /// Parses an uploaded file (JSON, CSV, XLSX) and compares each entry against
        /// existing rules in cache. Returns categorized results (new/update/invalid)
        /// without persisting anything.
        ///
        /// Accepts two content types:
        /// - application/json: JSON array of ImportRuleEntry
        /// - multipart/form-data: CSV or XLSX file upload (field name "file")
        /// </summary>
        [HttpPost("import/preview")]
        public async Task<IActionResult> ImportPreview()
        {
            List<ImportRuleEntry> entries;

            var contentType = Request.ContentType ?? string.Empty;

            if (contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            {
                var file = Request.Form.Files.FirstOrDefault();

                if (file == null || file.Length == 0)
                {
                    return BadRequest(new { error = "No file uploaded" });
                }

                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

                try
                {
                    using var stream = file.OpenReadStream();

                    entries = extension switch
                    {
                        ".csv" => RuleImportExportService.ParseCsv(stream),
                        ".xlsx" => RuleImportExportService.ParseXlsx(stream),
                        ".xls" => RuleImportExportService.ParseXlsx(stream),
                        ".json" => await ParseJsonFromStreamAsync(stream),
                        _ => throw new InvalidOperationException($"Unsupported file format: {extension}. Use .json, .csv, .xlsx, or .xls.")
                    };
                }
                catch (InvalidOperationException exception)
                {
                    return BadRequest(new { error = exception.Message });
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to parse preview file: {FileName}", file.FileName);

                    return BadRequest(new { error = $"Failed to parse file: {exception.Message}" });
                }
            }
            else
            {
                try
                {
                    entries = await ParseJsonFromStreamAsync(Request.Body);
                }
                catch (JsonException exception)
                {
                    return BadRequest(new { error = $"Invalid JSON: {exception.Message}" });
                }
            }

            if (entries.Count == 0)
            {
                return BadRequest(new { error = "No rules found in file" });
            }

            // Build lookup from existing rules for comparison
            var matcherLookup = _cacheService.GetAll()
                .GroupBy(rule => rule.Matcher, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

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

                // Validate required fields
                if (string.IsNullOrWhiteSpace(entry.Matcher))
                {
                    previewEntry.Status = "invalid";
                    previewEntry.Reason = "Matcher is required";
                    counts.Invalid++;

                    previewEntries.Add(previewEntry);

                    continue;
                }

                // Validate redirect type
                var redirectType = entry.RedirectType ?? "partial";

                if (!TryParseRedirectType(redirectType, out _))
                {
                    previewEntry.Status = "invalid";
                    previewEntry.Reason = $"Invalid redirect type: '{redirectType}'";
                    counts.Invalid++;

                    previewEntries.Add(previewEntry);

                    continue;
                }

                // Check if this is an update (by ID or matcher)
                RedirectRule? existingRule = null;

                if (!string.IsNullOrEmpty(entry.Id) && Guid.TryParse(entry.Id, out var parsedId))
                {
                    existingRule = _cacheService.GetById(parsedId);
                }

                if (existingRule == null)
                {
                    matcherLookup.TryGetValue(entry.Matcher, out existingRule);
                }

                if (existingRule != null)
                {
                    previewEntry.Status = "update";
                    previewEntry.ExistingRuleId = existingRule.Id.ToString();
                    counts.Update++;
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

        /// <summary>
        /// POST /api/rules/import
        /// Imports rules with upsert semantics.
        ///
        /// Accepts two content types:
        /// - application/json: JSON array of ImportRuleEntry (existing behavior)
        /// - multipart/form-data: CSV or XLSX file upload (field name "file")
        ///
        /// If a rule has an ID and exists: update. If ID not found: create with that ID.
        /// If no ID but matcher matches: update. If no ID and no matcher match: create new.
        /// After import, replaces entire cache to ensure consistency.
        /// </summary>
        [HttpPost("import")]
        public async Task<IActionResult> Import()
        {
            List<ImportRuleEntry> entries;

            var contentType = Request.ContentType ?? string.Empty;

            if (contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            {
                var file = Request.Form.Files.FirstOrDefault();

                if (file == null || file.Length == 0)
                {
                    return BadRequest(new { error = "No file uploaded" });
                }

                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

                try
                {
                    using var stream = file.OpenReadStream();

                    entries = extension switch
                    {
                        ".csv" => RuleImportExportService.ParseCsv(stream),
                        ".xlsx" => RuleImportExportService.ParseXlsx(stream),
                        ".xls" => RuleImportExportService.ParseXlsx(stream),
                        _ => throw new InvalidOperationException($"Unsupported file format: {extension}. Use .csv, .xlsx, or .xls.")
                    };
                }
                catch (InvalidOperationException exception)
                {
                    return BadRequest(new { error = exception.Message });
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to parse import file: {FileName}", file.FileName);

                    return BadRequest(new { error = $"Failed to parse file: {exception.Message}" });
                }
            }
            else
            {
                try
                {
                    entries = await JsonSerializer.DeserializeAsync<List<ImportRuleEntry>>(
                        Request.Body,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<ImportRuleEntry>();
                }
                catch (JsonException exception)
                {
                    return BadRequest(new { error = $"Invalid JSON: {exception.Message}" });
                }
            }

            if (entries.Count == 0)
            {
                return BadRequest(new { error = "No rules to import" });
            }

            var jobId = Guid.NewGuid().ToString("N");
            var totalCount = entries.Count;

            ActiveJobs[jobId] = new JobProgress { Total = totalCount };

            var repository = _repository;
            var cacheService = _cacheService;
            var logger = _logger;

            _ = Task.Run(async () =>
            {
                var progress = ActiveJobs[jobId];

                try
                {
                    var matcherLookup = cacheService.GetAll()
                        .GroupBy(r => r.Matcher, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                    for (var index = 0; index < entries.Count; index++)
                    {
                        var entry = entries[index];

                        try
                        {
                            if (string.IsNullOrWhiteSpace(entry.Matcher))
                            {
                                progress.Errors.Add($"Entry {index}: matcher is required");
                                progress.Processed++;

                                continue;
                            }

                            if (!TryParseRedirectType(entry.RedirectType ?? "partial", out var redirectType))
                            {
                                progress.Errors.Add($"Entry {index}: invalid redirect type '{entry.RedirectType}'");
                                progress.Processed++;

                                continue;
                            }

                            DateTimeOffset createdAt;

                            if (!string.IsNullOrEmpty(entry.CreatedAt) && DateTimeOffset.TryParse(entry.CreatedAt, out var parsedCreatedAt))
                            {
                                createdAt = parsedCreatedAt;
                            }
                            else
                            {
                                createdAt = DateTimeOffset.UtcNow;
                            }

                            RedirectRule? existingRule = null;
                            Guid ruleId;

                            if (!string.IsNullOrEmpty(entry.Id) && Guid.TryParse(entry.Id, out var parsedId))
                            {
                                existingRule = cacheService.GetById(parsedId);
                                ruleId = parsedId;
                            }
                            else
                            {
                                matcherLookup.TryGetValue(entry.Matcher, out existingRule);
                                ruleId = existingRule?.Id ?? Guid.NewGuid();
                            }

                            var rule = new RedirectRule
                            {
                                Id = ruleId,
                                Matcher = entry.Matcher,
                                TargetUrl = entry.TargetUrl,
                                RedirectType = redirectType,
                                InfoText = entry.InfoText,
                                AutoRedirect = entry.AutoRedirect ?? false,
                                DiscardQueryParams = entry.DiscardQueryParams ?? false,
                                ForwardQueryParams = entry.ForwardQueryParams ?? false,
                                KeptQueryParams = entry.KeptQueryParams ?? new List<KeptQueryParam>(),
                                StaticQueryParams = entry.StaticQueryParams ?? new List<StaticQueryParam>(),
                                SearchAndReplace = entry.SearchAndReplace ?? new List<SearchAndReplaceEntry>(),
                                CreatedAt = existingRule?.CreatedAt ?? createdAt
                            };

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
                        "Import completed: {Imported} imported, {Updated} updated, {ErrorCount} errors. Job: {JobId}",
                        progress.Imported, progress.Updated, progress.Errors.Count, jobId);
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

        /// <summary>
        /// GET /api/rules/export?format=json|csv|xlsx
        /// Exports all rules as a download.
        /// - json (default): JSON array with Content-Disposition: attachment
        /// - csv: flat CSV with header row
        /// - xlsx: Excel workbook with header row
        /// </summary>
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

        /// <summary>
        /// POST /api/rules/validate
        /// Runs an array of URLs through the matching and transformation pipeline,
        /// returning match details and resolved URLs for each. (Phase 5.1)
        /// </summary>
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
                        var resolvedUrl = _urlTransformService.ResolveTargetUrl(
                            trimmedUrl,
                            matchResult.Rule,
                            appSettings.DefaultNewDomain);

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
                            ResolvedUrl = resolvedUrl
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
            return Enum.TryParse(value, ignoreCase: true, out redirectType);
        }

        private static async Task<List<ImportRuleEntry>> ParseJsonFromStreamAsync(Stream stream)
        {
            return await JsonSerializer.DeserializeAsync<List<ImportRuleEntry>>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<ImportRuleEntry>();
        }

        private class JobProgress
        {
            public int Processed { get; set; }

            public int Total { get; set; }

            public int Imported { get; set; }

            public int Updated { get; set; }

            public bool IsComplete { get; set; }

            public string? Error { get; set; }

            public List<string> Errors { get; set; } = new();
        }
    }
}