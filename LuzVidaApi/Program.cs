using LuzVidaApi;

var builder = WebApplication.CreateBuilder(args);

// En Render el puerto llega en la variable PORT.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// 1. CORS (para abrir maria.html desde Live Server u otro origen)
builder.Services.AddCors(options =>
{
    options.AddPolicy("PermitirTodo", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

Db.PrepararTablas(app.Configuration, app.Logger);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// CORS debe ir antes de MapControllers.
// No se usa UseHttpsRedirection: Render ya entrega la pagina por https.
app.UseCors("PermitirTodo");

// La API tambien entrega la pagina (wwwroot/index.html), asi todo vive en un solo servicio.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.Run();
