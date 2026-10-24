using Broot.Redirect.API.Dtos;
using Broot.Redirect.API.Filters;
using Broot.Redirect.API.Services;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Broot.Redirect.API.Controllers
{
    [Route("api/qm-pro")]
    [ServiceFilter(typeof(QmProApiKeyFilter))]
    public class QmProController : ControllerBase
    {
        public const int CacheNotReadyRetryAfterSeconds = 5;

        private readonly QmProRuleService _ruleService;
        private readonly IRuleCacheService _cacheService;

        public QmProController(QmProRuleService ruleService, IRuleCacheService cacheService)
        {
            _ruleService = ruleService;
            _cacheService = cacheService;
        }

        [HttpPost("publish")]
        public async Task<IActionResult> Publish([FromBody] QmProPublishRequest? request, CancellationToken cancellationToken)
        {
            if (!_cacheService.IsWarmedUp)
            {
                return CacheNotReady();
            }

            if (request == null || !ModelState.IsValid)
            {
                return ValidationError("Ungültiger Request-Body");
            }

            var result = await _ruleService.PublishAsync(request, cancellationToken);

            return result.Status switch
            {
                QmProPublishStatus.Created => StatusCode(StatusCodes.Status201Created, RuleResponse(result.Rule, "created")),
                QmProPublishStatus.Updated => Ok(RuleResponse(result.Rule, "updated")),
                QmProPublishStatus.Unchanged => Ok(RuleResponse(result.Rule, "unchanged")),
                QmProPublishStatus.MatcherDuplicate => MatcherConflict(result, "MATCHER_DUPLICATE"),
                QmProPublishStatus.MatcherConflict => MatcherConflict(result, "MATCHER_CONFLICT"),
                _ => ValidationError(result.Error)
            };
        }

        [HttpPost("unpublish")]
        public async Task<IActionResult> Unpublish([FromBody] QmProUnpublishRequest? request, CancellationToken cancellationToken)
        {
            if (!_cacheService.IsWarmedUp)
            {
                return CacheNotReady();
            }

            if (request == null || !ModelState.IsValid)
            {
                return ValidationError("Ungültiger Request-Body");
            }

            var result = await _ruleService.UnpublishAsync(request, cancellationToken);

            return result.Status switch
            {
                QmProUnpublishStatus.Deactivated => Ok(RuleResponse(result.Rule, "deactivated")),
                QmProUnpublishStatus.AlreadyInactive => Ok(RuleResponse(result.Rule, "alreadyInactive")),
                QmProUnpublishStatus.NotFound => Ok(RuleResponse(null, "notFound")),
                _ => ValidationError(result.Error)
            };
        }

        private static QmProRuleResponse RuleResponse(RedirectRule? rule, string result)
        {
            return new QmProRuleResponse
            {
                RuleId = rule?.Id,
                Result = result
            };
        }

        private IActionResult CacheNotReady()
        {
            Response.Headers.RetryAfter = CacheNotReadyRetryAfterSeconds.ToString();

            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "Die Regeln werden noch geladen. Bitte später erneut versuchen.",
                code = "CACHE_NOT_READY"
            });
        }

        private IActionResult ValidationError(string? message)
        {
            return BadRequest(new
            {
                error = message ?? "Ungültige Eingabe",
                code = "VALIDATION_ERROR"
            });
        }

        private IActionResult MatcherConflict(QmProPublishResult result, string code)
        {
            return Conflict(new
            {
                error = result.Error,
                code,
                conflictingRule = result.ConflictingRule == null
                    ? null
                    : new
                    {
                        id = result.ConflictingRule.Id,
                        matcher = result.ConflictingRule.Matcher
                    }
            });
        }
    }
}
