using System.Net;
using Library.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Library.Desktop;

internal sealed class LibraryWindow : Form
{
    private readonly WebView2 view = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Label status = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "Открываю библиотеку…"
    };
    private readonly CancellationTokenSource lifetime = new();
    private readonly string dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibrarySystem0302");
    private Task? opening;
    private WebApplication? host;
    private bool closing;
    private bool stopped;

    public LibraryWindow()
    {
        Text = "Электронная библиотека — МДК 03.02";
        ClientSize = new Size(1200, 780);
        MinimumSize = new Size(720, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(view);
        Controls.Add(status);
        Shown += (_, _) => opening = OpenAsync();
        FormClosing += OnClosing;
    }

    private async Task OpenAsync()
    {
        var cancellation = lifetime.Token;
        try
        {
            Directory.CreateDirectory(dataDirectory);
            // Use fresh reports from the solution during development; published copies use bundled data.
            string contentRoot = FindContentRoot();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(LibraryHost).Assembly.GetName().Name,
                ContentRootPath = contentRoot,
                EnvironmentName = "Development"
            });
            builder.Logging.ClearProviders();
            builder.Configuration["LabContentRoot"] = contentRoot;
            builder.Configuration["StorageRoot"] = Path.Combine(dataDirectory, "Storage");
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));

            host = LibraryHost.Build(builder);
            await host.StartAsync(cancellation);
            var addresses = host.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses;
            var origin = new Uri(addresses?.Single()
                ?? throw new InvalidOperationException("Локальный сервер не сообщил адрес."));

            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(dataDirectory, "WebView2"));
            cancellation.ThrowIfCancellationRequested();
            await view.EnsureCoreWebView2Async(environment);
            cancellation.ThrowIfCancellationRequested();
            view.CoreWebView2.Settings.AreDevToolsEnabled = false;
            view.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var target) ||
                    target.Scheme != origin.Scheme || target.Host != origin.Host || target.Port != origin.Port)
                    e.Cancel = true;
            };
            view.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            view.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                if (e.IsSuccess || closing) return;
                view.Hide();
                status.Text = $"Не удалось открыть библиотеку: {e.WebErrorStatus}.";
                status.Show();
            };
            view.Source = origin;
            status.Hide();
            view.Show();
        }
        catch (OperationCanceledException) when (closing) { }
        catch (WebView2RuntimeNotFoundException) when (!closing)
        {
            status.Text = "Не найден Microsoft Edge WebView2 Runtime.\nУстановите его и запустите проект снова.";
        }
        catch (Exception error) when (!closing)
        {
            status.Text = "Не удалось запустить библиотеку.\n" + error.GetBaseException().Message;
        }
    }

    private static string FindContentRoot()
    {
        for (DirectoryInfo? folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "LibrarySystem.sln")) &&
                File.Exists(Path.Combine(folder.FullName, "web", "index.html")))
                return folder.FullName;
        }
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "web", "index.html")))
            return AppContext.BaseDirectory;
        throw new FileNotFoundException("Не найден интерфейс web/index.html. Распакуйте проект целиком и пересоберите решение.");
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (stopped) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        lifetime.Cancel();
        try
        {
            if (opening is not null) await opening;
        }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
        try
        {
            if (host is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await host.StopAsync(timeout.Token); }
                finally { await host.DisposeAsync(); }
            }
        }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
        finally
        {
            lifetime.Dispose();
            stopped = true;
            // Re-enter Close only after this FormClosing event has returned.
            BeginInvoke((Action)Close);
        }
    }
}
