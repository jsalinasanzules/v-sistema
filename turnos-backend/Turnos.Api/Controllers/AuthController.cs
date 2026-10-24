using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turnos.Api.Services;

namespace Turnos.Api.Controllers;
[ApiController, Route("api/auth")]
public sealed class AuthController(AuthService auth) : ControllerBase
{
    public sealed record LoginRequest(string Username,string Password);
    [AllowAnonymous, HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request) {
        try { return Ok(await auth.LoginAsync(request.Username,request.Password,HttpContext.Connection.RemoteIpAddress?.ToString(),Request.Headers.UserAgent.ToString())); }
        catch(UnauthorizedAccessException ex) { return Unauthorized(new{message=ex.Message}); }
    }
    [Authorize, HttpGet("me")]
    public IActionResult Me() => Ok(new { id=User.FindFirst("uid")?.Value, username=User.FindFirst("sub")?.Value, role=User.FindFirst("role")?.Value, name=User.FindFirst("name")?.Value });
}