using Broot.Redirect.API.Controllers;
using Broot.Redirect.API.Dtos.Broot.Redirect.API.Dtos;
using Broot.Redirect.Core.Interfaces;
using Broot.Redirect.Core.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Broot.Redirect.Tests.API.Controllers
{
    public class GlobalRulesControllerTests
    {
        private readonly IGlobalRuleRepository _repository;
        private readonly IGlobalRuleCacheService _cacheService;
        private readonly GlobalRulesController _sut;

        public GlobalRulesControllerTests()
        {
            _repository = Substitute.For<IGlobalRuleRepository>();
            _cacheService = Substitute.For<IGlobalRuleCacheService>();
            var logger = Substitute.For<ILogger<GlobalRulesController>>();
            _sut = new GlobalRulesController(_repository, _cacheService, logger);
        }

        [Fact]
        public void GetAll_ReturnsOkWithRules()
        {
            var rules = new List<GlobalRule>
            {
                new GlobalRule { Id = Guid.NewGuid(), Search = "old", Replace = "new" }
            };

            _cacheService.GetAll().Returns(rules);

            var result = _sut.GetAll() as OkObjectResult;

            result.Should().NotBeNull();
            (result!.Value as IReadOnlyList<GlobalRule>).Should().HaveCount(1);
        }

        [Fact]
        public void GetById_Exists_ReturnsOk()
        {
            var ruleId = Guid.NewGuid();
            var rule = new GlobalRule { Id = ruleId, Search = "old", Replace = "new" };

            _cacheService.GetById(ruleId).Returns(rule);

            var result = _sut.GetById(ruleId) as OkObjectResult;

            result.Should().NotBeNull();
            result!.Value.Should().Be(rule);
        }

        [Fact]
        public void GetById_NotFound_Returns404()
        {
            _cacheService.GetById(Arg.Any<Guid>()).Returns((GlobalRule?)null);

            var result = _sut.GetById(Guid.NewGuid());

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Fact]
        public async Task Create_ValidRequest_ReturnsCreated()
        {
            var request = new CreateGlobalRuleRequest
            {
                Search = "old",
                Replace = "new",
                CaseSensitive = false,
                Priority = 1
            };

            var result = await _sut.Create(request) as CreatedAtActionResult;

            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(201);

            await _repository.Received(1).CreateAsync(Arg.Any<GlobalRule>());

            _cacheService.Received(1).AddRule(Arg.Any<GlobalRule>());
        }

        [Fact]
        public async Task Update_Exists_ReturnsOk()
        {
            var ruleId = Guid.NewGuid();
            var existing = new GlobalRule { Id = ruleId, Search = "old", Replace = "new", Priority = 0 };

            _cacheService.GetById(ruleId).Returns(existing);

            var request = new UpdateGlobalRuleRequest { Search = "updated" };

            var result = await _sut.Update(ruleId, request) as OkObjectResult;

            result.Should().NotBeNull();

            await _repository.Received(1).UpdateAsync(Arg.Any<GlobalRule>());

            _cacheService.Received(1).UpdateRule(Arg.Any<GlobalRule>());
        }

        [Fact]
        public async Task Update_NotFound_Returns404()
        {
            _cacheService.GetById(Arg.Any<Guid>()).Returns((GlobalRule?)null);

            var result = await _sut.Update(Guid.NewGuid(), new UpdateGlobalRuleRequest());

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Fact]
        public async Task Delete_Exists_ReturnsNoContent()
        {
            var ruleId = Guid.NewGuid();
            var existing = new GlobalRule { Id = ruleId, Search = "old", Replace = "new" };

            _cacheService.GetById(ruleId).Returns(existing);

            var result = await _sut.Delete(ruleId);

            result.Should().BeOfType<NoContentResult>();

            await _repository.Received(1).DeleteAsync(ruleId);

            _cacheService.Received(1).RemoveRule(ruleId);
        }

        [Fact]
        public async Task Delete_NotFound_Returns404()
        {
            _cacheService.GetById(Arg.Any<Guid>()).Returns((GlobalRule?)null);

            var result = await _sut.Delete(Guid.NewGuid());

            result.Should().BeOfType<NotFoundObjectResult>();
        }
    }
}
