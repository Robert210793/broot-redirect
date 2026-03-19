using Broot.Redirect.API.Dtos.Broot.Redirect.API.Dtos;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Broot.Redirect.API.Controllers
{
    [ApiController]
    [Route("api/global-rules")]
    public class GlobalRulesController : ControllerBase
    {
        private readonly IGlobalRuleRepository _repository;
        private readonly IGlobalRuleCacheService _cacheService;
        private readonly ILogger<GlobalRulesController> _logger;

        public GlobalRulesController(
            IGlobalRuleRepository repository,
            IGlobalRuleCacheService cacheService,
            ILogger<GlobalRulesController> logger)
        {
            _repository = repository;
            _cacheService = cacheService;
            _logger = logger;
        }

        /// <summary>
        /// GET /api/global-rules
        /// Returns all global rules ordered by priority (ascending).
        /// Admin-protected.
        /// </summary>
        /// 
        [HttpGet]
        public IActionResult GetAll()
        {
            var rules = _cacheService.GetAll();

            return Ok(rules);
        }

        /// <summary>
        /// GET /api/global-rules/{id}
        /// Returns a single global rule by ID from cache.
        /// Admin-protected.
        /// </summary>
        /// 
        [HttpGet("{id:guid}")]
        public IActionResult GetById(Guid id)
        {
            var rule = _cacheService.GetById(id);

            if (rule == null)
            {
                return NotFound(new { error = "Global rule not found" });
            }

            return Ok(rule);
        }

        /// <summary>
        /// POST /api/global-rules
        /// Creates a new global rule.
        /// Writes to Table Storage first, then updates cache.
        /// Admin-protected.
        /// </summary>
        /// 
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateGlobalRuleRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { error = "Invalid global rule data" });
            }

            var rule = new GlobalRule
            {
                Id = Guid.NewGuid(),
                Search = request.Search,
                Replace = request.Replace,
                CaseSensitive = request.CaseSensitive,
                Priority = request.Priority,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _repository.CreateAsync(rule);

            _cacheService.AddRule(rule);

            _logger.LogInformation(
                "Created global rule {RuleId} with search '{Search}', priority {Priority}",
                rule.Id,
                rule.Search,
                rule.Priority);

            return CreatedAtAction(nameof(GetById), new { id = rule.Id }, rule);
        }

        /// <summary>
        /// PUT /api/global-rules/{id}
        /// Partial update -- merges provided fields into existing global rule.
        /// Writes to Table Storage first, then updates cache.
        /// Admin-protected.
        /// </summary>
        /// 
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGlobalRuleRequest request)
        {
            var existing = _cacheService.GetById(id);

            if (existing == null)
            {
                return NotFound(new { error = "Global rule not found" });
            }

            var updated = new GlobalRule
            {
                Id = existing.Id,
                Search = request.Search ?? existing.Search,
                Replace = request.Replace ?? existing.Replace,
                CaseSensitive = request.CaseSensitive ?? existing.CaseSensitive,
                Priority = request.Priority ?? existing.Priority,
                CreatedAt = existing.CreatedAt
            };

            await _repository.UpdateAsync(updated);

            _cacheService.UpdateRule(updated);

            _logger.LogInformation("Updated global rule {RuleId}", id);

            return Ok(updated);
        }

        /// <summary>
        /// DELETE /api/global-rules/{id}
        /// Deletes a single global rule. Returns 204 on success, 404 if not found.
        /// Admin-protected.
        /// </summary>
        /// 
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var existing = _cacheService.GetById(id);

            if (existing == null)
            {
                return NotFound(new { error = "Global rule not found" });
            }

            await _repository.DeleteAsync(id);

            _cacheService.RemoveRule(id);

            _logger.LogInformation("Deleted global rule {RuleId}", id);

            return NoContent();
        }
    }
}
