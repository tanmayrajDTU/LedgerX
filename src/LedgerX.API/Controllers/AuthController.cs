using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LedgerX.Core.Entities;
using LedgerX.Infrastructure.Data;
using LedgerX.Infrastructure.Services;

namespace LedgerX.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly LedgerXDbContext _dbContext;
        private readonly JwtTokenService _jwtTokenService;

        public AuthController(LedgerXDbContext dbContext, JwtTokenService jwtTokenService)
        {
            _dbContext = dbContext;
            _jwtTokenService = jwtTokenService;
        }

        public class RegisterDto
        {
            public string Username { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            {
                return BadRequest(new { Message = "Username, email, and password are required." });
            }

            if (await _dbContext.Users.AnyAsync(u => u.Username.ToLower() == dto.Username.ToLower()))
            {
                return BadRequest(new { Message = "Username is already taken." });
            }

            if (await _dbContext.Users.AnyAsync(u => u.Email.ToLower() == dto.Email.ToLower()))
            {
                return BadRequest(new { Message = "Email is already registered." });
            }

            var user = new User
            {
                Username = dto.Username,
                Email = dto.Email,
                PasswordHash = PasswordHasher.HashPassword(dto.Password),
                Role = "User" // Default role is User. Admin is seeded or set in DB
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            return Ok(new { Message = "Registration successful. You can now login." });
        }

        public class LoginDto
        {
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == dto.Username.ToLower());
            if (user == null || !PasswordHasher.VerifyPassword(dto.Password, user.PasswordHash))
            {
                return Unauthorized(new { Message = "Invalid username or password." });
            }

            var accessToken = _jwtTokenService.GenerateAccessToken(user);
            var refreshTokenValue = _jwtTokenService.GenerateRefreshToken();

            var refreshToken = new RefreshToken
            {
                UserId = user.Id,
                Token = refreshTokenValue,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };

            _dbContext.RefreshTokens.Add(refreshToken);
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                AccessToken = accessToken,
                RefreshToken = refreshTokenValue,
                Username = user.Username,
                Role = user.Role,
                UserId = user.Id
            });
        }

        public class RefreshTokenDto
        {
            public string RefreshToken { get; set; } = string.Empty;
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenDto dto)
        {
            var token = await _dbContext.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.Token == dto.RefreshToken);

            if (token == null || !token.IsActive)
            {
                return BadRequest(new { Message = "Invalid or expired refresh token." });
            }

            // Revoke current token
            token.RevokedAt = DateTime.UtcNow;

            // Generate new pair
            var user = token.User!;
            var newAccessToken = _jwtTokenService.GenerateAccessToken(user);
            var newRefreshTokenValue = _jwtTokenService.GenerateRefreshToken();

            var newRefreshToken = new RefreshToken
            {
                UserId = user.Id,
                Token = newRefreshTokenValue,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };

            _dbContext.RefreshTokens.Add(newRefreshToken);
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshTokenValue
            });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenDto dto)
        {
            var token = await _dbContext.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == dto.RefreshToken);
            if (token != null)
            {
                token.RevokedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }

            return Ok(new { Message = "Logged out successfully." });
        }
    }
}
