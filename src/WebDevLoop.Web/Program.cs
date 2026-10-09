using WebDevLoop.Web.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddWebDevLoop(builder.Configuration);

WebApplication app = builder.Build();
await app.InitializeWebDevLoopAsync();
app.UseWebDevLoop();
await app.RunAsync();
