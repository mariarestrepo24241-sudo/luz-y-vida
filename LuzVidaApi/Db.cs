using Npgsql;

namespace LuzVidaApi;

public static class Db
{
    // En Render / Neon la direccion llega en la variable DATABASE_URL
    // (postgresql://usuario:clave@servidor/base). Aqui se convierte al formato de Npgsql.
    public static string ConnectionString(IConfiguration config)
    {
        var url = Environment.GetEnvironmentVariable("DATABASE_URL");

        if (string.IsNullOrWhiteSpace(url))
        {
            var local = config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(local))
            {
                throw new InvalidOperationException("Falta la variable DATABASE_URL con la direccion de la base de datos.");
            }
            return local;
        }

        url = url.Trim();

        if (!url.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return url; // ya viene en formato "Host=...;Database=..."
        }

        var uri = new Uri(url);
        var info = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = Uri.UnescapeDataString(info[0]),
            Password = info.Length > 1 ? Uri.UnescapeDataString(info[1]) : "",
            Database = uri.AbsolutePath.TrimStart('/'),
            SslMode = SslMode.Require,
            TrustServerCertificate = true
        };

        return builder.ConnectionString;
    }

    private const string CrearTablas = """
        CREATE TABLE IF NOT EXISTS clientes (
            id             SERIAL PRIMARY KEY,
            nombre         VARCHAR(100) NOT NULL,
            apellido       VARCHAR(100) NOT NULL,
            email          VARCHAR(200) NOT NULL,
            celular        VARCHAR(30),
            passwordhash   VARCHAR(500),
            fechacreacion  TIMESTAMPTZ NOT NULL DEFAULT now()
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_clientes_email ON clientes (lower(email));

        CREATE TABLE IF NOT EXISTS mediciones (
            id                  SERIAL PRIMARY KEY,
            usuarioid           INT NOT NULL REFERENCES clientes(id),
            fecha               DATE NOT NULL,
            altura              NUMERIC(8,2) NOT NULL,
            hojas               INT NOT NULL,
            tamanohojas         VARCHAR(100),
            grosortallo         NUMERIC(8,2) NOT NULL,
            colorhojas          VARCHAR(50),
            estadoplanta        VARCHAR(50),
            tipoluz             VARCHAR(50),
            crecimientosemanal  NUMERIC(8,2) NOT NULL,
            fechacreacion       TIMESTAMPTZ NOT NULL DEFAULT now()
        );
        """;

    // Crea las tablas si todavia no existen (no borra nada).
    public static void PrepararTablas(IConfiguration config, ILogger logger)
    {
        try
        {
            using var connection = new NpgsqlConnection(ConnectionString(config));
            connection.Open();
            using var command = new NpgsqlCommand(CrearTablas, connection);
            command.ExecuteNonQuery();
            logger.LogInformation("Base de datos lista.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo preparar la base de datos.");
        }
    }
}
