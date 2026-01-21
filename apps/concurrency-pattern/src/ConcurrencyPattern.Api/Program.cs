using ConcurrencyPattern.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 서비스 등록
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Concurrency Pattern API",
        Version = "v1",
        Description = "ASP.NET EF Core 동시성 레코드 접근 문제 해결을 위한 순차 처리 패턴 프로토타입"
    });
});

var app = builder.Build();

// Swagger (개발 환경)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
