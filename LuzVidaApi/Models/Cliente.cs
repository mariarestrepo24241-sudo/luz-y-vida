namespace LuzVidaApi.Models;

public class Cliente
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Apellido { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Celular { get; set; }
    public string Password { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public DateTime FechaCreacion { get; set; }
}
