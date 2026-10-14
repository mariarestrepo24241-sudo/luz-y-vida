using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace LuzVidaApi.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ChatController : ControllerBase
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(120) };

    // Tiempo máximo de espera por cada intento con Gemini.
    private const int SegundosPorIntento = 25;

    private readonly IConfiguration _configuration;

    public ChatController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // Texto corto con el tipo de error y su causa, para mostrarlo en la página.
    private static string Descripcion(Exception ex)
    {
        var texto = ex.GetType().Name + ": " + ex.Message;
        var interna = ex.InnerException;
        while (interna != null)
        {
            texto += " | " + interna.GetType().Name + ": " + interna.Message;
            interna = interna.InnerException;
        }
        return texto;
    }

    // Modelos gratuitos a probar, en orden. Si uno está saturado o no existe, se pasa al siguiente.
    private static readonly string[] ModelosRespaldo =
    {
        "gemini-3.8-flash",
        "gemini-3.7-flash",
        "gemini-3.5-flash-lite",
        "gemini-3.1-flash-lite"
    };

    private List<string> ListaModelos()
    {
        var lista = new List<string>();
        var principal = _configuration["Gemini:Model"];
        if (!string.IsNullOrWhiteSpace(principal)) lista.Add(principal.Trim());
        foreach (var m in ModelosRespaldo)
        {
            if (!lista.Contains(m)) lista.Add(m);
        }
        return lista;
    }

    private static string MensajeDeGoogle(string json)
    {
        try
        {
            using var err = JsonDocument.Parse(json);
            if (err.RootElement.TryGetProperty("error", out var e) &&
                e.TryGetProperty("message", out var m))
            {
                return m.GetString() ?? json;
            }
        }
        catch { /* se deja el texto tal cual */ }
        return json;
    }

    // Llama a Gemini y devuelve el texto de la respuesta (o un error listo para enviar al navegador).
    // Reintenta si el modelo está saturado y prueba con otro modelo si hace falta.
    private async Task<(string? texto, IActionResult? error)> LlamarGemini(object cuerpo)
    {
        var apiKey = _configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return (null, StatusCode(500, new { error = "La clave de Gemini no está configurada en el servidor." }));
        }

        var contenidoJson = JsonSerializer.Serialize(cuerpo);
        var intentos = new List<string>();
        int ultimoEstado = 502;
        string ultimoMensaje = "";

        foreach (var modelo in ListaModelos())
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelo}:generateContent";

            for (int intento = 1; intento <= 2; intento++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(contenidoJson, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("x-goog-api-key", apiKey.Trim());

                HttpResponseMessage response;
                string json;
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(SegundosPorIntento));
                    response = await _httpClient.SendAsync(request, cts.Token);
                    json = await response.Content.ReadAsStringAsync(cts.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is HttpRequestException || ex is IOException)
                {
                    // Sin respuesta a tiempo o falla de red: se anota y se pasa al siguiente modelo.
                    ultimoEstado = 504;
                    ultimoMensaje = $"sin respuesta en {SegundosPorIntento} s ({ex.GetType().Name})";
                    Console.WriteLine($"Gemini {modelo} intento {intento}: {ultimoMensaje}");
                    intentos.Add($"{modelo} (sin respuesta)");
                    break;
                }

                using var respuestaGemini = response;
                var estado = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(json);
                    var texto = new StringBuilder();

                    if (doc.RootElement.TryGetProperty("candidates", out var candidatos) &&
                        candidatos.GetArrayLength() > 0 &&
                        candidatos[0].TryGetProperty("content", out var contenido) &&
                        contenido.TryGetProperty("parts", out var partes))
                    {
                        foreach (var parte in partes.EnumerateArray())
                        {
                            if (parte.TryGetProperty("text", out var t))
                            {
                                texto.Append(t.GetString());
                            }
                        }
                    }

                    if (texto.Length == 0)
                    {
                        return (null, StatusCode(502, new { error = "Gemini no devolvió ningún texto. Prueba con otra foto." }));
                    }

                    Console.WriteLine($"Gemini respondió con el modelo {modelo}.");
                    return (texto.ToString(), null);
                }

                ultimoEstado = estado;
                ultimoMensaje = MensajeDeGoogle(json);
                Console.WriteLine($"Gemini {modelo} intento {intento}: {estado} - {ultimoMensaje}");
                intentos.Add($"{modelo} ({estado})");

                // Clave mala o sin permiso: probar otro modelo no sirve.
                if (estado == 401 || estado == 403)
                {
                    return (null, StatusCode(estado, new { error = "Error al comunicarse con Gemini: " + ultimoMensaje }));
                }

                // Saturado o error temporal: esperar un momento y reintentar el mismo modelo.
                if ((estado == 503 || estado == 500 || estado == 429) && intento == 1)
                {
                    await Task.Delay(3000);
                    continue;
                }

                break; // pasar al siguiente modelo
            }
        }

        return (null, StatusCode(ultimoEstado, new
        {
            error = "Error al comunicarse con Gemini: " + ultimoMensaje +
                    " (modelos probados: " + string.Join(", ", intentos) + ")"
        }));
    }

    // POST api/Chat/enviar  (multipart/form-data con el campo "imagen")
    // Es el endpoint que usa maria.html para analizar la foto de la planta.
    [HttpPost("enviar")]
    public async Task<IActionResult> EnviarFotoPlanta(IFormFile imagen)
    {
        if (imagen == null || imagen.Length == 0)
        {
            return BadRequest(new { error = "No se ha adjuntado ninguna imagen." });
        }

        try
        {
            Console.WriteLine($"Imagen recibida: {imagen.Length / 1024} KB");
            string base64Image;
            using (var memoryStream = new MemoryStream())
            {
                await imagen.CopyToAsync(memoryStream);
                base64Image = Convert.ToBase64String(memoryStream.ToArray());
            }

            var mimeType = string.IsNullOrWhiteSpace(imagen.ContentType) ? "image/jpeg" : imagen.ContentType;

            var cuerpo = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new
                            {
                                text = "Actúa como un experto en agronomía y botánica. Analiza la salud de esta planta en la foto, identifica posibles plagas, deficiencias o enfermedades, y da un diagnóstico breve junto con recomendaciones de cuidado. Responde en español."
                            },
                            new
                            {
                                inline_data = new { mime_type = mimeType, data = base64Image }
                            }
                        }
                    }
                }
            };

            var (texto, error) = await LlamarGemini(cuerpo);
            if (error != null) return error;

            // maria.html lee el campo "analisis".
            return Ok(new { analisis = texto });
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR en Chat/enviar: " + ex);
            return StatusCode(500, new { error = "Error interno: " + Descripcion(ex) });
        }
    }

    // POST api/Chat/medicion  (multipart/form-data con el campo "imagen")
    // La IA mira la foto y devuelve los valores para rellenar el formulario de medición.
    [HttpPost("medicion")]
    public async Task<IActionResult> AnalizarMedicion(IFormFile imagen)
    {
        if (imagen == null || imagen.Length == 0)
        {
            return BadRequest(new { error = "No se ha adjuntado ninguna imagen." });
        }

        try
        {
            string base64Image;
            using (var memoryStream = new MemoryStream())
            {
                await imagen.CopyToAsync(memoryStream);
                base64Image = Convert.ToBase64String(memoryStream.ToArray());
            }

            var mimeType = string.IsNullOrWhiteSpace(imagen.ContentType) ? "image/jpeg" : imagen.ContentType;

            var esquema = new
            {
                type = "OBJECT",
                properties = new
                {
                    altura_cm = new { type = "NUMBER", nullable = true },
                    numero_hojas = new { type = "INTEGER", nullable = true },
                    tamano_hojas = new { type = "STRING", nullable = true },
                    grosor_tallo_mm = new { type = "NUMBER" },
                    color_hojas = new { type = "STRING", @enum = new[] { "Verde claro", "Verde", "Verde intenso", "Amarillo", "Marrón" } },
                    estado_planta = new { type = "STRING", @enum = new[] { "Sana", "Regular", "En observación", "Enferma" } },
                    tipo_luz = new { type = "STRING", @enum = new[] { "Natural", "Artificial", "Mixta" }, nullable = true },
                    tipo_planta = new { type = "STRING" },
                    aspecto = new { type = "STRING" }
                },
                required = new[] { "tipo_planta", "grosor_tallo_mm", "color_hojas", "estado_planta", "aspecto" }
            };

            var cuerpo = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new
                            {
                                text = "Actúa como un experto en agronomía y botánica. Observa la foto de esta planta y devuelve los datos para un registro de medición. " +
                                       "tipo_planta: nombre común y científico, por ejemplo 'Lengua de suegra (Dracaena trifasciata)'. " +
                                       "Estima altura_cm (alto total de la planta en cm), numero_hojas (hojas visibles), tamano_hojas (formato 'largo x ancho cm' de una hoja típica), " +
                                       "grosor_tallo_mm (grosor del tallo; si la planta no tiene tallo visible, el de la base o de la hoja más gruesa; da siempre una estimación en milímetros), " +
                                       "color_hojas, estado_planta y tipo_luz (Natural si se ve luz de ventana o exterior, Artificial si es de lámpara, Mixta si hay ambas). " +
                                       "Si hay un objeto de referencia (maceta, mano, regla) úsalo para estimar tamaños. Si algo no se puede estimar con la foto, devuelve null en ese campo (excepto grosor_tallo_mm). " +
                                       "En aspecto describe en 1 o 2 frases cortas, en español, cómo se ve la planta hoy: hojas, color y si hay plagas, manchas o deficiencias."
                            },
                            new
                            {
                                inline_data = new { mime_type = mimeType, data = base64Image }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    responseSchema = esquema,
                    temperature = 0.2
                }
            };

            var (texto, error) = await LlamarGemini(cuerpo);
            if (error != null) return error;

            JsonElement medicion;
            try
            {
                var limpio = texto!.Replace("```json", "").Replace("```", "").Trim();
                using var doc = JsonDocument.Parse(limpio);
                medicion = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return StatusCode(502, new { error = "La IA no devolvió datos válidos. Prueba con otra foto." });
            }

            // maria.html lee el campo "medicion".
            return Ok(new { medicion });
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR en Chat/medicion: " + ex);
            return StatusCode(500, new { error = "Error interno: " + Descripcion(ex) });
        }
    }

    // POST api/Chat/texto  { "mensaje": "..." }
    [HttpPost("texto")]
    public async Task<IActionResult> EnviarMensaje([FromBody] MensajeDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Mensaje))
        {
            return BadRequest(new { error = "El mensaje no puede estar vacío." });
        }

        try
        {
            var cuerpo = new
            {
                contents = new[] { new { parts = new[] { new { text = request.Mensaje } } } }
            };

            var (texto, error) = await LlamarGemini(cuerpo);
            if (error != null) return error;

            return Ok(new { respuesta = texto });
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR en Chat/texto: " + ex);
            return StatusCode(500, new { error = "Error interno: " + Descripcion(ex) });
        }
    }
}

public class MensajeDto
{
    public string Mensaje { get; set; } = string.Empty;
}
