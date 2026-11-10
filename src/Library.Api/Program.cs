using Library.Api;

var builder = WebApplication.CreateBuilder(args);
LibraryHost.Build(builder).Run();

public partial class Program { }
