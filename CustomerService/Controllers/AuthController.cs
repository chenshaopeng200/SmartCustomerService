using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace CustomerService.Controllers;

[ApiController]
[Route("api/customer/auth")]
public class AuthController : ControllerBase
{
    private readonly string _secret;
    private readonly int _expireHours;
    private readonly ILogger<AuthController> _logger;

    // Demo users — replace with real user store in production
    private static readonly Dictionary<string, string> _users = new()
    {
        ["admin"] = "admin123",
        ["agent"] = "agent123",
        ["viewer"] = "viewer123"
    };

    public AuthController(IConfiguration config, ILogger<AuthController> logger)
    {
        _secret = config["Jwt:Secret"] ?? "SmartCustomerServiceSecretKey2024!@#$%";
        _expireHours = int.Parse(config["Jwt:ExpireHours"] ?? "24");
        _logger = logger;
    }

    [HttpPost("login")]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new LoginResponse { Success = false, Message = "用户名和密码不能为空。" });

        if (!_users.TryGetValue(request.Username, out var storedPwd) || storedPwd != request.Password)
            return Unauthorized(new LoginResponse { Success = false, Message = "用户名或密码错误。" });

        var token = GenerateToken(request.Username);
        _logger.LogInformation("User {Username} logged in", request.Username);

        return new LoginResponse
        {
            Success = true,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddHours(_expireHours),
            Username = request.Username
        };
    }

    private string GenerateToken(string username)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, username),
            new Claim(ClaimTypes.Name, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: "SmartCustomerService",
            audience: "SmartCustomerService",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(_expireHours),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string Username { get; set; } = string.Empty;
}
