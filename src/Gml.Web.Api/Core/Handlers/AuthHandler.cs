using System.Net;
using System.Security.Claims;
using AutoMapper;
using FluentValidation;
using Gml.Domains.Auth;
using Gml.Domains.Repositories;
using Gml.Dto.Auth;
using Gml.Dto.Messages;
using Gml.Dto.Player;
using Gml.Dto.User;
using Gml.Web.Api.Core.Options;
using Gml.Web.Api.Data;
using GmlCore.Interfaces;
using GmlCore.Interfaces.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Gml.Web.Api.Core.Handlers;

public class AuthHandler : IAuthHandler
{
    // Absolute grace window during which a refresh token that was just rotated out can still be
    // presented once more (e.g. by a second concurrent tab/request) without forcing a full re-login.
    private static readonly TimeSpan RefreshReuseGraceWindow = TimeSpan.FromSeconds(10);

    private sealed record CachedRefresh(AuthTokensDto Tokens, string RefreshToken, DateTime ExpiresAtUtc);

    private static CookieOptions BuildRefreshCookieOptions(HttpContext httpContext, DateTime? expiresAtUtc = null)
    {
        var isHttps = httpContext.Request.IsHttps;

        return new CookieOptions
        {
            HttpOnly = true,
            // "Secure" cookies are refused by browsers on a non-HTTPS origin, and SameSite=None requires
            // Secure — so on plain-HTTP self-hosted deployments the cookie must fall back to Lax or it is
            // silently never stored, breaking silent refresh right after a successful login.
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/",
            Expires = expiresAtUtc
        };
    }

