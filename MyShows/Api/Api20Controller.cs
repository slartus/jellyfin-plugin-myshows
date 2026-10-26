using System;
using System.Net.Http;
using System.Net.Mime;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyShows.Configuration;

namespace MyShows.Api
{
    [ApiController]
    [Authorize]
    [Route("MyShows/v2")]
    [Produces(MediaTypeNames.Application.Json)]
    public class Api20Controller : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public Api20Controller(
            IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [HttpPost("login")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> LogIn(
            [FromForm] string id,
            [FromForm] string login,
            [FromForm] string password)
        {
            var targetUserId = CallerScope.ResolveTargetUserId(User, id);
            if (string.IsNullOrWhiteSpace(id) || targetUserId == null) return Forbid();

            try
            {
                var httpClient = GetHttpClient();
                var (token, error) = await OAuthHelper.GetToken(httpClient, login, password);

                if (error != null)
                {
                    return Ok(new
                    {
                        success = false,
                        statusText = error.error_description
                    });
                }

                Plugin.Instance.PluginConfiguration.AddUser(new UserConfig
                {
                    ApiVersion = MyShowsApi.MyShowsApiVersion.V20,
                    AccessToken = token.access_token,
                    RefreshToken = token.refresh_token,
                    ExpirationTime = DateTime.Now.AddSeconds(token.expires_in),
                    Id = targetUserId,
                    Name = login
                });
            }
            catch (HttpRequestException e)
            {
                return Ok(new
                {
                    success = false,
                    statusText = e.Message
                });
            }

            return Ok(new { success = true });
        }

        private HttpClient GetHttpClient()
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            return client;
        }
    }
}
