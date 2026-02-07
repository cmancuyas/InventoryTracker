using InventoryTracker.Api.Auth;
using InventoryTracker.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;

namespace InventoryTracker.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly IJwtTokenService _jwt;
    private readonly JwtOptions _jwtOptions;

    public AuthController(
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        IJwtTokenService jwt,
        Microsoft.Extensions.Options.IOptions<JwtOptions> jwtOptions)
    {
        _users = users;
        _signIn = signIn;
        _jwt = jwt;
        _jwtOptions = jwtOptions.Value;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest("Email and password are required.");

        var user = await _users.FindByEmailAsync(req.Email);
        if (user is null)
            return Unauthorized("Invalid credentials.");

        var result = await _signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return Unauthorized("Invalid credentials.");

        var roles = (await _users.GetRolesAsync(user)).ToList();
        var token = _jwt.CreateToken(user, roles);

        return Ok(new LoginResponse(
            AccessToken: token,
            ExpiresInMinutes: _jwtOptions.ExpiresMinutes,
            UserId: user.Id,
            Email: user.Email ?? req.Email,
            Roles: roles
        ));
    }
}
