namespace LuzVidaApi.Models;

public class Medicion
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public string Fecha { get; set; } = string.Empty;        // yyyy-MM-dd
    public decimal Altura { get; set; }                       // cm
    public int Hojas { get; set; }
    public string? TamanoHojas { get; set; }
    public decimal GrosorTallo { get; set; }                  // mm
    public string? ColorHojas { get; set; }
    public string? EstadoPlanta { get; set; }
    public string? TipoLuz { get; set; }
    public decimal CrecimientoSemanal { get; set; }           // cm
}
