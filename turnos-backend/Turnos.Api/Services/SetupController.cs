
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Turnos.Api.Services;

namespace Turnos.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/setup")]
public sealed class SetupController(
    IConfiguration config,
    DataProtectionService crypto,
    ILogger<SetupController> logger) : ControllerBase
{
    [HttpPost("initial-admin")]
    public async Task<IActionResult> Create(
        [FromHeader(Name = "X-Bootstrap-Key")] string? key,
        [FromBody] InitialAdminRequest request)
    {
        var expected = config["Security:BootstrapKey"];

        if (string.IsNullOrWhiteSpace(expected) ||
            string.IsNullOrWhiteSpace(key) ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
                SHA256.HashData(Encoding.UTF8.GetBytes(key))))
        {
            return Unauthorized(new { message = "Clave temporal inválida." });
        }

        var username = request.Username?.Trim() ?? "";
        var password = request.Password ?? "";
        var identification = request.Identification?.Trim() ?? "";
        var email = request.Email?.Trim() ?? "";
        var firstName = request.FirstName?.Trim() ?? "";
        var lastName = request.LastName?.Trim() ?? "";

        if (username.Length < 8 || username.Length > 20 ||
            !username.Any(char.IsLetter) ||
            !username.Any(char.IsDigit) ||
            username.Any(c => !char.IsLetterOrDigit(c)))
        {
            return BadRequest(new {
                message = "El usuario debe tener entre 8 y 20 caracteres alfanuméricos, con letras y números."
            });
        }

        if (password.Length < 8 || password.Length > 30 ||
            !password.Any(char.IsUpper) ||
            !password.Any(char.IsDigit))
        {
            return BadRequest(new {
                message = "La contraseña debe tener entre 8 y 30 caracteres, al menos una mayúscula y un número."
            });
        }

        if (identification.Length < 10 || identification.Length > 13 ||
            identification.Any(c => !char.IsDigit(c)))
        {
            return BadRequest(new {
                message = "La identificación debe contener entre 10 y 13 dígitos."
            });
        }

        if (string.IsNullOrWhiteSpace(email) ||
            !System.Text.RegularExpressions.Regex.IsMatch(
                email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
        {
            return BadRequest(new { message = "Correo electrónico inválido." });
        }

        if (firstName.Length is < 1 or > 100 ||
            lastName.Length is < 1 or > 100)
        {
            return BadRequest(new {
                message = "Nombres y apellidos son obligatorios y deben tener máximo 100 caracteres."
            });
        }

        await using var db = await Db.Source(config).OpenConnectionAsync();
        await using var tx = await db.BeginTransactionAsync();

        try
        {
            // Evita que dos solicitudes creen el administrador simultáneamente.
            await db.ExecuteAsync(
                "select pg_advisory_xact_lock(748291604)",
                transaction: tx);

            var exists = await db.ExecuteScalarAsync<bool>("""
                select exists (
                    select 1
                    from app.users u
                    join app.roles r on r.id = u.role_id
                    where r.code = 'ADMINISTRADOR'
                )
                """, transaction: tx);

            if (exists)
            {
                return Conflict(new {
                    message = "Ya existe un administrador. Este endpoint solo permite la inicialización."
                });
            }

            var roleId = await db.ExecuteScalarAsync<short?>(
                """
                select id
                from app.roles
                where code = 'ADMINISTRADOR'
                  and is_active = true
                """,
                transaction: tx);

            if (roleId is null)
            {
                return StatusCode(500, new {
                    message = "No existe el rol ADMINISTRADOR activo en app.roles."
                });
            }

            var id = Guid.NewGuid();
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(password, 12);

            await db.ExecuteAsync("""
                insert into app.users (
                    id,
                    role_id,
                    username,
                    password_hash,
                    identification_cipher,
                    identification_lookup,
                    email_cipher,
                    email_lookup,
                    first_name,
                    last_name,
                    status,
                    is_active,
                    is_blocked,
                    created_by,
                    approved_by,
                    approved_at
                )
                values (
                    @id,
                    @roleId,
                    @username,
                    @passwordHash,
                    @identificationCipher,
                    @identificationLookup,
                    @emailCipher,
                    @emailLookup,
                    @firstName,
                    @lastName,
                    'APPROVED',
                    true,
                    false,
                    null,
                    null,
                    now()
                )
                """,
                new {
                    id,
                    roleId,
                    username,
                    passwordHash,
                    identificationCipher = crypto.Encrypt(identification),
                    identificationLookup = crypto.Lookup(identification),
                    emailCipher = crypto.Encrypt(email),
                    emailLookup = crypto.Lookup(email),
                    firstName,
                    lastName
                },
                tx);

            await tx.CommitAsync();

            logger.LogWarning(
                "Initial administrator created. Remove SetupController and Security:BootstrapKey immediately.");

            return Created("/api/users/" + id, new {
                id,
                username,
                role = "ADMINISTRADOR",
                status = "APPROVED",
                message = "Administrador creado correctamente."
            });
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            await tx.RollbackAsync();

            return Conflict(new {
                message = "El usuario, la identificación o el correo ya están registrados."
            });
        }
    }
}

public sealed class InitialAdminRequest
{
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? Identification { get; init; }
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}
