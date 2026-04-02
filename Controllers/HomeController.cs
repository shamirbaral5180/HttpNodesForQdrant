using HttpNodesForQdrant.Models;
using HttpNodesForQdrant.Services;
using Microsoft.AspNetCore.Mvc;

namespace HttpNodesForQdrant.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HomeController : ControllerBase
    {
        private readonly IQdrantService _qdrantService;
        private readonly IConfiguration _configuration;

        public HomeController(IQdrantService qdrantService, IConfiguration configuration)
        {
            _qdrantService = qdrantService;
            _configuration = configuration;
        }

        [HttpPost("store")]
        public async Task<IActionResult> StoreConversation(
            [FromBody] StoreConversationRequest request,
            [FromHeader(Name = "X-Api-Key")] string? apiKey,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!HasValidApiKey(apiKey))
                {
                    return Unauthorized(new { message = "Invalid API key." });
                }
                if (request.Timestamp == default)
                {
                    request.Timestamp = DateTimeOffset.UtcNow;
                }
                await _qdrantService.StoreConversationAsync(request, cancellationToken);
                return Ok(new { message = "Conversation stored." });
            }
            catch(Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("search")]
        public async Task<ActionResult<IReadOnlyList<ConversationSearchResult>>> SearchConversation(
            [FromBody] SearchConversationRequest request,
            [FromHeader(Name = "X-Api-Key")] string? apiKey,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!HasValidApiKey(apiKey))
                {
                    return Unauthorized(new { message = "Invalid API key." });
                }

                var results = await _qdrantService.SearchConversationsAsync(request, cancellationToken);
                return Ok(results);
            }
            catch(Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        private bool HasValidApiKey(string? apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return false;
            }

            var expectedApiKey = _configuration["ApiSecurity:ApiKey"];
            return !string.IsNullOrWhiteSpace(expectedApiKey)
                   && string.Equals(apiKey, expectedApiKey, StringComparison.Ordinal);
        }
    }
}
