using SharpLab.Container.Docker.Manager;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddSingleton<ExecutionEndpoint>();
builder.Services.AddSingleton<ContainerOptions>(builder.Configuration.Get<ContainerOptions>()!);

var app = builder.Build();

app.Map("/", async (HttpContext context, ExecutionEndpoint executionEndpoint) => {
    await executionEndpoint.Execute(context);
});

app.Run();
