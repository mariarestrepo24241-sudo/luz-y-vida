using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using LuzVidaApi.Models;

namespace LuzVidaApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClienteController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly PasswordHasher<Cliente> _passwordHasher = new();

    public ClienteController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private static bool ValidarPassword(string password)
    {
        // Mínimo 6 caracteres, una mayúscula y un carácter especial
        return !string.IsNullOrWhiteSpace(password)
            && Regex.IsMatch(password, @"^(?=.*[A-Z])(?=.*[^A-Za-z0-9]).{6,}$");
    }

    [HttpPost]
    public IActionResult Create(Cliente cliente)
    {
        if (!ValidarPassword(cliente.Password))
        {
            return BadRequest(new
            {
                mensaje = "La contraseña debe tener mínimo 6 caracteres, una letra mayúscula y un carácter especial."
            });
        }

        using var connection = new NpgsqlConnection(
            Db.ConnectionString(_configuration));

        connection.Open();

        cliente.PasswordHash =
            _passwordHasher.HashPassword(cliente, cliente.Password);

        const string sql = """
            INSERT INTO Clientes
                (Nombre, Apellido, Email, Celular, PasswordHash)
            VALUES
                (@Nombre, @Apellido, @Email, @Celular, @PasswordHash)
            """;

        using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Nombre", cliente.Nombre);
        command.Parameters.AddWithValue("@Apellido", cliente.Apellido);
        command.Parameters.AddWithValue("@Email", cliente.Email);
        command.Parameters.AddWithValue("@Celular", (object?)cliente.Celular ?? DBNull.Value);
        command.Parameters.AddWithValue("@PasswordHash", cliente.PasswordHash);

        try
        {
            command.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict(new
            {
                mensaje = "Ya existe una cuenta con ese correo."
            });
        }

        return Ok(new
        {
            mensaje = "Cliente creado correctamente"
        });
    }

    [HttpPost("login")]
    public IActionResult Login(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new
            {
                mensaje = "El correo y la contraseña son obligatorios."
            });
        }

        using var connection = new NpgsqlConnection(
            Db.ConnectionString(_configuration));

        connection.Open();

        const string sql = """
            SELECT Id, Nombre, Apellido, Email, Celular, FechaCreacion, PasswordHash
            FROM Clientes
            WHERE lower(Email) = lower(@Email)
            """;

        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Email", request.Email);

        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return Unauthorized(new
            {
                mensaje = "Correo o contraseña incorrectos."
            });
        }

        var cliente = new Cliente
        {
            Id = reader.GetInt32(0),
            Nombre = reader.GetString(1),
            Apellido = reader.GetString(2),
            Email = reader.GetString(3),
            Celular = reader.IsDBNull(4) ? "" : reader.GetString(4),
            FechaCreacion = reader.GetDateTime(5),
            PasswordHash = reader.IsDBNull(6)
                ? string.Empty
                : reader.GetString(6)
        };

        if (string.IsNullOrWhiteSpace(cliente.PasswordHash))
        {
            return Unauthorized(new
            {
                mensaje = "Este cliente todavía no tiene una contraseña registrada."
            });
        }

        var resultado = _passwordHasher.VerifyHashedPassword(
            cliente,
            cliente.PasswordHash!,
            request.Password);

        if (resultado == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new
            {
                mensaje = "Correo o contraseña incorrectos."
            });
        }

        return Ok(new
        {
            mensaje = "Inicio de sesión correcto",
            cliente = new
            {
                cliente.Id,
                cliente.Nombre,
                cliente.Apellido,
                cliente.Email,
                cliente.Celular,
                cliente.FechaCreacion
            }
        });
    }
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}