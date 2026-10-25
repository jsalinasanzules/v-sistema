using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace Turnos.Api.Services;

public static class Db
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, NpgsqlDataSource> Sources = new();
    public static NpgsqlDataSource Source(IConfiguration c) {
        var cs = c.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs)) throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
        return Sources.GetOrAdd(cs, static connectionString => NpgsqlDataSource.Create(connectionString));
    }
}

public sealed class DataProtectionService(IConfiguration config)
{
    private byte[] Key(string name) {
        var raw = config[$"Security:{name}"] ?? "";
        if (raw.Length < 32) throw new InvalidOperationException($"Security:{name} must contain at least 32 characters.");
        return SHA256.HashData(Encoding.UTF8.GetBytes(raw));
    }
    public byte[] Encrypt(string value) {
        var key = Key("DataEncryptionKey"); var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(value); var cipher = new byte[plain.Length]; var tag = new byte[16];
        using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, plain, cipher, tag);
        return nonce.Concat(tag).Concat(cipher).ToArray();
    }
    public string Decrypt(byte[] payload) {
        if (payload.Length < 28) throw new CryptographicException("Invalid encrypted payload.");
        var plain = new byte[payload.Length - 28];
        using var aes = new AesGcm(Key("DataEncryptionKey"), 16);
        aes.Decrypt(payload[..12], payload[28..], payload[12..28], plain);
        return Encoding.UTF8.GetString(plain);
    }
    public byte[] Lookup(string value) => HMACSHA256.HashData(Key("LookupHmacKey"), Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
}

public sealed class AuditService(IConfiguration config, ILogger<AuditService> log)
{
    public async Task WriteAsync(Guid? actor, string action, string entity, string? id = null, object? details = null) {
        try {
            await using var db = await Db.Source(config).OpenConnectionAsync();
            await db.ExecuteAsync("insert into app.audit_events(actor_user_id, action, entity_name, entity_id, details) values (@actor,@action,@entity,@id,cast(@details as jsonb))",
                new { actor, action, entity, id, details = details is null ? null : JsonSerializer.Serialize(details) });
        } catch (Exception ex) { log.LogError(ex, "Audit insert failed for {Action}/{Entity}", action, entity); }
    }
}

public static class RequestUser
{
    public static Guid Id(System.Security.Claims.ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst("uid")?.Value, out var id) ? id : throw new UnauthorizedAccessException();
    public static string Role(System.Security.Claims.ClaimsPrincipal user) =>
        (user.FindFirst("role")?.Value
         ?? user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
         ?? user.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value
         ?? string.Empty).Trim().ToUpperInvariant();
}

public sealed class AuthService(IConfiguration config, DataProtectionService crypto, AuditService audit, ILogger<AuthService> logger)
{
    public async Task<object> LoginAsync(string username, string password, string? ip, string? agent) {
        await using var db = await Db.Source(config).OpenConnectionAsync();
        var user = await db.QuerySingleOrDefaultAsync<LoginRow>("""
          select u.id as "Id",
                 u.username as "Username",
                 u.password_hash as "PasswordHash",
                 u.first_name as "FirstName",
                 u.last_name as "LastName",
                 u.failed_login_attempts as "FailedLoginAttempts",
                 u.is_blocked as "IsBlocked",
                 u.is_active as "IsActive",
                 u.status as "Status",
                 r.code as "Role"
          from app.users u join app.roles r on r.id=u.role_id
          where lower(u.username)=lower(@username) and u.deleted_at is null
          """, new { username });
        if (user is null || user.IsBlocked || !user.IsActive || user.Status != "APPROVED" || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)) {
            if (user is not null) {
                await db.ExecuteAsync("update app.users set failed_login_attempts=least(failed_login_attempts+1,32767), is_blocked=(failed_login_attempts+1>=5), updated_at=now() where id=@id", new { user.Id });
                await audit.WriteAsync(user.Id, "LOGIN_FAILED", "users", user.Id.ToString(), new { username });
            } else await audit.WriteAsync(null, "LOGIN_FAILED", "users", null, new { username });
            logger.LogWarning("Failed login attempt for username {Username} from {IP}", username, ip);
            throw new UnauthorizedAccessException("Credenciales inválidas o usuario pendiente/inactivo/bloqueado.");
        }
        await db.ExecuteAsync("update app.users set failed_login_attempts=0,last_login_at=now(),updated_at=now() where id=@id", new { user.Id });
        await db.ExecuteAsync("insert into app.user_sessions(user_id,ip_address,user_agent) values (@id,cast(@ip as inet),@agent)", new { id=user.Id, ip=string.IsNullOrWhiteSpace(ip) ? null : ip, agent });
        var claims = new List<System.Security.Claims.Claim> {
            new("uid", user.Id.ToString()), new("sub", user.Username), new("role", user.Role),
            new("name", $"{user.FirstName} {user.LastName}")
        };
        var signing = new SymmetricSecurityKey(Encoding.UTF8.GetBytes((config["Security:JwtSigningKey"] ?? "").PadRight(32,'0')[..32]));
        var encryption = new SymmetricSecurityKey(Encoding.UTF8.GetBytes((config["Security:JwtEncryptionKey"] ?? "").PadRight(32,'0')[..32]));
        var creds = new SigningCredentials(signing, SecurityAlgorithms.HmacSha256);
        var encryptCreds = new EncryptingCredentials(encryption, SecurityAlgorithms.Aes256KW, SecurityAlgorithms.Aes256CbcHmacSha512);
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: config["Security:Issuer"] ?? "Turnos.Api", audience: config["Security:Audience"] ?? "Turnos.Web",
            claims: claims, notBefore: DateTime.UtcNow, expires: DateTime.UtcNow.AddHours(2), signingCredentials: creds);
        var encoded = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
        var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();
        var jwe = handler.CreateToken(new SecurityTokenDescriptor {
            Issuer=config["Security:Issuer"] ?? "Turnos.Api", Audience=config["Security:Audience"] ?? "Turnos.Web",
            Subject=new System.Security.Claims.ClaimsIdentity(claims), Expires=DateTime.UtcNow.AddHours(2),
            SigningCredentials=creds, EncryptingCredentials=encryptCreds
        });
        await audit.WriteAsync(user.Id, "LOGIN_SUCCESS", "users", user.Id.ToString());
        return new { accessToken=jwe, tokenType="Bearer", expiresIn=7200, user=new { id=user.Id, username=user.Username, firstName=user.FirstName, lastName=user.LastName, role=user.Role } };
    }
    private sealed record LoginRow(Guid Id, string Username, string PasswordHash, string FirstName, string LastName, short FailedLoginAttempts, bool IsBlocked, bool IsActive, string Status, string Role);
}

public sealed class MenuService(IConfiguration config)
{
    public async Task<IEnumerable<object>> ForRoleAsync(string role) {
        await using var db = await Db.Source(config).OpenConnectionAsync();
        return await db.QueryAsync("""
          select m.id,m.code,m.label,m.route,m.icon,m.sort_order as "sortOrder",
                 p.can_view as "canView",p.can_create as "canCreate",p.can_update as "canUpdate",p.can_delete as "canDelete"
          from app.menu_items m join app.role_menu_permissions p on p.menu_item_id=m.id
          join app.roles r on r.id=p.role_id
          where r.code=@role and r.is_active and m.is_active and p.can_view
          order by m.sort_order,m.label
          """, new { role });
    }
}