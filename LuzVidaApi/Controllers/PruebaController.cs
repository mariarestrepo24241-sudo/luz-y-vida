using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace LuzVidaApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PruebaController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public PruebaController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("testdb")]
    public IActionResult TestDb()
    {
        try
        {
            using var connection = new NpgsqlConnection(
                Db.ConnectionString(_configuration));

            connection.Open();

            return Ok(new
            {
                mensaje = "Conexión a la base de datos exitosa",
                baseDeDatos = connection.Database
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                mensaje = "Error al conectar con la base de datos",
                error = ex.Message
            });
        }
    }
}