using Koe.GuestServer;

var builder = WebApplication.CreateBuilder(args);
GuestApi.Configure(builder);
var app = builder.Build();
GuestApi.Map(app);
await app.RunAsync();
