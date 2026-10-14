using System.Globalization;
using LuzVidaApi.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace LuzVidaApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MedicionController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public MedicionController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // GET api/Medicion?usuarioId=1
    [HttpGet]
    public IActionResult GetPorUsuario([FromQuery] int usuarioId)
    {
        if (usuarioId <= 0)
        {
            return BadRequest(new { mensaje = "usuarioId es obligatorio." });
        }

        var mediciones = new List<Medicion>();

        using var connection = new NpgsqlConnection(
            Db.ConnectionString(_configuration));
        connection.Open();

        const string sql = """
            SELECT Id, UsuarioId, Fecha, Altura, Hojas, TamanoHojas,
                   GrosorTallo, ColorHojas, EstadoPlanta, TipoLuz, CrecimientoSemanal
            FROM Mediciones
            WHERE UsuarioId = @UsuarioId
            ORDER BY Fecha, Id
            """;

        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@UsuarioId", usuarioId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            mediciones.Add(new Medicion
            {
                Id = reader.GetInt32(0),
                UsuarioId = reader.GetInt32(1),
                Fecha = reader.GetDateTime(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Altura = reader.GetDecimal(3),
                Hojas = reader.GetInt32(4),
                TamanoHojas = reader.IsDBNull(5) ? "" : reader.GetString(5),
                GrosorTallo = reader.GetDecimal(6),
                ColorHojas = reader.IsDBNull(7) ? "" : reader.GetString(7),
                EstadoPlanta = reader.IsDBNull(8) ? "" : reader.GetString(8),
                TipoLuz = reader.IsDBNull(9) ? "" : reader.GetString(9),
                CrecimientoSemanal = reader.GetDecimal(10)
            });
        }

        return Ok(mediciones);
    }

    // POST api/Medicion
    [HttpPost]
    public IActionResult Create([FromBody] Medicion medicion)
    {
        if (medicion.UsuarioId <= 0)
        {
            return BadRequest(new { mensaje = "Debes iniciar sesión para guardar mediciones." });
        }

        if (!DateTime.TryParse(medicion.Fecha, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var fecha))
        {
            return BadRequest(new { mensaje = "La fecha no es válida." });
        }

        using var connection = new NpgsqlConnection(
            Db.ConnectionString(_configuration));
        connection.Open();

        const string sql = """
            INSERT INTO Mediciones
                (UsuarioId, Fecha, Altura, Hojas, TamanoHojas, GrosorTallo,
                 ColorHojas, EstadoPlanta, TipoLuz, CrecimientoSemanal)
            VALUES
                (@UsuarioId, @Fecha, @Altura, @Hojas, @TamanoHojas, @GrosorTallo,
                 @ColorHojas, @EstadoPlanta, @TipoLuz, @CrecimientoSemanal)
            """;

        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@UsuarioId", medicion.UsuarioId);
        command.Parameters.AddWithValue("@Fecha", DateOnly.FromDateTime(fecha));
        command.Parameters.AddWithValue("@Altura", medicion.Altura);
        command.Parameters.AddWithValue("@Hojas", medicion.Hojas);
        command.Parameters.AddWithValue("@TamanoHojas", (object?)medicion.TamanoHojas ?? DBNull.Value);
        command.Parameters.AddWithValue("@GrosorTallo", medicion.GrosorTallo);
        command.Parameters.AddWithValue("@ColorHojas", (object?)medicion.ColorHojas ?? DBNull.Value);
        command.Parameters.AddWithValue("@EstadoPlanta", (object?)medicion.EstadoPlanta ?? DBNull.Value);
        command.Parameters.AddWithValue("@TipoLuz", (object?)medicion.TipoLuz ?? DBNull.Value);
        command.Parameters.AddWithValue("@CrecimientoSemanal", medicion.CrecimientoSemanal);

        try
        {
            command.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation) // llave foránea: el usuario no existe
        {
            return BadRequest(new { mensaje = "El usuario no existe." });
        }

        return Ok(new { mensaje = "Medición guardada correctamente" });
    }
}
