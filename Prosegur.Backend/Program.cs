using Prosegur.Backend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<IStripeService, StripeService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWpfClient", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c => c.RoutePrefix = "swagger");
app.UseCors("AllowWpfClient");
app.UseStaticFiles();
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/simulation.html"));

app.Logger.LogInformation("API: http://localhost:5000");
app.Logger.LogInformation("Swagger: http://localhost:5000/swagger");
app.Logger.LogInformation("Dashboard: http://localhost:5000/simulation.html");

app.Run();