    // Same shape as the refresh cookie — kept as a separate method (rather than a shared one
    // with a name param) so each call site stays obviously readable about which cookie it's building.
    private static CookieOptions BuildAccessCookieOptions(HttpContext httpContext, DateTime expiresAtUtc)
    {
        var isHttps = httpContext.Request.IsHttps;

        return new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/",
            Expires = expiresAtUtc
        };
    }

    // Mirrors exactly the claims AccessTokenService puts in the JWT itself (see
    // AccessTokenService.GenerateAccessTokenCore) — this is plain JSON so a browser client that
    // only ever sees the token as an httpOnly cookie can still read who's signed in.
    private static Dictionary<string, object> BuildProfileClaims(int userId, string? login, string? email,
        IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions, DateTime accessTokenExpiresAtUtc)
    {
        var profile = new Dictionary<string, object>
        {
            ["sub"] = userId.ToString(),
            [ClaimTypes.NameIdentifier] = userId.ToString(),
            ["perm"] = permissions,
            ["exp"] = new DateTimeOffset(accessTokenExpiresAtUtc).ToUnixTimeSeconds()
        };

        if (!string.IsNullOrWhiteSpace(login)) profile[ClaimTypes.Name] = login;
        if (!string.IsNullOrWhiteSpace(email)) profile[ClaimTypes.Email] = email;

        // A JWT with more than one claim of the same type serializes that claim as a JSON array
        // instead of a scalar string — match that exactly so this looks identical to decoding the
        // real token, since the web client's TypeScript types assume that shape.
        if (roles.Count == 1) profile[ClaimTypes.Role] = roles.First();
        else if (roles.Count > 1) profile[ClaimTypes.Role] = roles;

        return profile;
    }

    public static async Task<IResult> Logout(
        HttpContext httpContext,
        IAccessTokenService tokenService,
        IRefreshTokenRepository refreshRepo)
    {
        var refreshToken = httpContext.Request.Cookies["refreshToken"];
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var hash = tokenService.HashRefreshToken(refreshToken);
            var stored = await refreshRepo.FindActiveByHashAsync(hash);
            if (stored is not null)
            {
                await refreshRepo.RevokeAsync(stored.UserId, stored.TokenHash);
            }
        }

        httpContext.Response.Cookies.Delete("refreshToken", BuildRefreshCookieOptions(httpContext));
        httpContext.Response.Cookies.Delete("accessToken", BuildAccessCookieOptions(httpContext, DateTime.UtcNow));

        return Results.Ok(ResponseMessage.Create("Вы вышли из системы", HttpStatusCode.OK));
    }

    public static async Task<IResult> CreateUser(
        HttpContext httpContext,
        IUserRepository userRepository,
        IValidator<UserCreateDto> validator,
        IMapper mapper,
        UserCreateDto createDto,
        ApplicationContext appContext,
        IAccessTokenService tokenService,
        IRefreshTokenRepository refreshRepo,
        ServerSettings settings,
        DatabaseContext db)
    {
        if (!appContext.Settings.RegistrationIsEnabled && !httpContext.User.IsInRole("Admin"))
            return Results.BadRequest(ResponseMessage.Create("Регистрация для новых пользователей запрещена",
                HttpStatusCode.BadRequest));

        var adminRoleEntity = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
        var hasAdmins = adminRoleEntity != null && await db.UserRoles.AnyAsync(ur => ur.RoleId == adminRoleEntity.Id);

        var result = await validator.ValidateAsync(createDto);

        if (!result.IsValid)
            return Results.BadRequest(ResponseMessage.Create(result.Errors, "Ошибка валидации",
                HttpStatusCode.BadRequest));

        var existing = await userRepository.CheckExistUser(createDto.Login, createDto.Email);

        if (existing is not null)
            return Results.BadRequest(ResponseMessage.Create("Пользователь с указанными данными уже существует",
                HttpStatusCode.BadRequest));

        string targetRoleName;
        if (!hasAdmins)
        {
            if (adminRoleEntity == null)
            {
                adminRoleEntity = new Role { Name = "Admin", Description = "System administrator" };
                db.Roles.Add(adminRoleEntity);
                await db.SaveChangesAsync();
            }

            targetRoleName = "Admin";
        }
        else
        {
            var requestedRole = httpContext.Request.Query.TryGetValue("role", out var roleVals)
                ? roleVals.ToString()
                : null;
            targetRoleName = string.IsNullOrWhiteSpace(requestedRole) ? "Admin" : requestedRole.Trim();
        }

        var targetRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == targetRoleName);
        if (targetRole == null)
        {
            return Results.BadRequest(ResponseMessage.Create($"Роль '{targetRoleName}' не найдена",
                HttpStatusCode.BadRequest));
        }

        var user = await userRepository.CreateUser(createDto.Email, createDto.Login, createDto.Password);

        var hasLink = await db.UserRoles.AnyAsync(x => x.UserId == user.Id && x.RoleId == targetRole.Id);
        if (!hasLink)
        {
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = targetRole.Id });
            await db.SaveChangesAsync();
        }

        var roles = await db.UserRoles.Where(ur => ur.UserId == user.Id).Select(ur => ur.Role.Name).ToListAsync();
        var permissions = await db.RolePermissions
            .Where(rp => db.UserRoles.Where(ur => ur.UserId == user.Id).Select(ur => ur.RoleId).Contains(rp.RoleId))
            .Select(rp => rp.Permission.Name)
            .Distinct()
            .ToListAsync();

        var accessToken = tokenService.GenerateAccessToken(user.Id, user.Login, user.Email, roles, permissions);
        var refreshToken = tokenService.GenerateRefreshToken();
        var refreshHash = tokenService.HashRefreshToken(refreshToken);
        var expiresAt = DateTime.UtcNow.AddDays(settings.RefreshTokenDays);
        var accessExpiresAt = DateTime.UtcNow.AddMinutes(settings.AccessTokenMinutes);
        await refreshRepo.CreateAsync(user.Id, refreshHash, expiresAt);

        httpContext.Response.Cookies.Append("refreshToken", refreshToken, BuildRefreshCookieOptions(httpContext, expiresAt));
        httpContext.Response.Cookies.Append("accessToken", accessToken, BuildAccessCookieOptions(httpContext, accessExpiresAt));

        var tokens = new AuthTokensDto
        {
            AccessToken = accessToken,
            ExpiresIn = settings.AccessTokenMinutes * 60,
            Profile = BuildProfileClaims(user.Id, user.Login, user.Email, roles, permissions, accessExpiresAt)
        };

        return Results.Ok(ResponseMessage.Create(tokens, "Успешная регистрация",
            HttpStatusCode.OK));
    }

    public static async Task<IResult> UserInfo(IGmlManager manager, IMapper mapper, string userName)
    {
        var user = await manager.Users.GetUserByName(userName);

        if (user is null)
        {
            return Results.NotFound(ResponseMessage.Create("Пользователь не найден", HttpStatusCode.BadRequest));
        }

        return Results.Ok(ResponseMessage.Create(mapper.Map<PlayerTextureDto>(user), "Успешная авторизация",
            HttpStatusCode.OK));
    }

    public static async Task<IResult> AuthUser(
        HttpContext httpContext,
        IUserRepository userRepository,
        IValidator<UserAuthDto> validator,
        IMapper mapper,
        UserAuthDto authDto,
        IAccessTokenService tokenService,
        IRefreshTokenRepository refreshRepo,
        ServerSettings settings,
        DatabaseContext db)
    {
        var result = await validator.ValidateAsync(authDto);

        if (!result.IsValid)
            return Results.BadRequest(ResponseMessage.Create(result.Errors, "Ошибка валидации",
                HttpStatusCode.BadRequest));

        var user = await userRepository.GetUser(authDto.Login, authDto.Password);

        if (user is null)
            return Results.BadRequest(ResponseMessage.Create("Неверный логин или пароль",
                HttpStatusCode.BadRequest));

        // Compute roles/perms for JWT
        var rolesSignin = await db.UserRoles.Where(ur => ur.UserId == user.Id).Select(ur => ur.Role.Name).ToListAsync();
        var permsSignin = await db.RolePermissions
            .Where(rp => db.UserRoles.Where(ur => ur.UserId == user.Id).Select(ur => ur.RoleId).Contains(rp.RoleId))
            .Select(rp => rp.Permission.Name)
            .Distinct()
            .ToListAsync();
        var accessToken = tokenService.GenerateAccessToken(user.Id, user.Login, user.Email, rolesSignin, permsSignin);
        var refreshToken = tokenService.GenerateRefreshToken();
        var refreshHash = tokenService.HashRefreshToken(refreshToken);
        var expiresAt = DateTime.UtcNow.AddDays(settings.RefreshTokenDays);
        var accessExpiresAt = DateTime.UtcNow.AddMinutes(settings.AccessTokenMinutes);
        await refreshRepo.CreateAsync(user.Id, refreshHash, expiresAt);

        httpContext.Response.Cookies.Append("refreshToken", refreshToken, BuildRefreshCookieOptions(httpContext, expiresAt));
        httpContext.Response.Cookies.Append("accessToken", accessToken, BuildAccessCookieOptions(httpContext, accessExpiresAt));

        var tokens = new AuthTokensDto
        {
            AccessToken = accessToken,
            ExpiresIn = settings.AccessTokenMinutes * 60,
            Profile = BuildProfileClaims(user.Id, user.Login, user.Email, rolesSignin, permsSignin, accessExpiresAt)
        };

        return Results.Ok(ResponseMessage.Create(tokens, "Успешная авторизация",
            HttpStatusCode.OK));
    }

    public static async Task<IResult> RefreshTokens(
        HttpContext httpContext,
        IAccessTokenService tokenService,
        IRefreshTokenRepository refreshRepo,
        ServerSettings settings,
        DatabaseContext db,
        IMemoryCache cache)
    {
        var refreshToken = httpContext.Request.Cookies["refreshToken"];
        if (string.IsNullOrWhiteSpace(refreshToken))
            return Results.Unauthorized();

        var hash = tokenService.HashRefreshToken(refreshToken);
        var cacheKey = $"refresh-reuse:{hash}";

        var stored = await refreshRepo.FindActiveByHashAsync(hash);
        if (stored is null)
        {
            // The token may have just been rotated out by a concurrent request for the same session
            // (multiple tabs, or the client racing its own refresh call). Rather than forcing a full
            // re-login, replay the pair that request already issued if we're still inside the grace window.
            if (cache.TryGetValue(cacheKey, out CachedRefresh? cached) && cached is not null)
            {
                httpContext.Response.Cookies.Append("refreshToken", cached.RefreshToken,
                    BuildRefreshCookieOptions(httpContext, cached.ExpiresAtUtc));
                httpContext.Response.Cookies.Append("accessToken", cached.Tokens.AccessToken,
                    BuildAccessCookieOptions(httpContext, DateTime.UtcNow.AddMinutes(settings.AccessTokenMinutes)));

                return Results.Ok(ResponseMessage.Create(cached.Tokens, "Токены обновлены", HttpStatusCode.OK));
            }

            return Results.Unauthorized();
        }

        var storedUser = await db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId);

        // Issue new pair
        var rolesRefresh = await db.UserRoles.Where(ur => ur.UserId == stored.UserId).Select(ur => ur.Role.Name)
            .ToListAsync();
        var permsRefresh = await db.RolePermissions
            .Where(rp => db.UserRoles.Where(ur => ur.UserId == stored.UserId).Select(ur => ur.RoleId)
                .Contains(rp.RoleId))
            .Select(rp => rp.Permission.Name)
            .Distinct()
            .ToListAsync();
        var newAccess = tokenService.GenerateAccessToken(stored.UserId, storedUser?.Login, storedUser?.Email,
            rolesRefresh, permsRefresh);
        var newRefresh = tokenService.GenerateRefreshToken();
        var newHash = tokenService.HashRefreshToken(newRefresh);
        var expiresAt = DateTime.UtcNow.AddDays(settings.RefreshTokenDays);
        var accessExpiresAt = DateTime.UtcNow.AddMinutes(settings.AccessTokenMinutes);
        await refreshRepo.CreateAsync(stored.UserId, newHash, expiresAt);

        var tokens = new AuthTokensDto
        {
            AccessToken = newAccess,
            ExpiresIn = settings.AccessTokenMinutes * 60,
            Profile = BuildProfileClaims(stored.UserId, storedUser?.Login, storedUser?.Email, rolesRefresh,
                permsRefresh, accessExpiresAt)
        };

        // Publish before revoking so a request racing this one can find it as soon as the old token
        // stops being "active", instead of hitting the Unauthorized path above.
        cache.Set(cacheKey, new CachedRefresh(tokens, newRefresh, expiresAt), RefreshReuseGraceWindow);

        // Revoke old token (rotation)
        await refreshRepo.RevokeAsync(stored.UserId, stored.TokenHash);

        httpContext.Response.Cookies.Append("refreshToken", newRefresh, BuildRefreshCookieOptions(httpContext, expiresAt));
        httpContext.Response.Cookies.Append("accessToken", newAccess, BuildAccessCookieOptions(httpContext, accessExpiresAt));

        return Results.Ok(ResponseMessage.Create(tokens, "Токены обновлены", HttpStatusCode.OK));
    }

    public static Task<IResult> UpdateUser(IUserRepository userRepository, UserUpdateDto userUpdateDto)
    {
        throw new NotImplementedException();
    }

    public static async Task<IResult> DeleteUser(HttpContext httpContext, DatabaseContext db,
        IRefreshTokenRepository refreshRepo, int userId)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return Results.NotFound(ResponseMessage.Create("Пользователь не найден", HttpStatusCode.NotFound));

        // Check if user has Admin role
        var hasAdminRole = await db.UserRoles
            .Include(ur => ur.Role)
            .AnyAsync(ur => ur.UserId == userId && ur.Role.Name == "Admin");

        var currentUserId = int.Parse(httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        if (currentUserId == userId)
            return Results.BadRequest(ResponseMessage.Create("Вы не можете удалить свою учетную запись",
                HttpStatusCode.BadRequest));

        if (hasAdminRole)
            return Results.BadRequest(ResponseMessage.Create("Нельзя удалять пользователя с ролью Admin",
                HttpStatusCode.BadRequest));

        // Revoke all refresh tokens
        await refreshRepo.RevokeAllAsync(user.Id);

        // Remove role links
        var links = db.UserRoles.Where(ur => ur.UserId == user.Id);
        db.UserRoles.RemoveRange(links);

        // Finally remove user
        db.Users.Remove(user);
        await db.SaveChangesAsync();

        return Results.Ok(ResponseMessage.Create("Пользователь удален", HttpStatusCode.OK));
    }
}
