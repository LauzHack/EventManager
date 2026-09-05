using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

using EventManager.Abstractions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using MimeKit;

namespace EventManager.Web;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var system = new WebEventManager();

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddRazorPages();
        builder.Services.AddHostedService(_ => new FunctionService(async token =>
        {
            using var timer = new PeriodicTimer(PeriodicTask.Period);
            while (await timer.WaitForNextTickAsync(token))
            {
                await system.RunPeriodicTasksAsync();
            }
        }));

        var app = builder.Build();
        app.UseStaticFiles();
        app.Run(system.RunAsync);
        app.UseHttpsRedirection();
        app.UseHsts();
        app.Start();
        app.WaitForShutdown();
    }

    private sealed class WebEventManager : EventManagerSystem<HttpContext>
    {
        protected override Uri? GetUri(HttpContext context)
        {
            var url = context.Request.GetEncodedUrl();
            // Usual stuff from crawlers
            if (url.EndsWith("/robots.txt", StringComparison.OrdinalIgnoreCase)
            // In case someone deliberately tries to get a 404, logging it is not helpful
             || url.EndsWith("/404", StringComparison.OrdinalIgnoreCase)
            // even when a non-ico favicon is configured some browsers ask for this
             || url.EndsWith("/favicon.ico", StringComparison.OrdinalIgnoreCase)
            // iOS wants this
             || url.EndsWith("/apple-touch-icon.png", StringComparison.OrdinalIgnoreCase)
            // Azure sends requests to `/robots933456.txt` to check that the website is alive
             || url.EndsWith("/robots933456.txt", StringComparison.OrdinalIgnoreCase)
            // Chrome/ium dev tools ask for this on localhost 
             || url.EndsWith("/.well-known/appspecific/com.chrome.devtools.json", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return null;
            }

            // In some HTTPS setups like Azure with a custom domain name, the website thinks it's serving HTTP, but it's exposed as HTTPS.
            // So we must overwrite this; anyway, nobody should use unsecured HTTP anymore.
            if (!url.StartsWith("http", StringComparison.Ordinal))
            {
                url = "https://" + url;
            }

            return new(url);
        }

        protected override async Task<OperationArguments> ParseExtraArgumentsAsync(HttpContext context)
        {
            if (!context.Request.HasFormContentType)
            {
                return OperationArguments.Empty;
            }

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var args = OperationArguments.Empty;
            foreach (var (key, values) in form)
            {
                foreach (var value in values)
                {
                    if (value is not null)
                    {
                        // Normalize line endings, so that input checks done by the browser match
                        // input checks done by the server.
                        // (Empirically, the browser seems to check with '\n' but then send '\r\n' sometimes.)
                        args = args.WithText(key, value.Replace("\r\n", "\n", StringComparison.Ordinal));
                    }
                }
            }
            foreach (var file in form.Files)
            {
                args = args.WithFile(file.Name, new FileAdapter(file));
            }
            return args;
        }

        protected override ClientSideStorage GetClientSideStorage(HttpContext context)
            => new CookiesClientSideStorage(context);

        protected override Task HandleOperationResultAsync(OperationResult result, HttpContext context)
        {
            switch (result)
            {
                case OperationResult.Page pageResult:
                    var pageName = Operation.CreatePageView(pageResult.View.Page).RelativeUri.ToString();
                    var modelFactory = PageModel.CreateFactory(pageResult);
                    return RazorRenderer.RenderAsync(pageName, modelFactory, context);

                case OperationResult.Letter letter:
                    return RazorRenderer.RenderAsync("Letter", _ => letter, context);

                case OperationResult.File(var file):
                    context.Response.Clear();
                    context.Response.Headers.ContentType = file.MimeType;
                    // "attachment" so if viewed directly the file will be downloaded, not previewed inside a browser;
                    // this ensures a malicious user cannot upload, e.g., a PDF with JS that exfiltrates cookies.
                    context.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
                    {
                        FileName = new string([.. file.Name.Where(char.IsAscii)]),
                        FileNameStar = file.Name
                    }.ToString();
                    return context.Response.SendFileAsync(new FileInfoAdapter(file), context.RequestAborted);

                case OperationResult.Challenges challenges:
                    return RazorRenderer.RenderAsync("Challenges", _ => challenges, context);

                case OperationResult.Projects projects:
                    return RazorRenderer.RenderAsync("Projects", _ => projects, context);

                case OperationResult.AuthenticationRequired:
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return RazorRenderer.RenderAsync("AuthenticationRequired", _ => null, context);

                case OperationResult.Unavailable:
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return RazorRenderer.RenderAsync("Unavailable", _ => null, context);

                case OperationResult.NotFound:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return RazorRenderer.RenderAsync("NotFound", _ => null, context);

                case OperationResult.UserError(var description):
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return RazorRenderer.RenderAsync("UserError", _ => description, context);

                case OperationResult.SystemError(var description):
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    return RazorRenderer.RenderAsync("SystemError", _ => description, context);

                default:
                    throw new UnreachableException();
            }
        }

        // Ignore "end of stream" and such, when a client disconnects abruptly
        protected override bool ShouldLogException(Exception e)
            => base.ShouldLogException(e) && e is not BadHttpRequestException;

        protected override Db CreateDb()
            => new EntityFrameworkDb(System.IO.Path.Join(Environment.GetEnvironmentVariable("HOME") ?? throw new InvalidOperationException("No HOME"), "EventManager.db"));

        protected override FileStorage CreateFileStorage()
            => new DiskFileStorage(System.IO.Path.Join(Environment.GetEnvironmentVariable("HOME") ?? throw new InvalidOperationException("No HOME"), "EventManagerFiles"));

        protected override EmailSender CreateEmailSender(Uri baseUri, Config config)
#if DEBUG
            => new DebugEmailSender(baseUri, config.AuthenticationSecret);
#else
            => new MailKitEmailSender(baseUri, config.EventDetails, config.EventTheme, config.EmailSenderSettings, config.AuthenticationSecret);
#endif

        protected override TimeProvider CreateTimeProvider()
            => TimeProvider.System;

        private sealed record FileAdapter(IFormFile Wrapped) : File(Wrapped.FileName, Wrapped.ContentType, Wrapped.Length)
        {
            public override System.IO.Stream OpenRead()
                => Wrapped.OpenReadStream();
        }

        private sealed class FileInfoAdapter(File file) : IFileInfo
        {
            public bool Exists
                => true;

            public bool IsDirectory
                => false;

            public DateTimeOffset LastModified
                => DateTimeOffset.MinValue;

            public long Length
                => file.Length;

            public string Name
                => file.Name;

            public string? PhysicalPath
                => null;

            public System.IO.Stream CreateReadStream()
                => file.OpenRead();
        }
    }

    private sealed class FunctionService(Func<CancellationToken, Task> function) : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
            => function(stoppingToken);
    }

#if DEBUG
    private sealed class DebugEmailSender(Uri baseUri, AuthenticationSecret? authSecret) : EmailSender
    {
        public override Task SendAsync(IReadOnlyCollection<Email> emails, EmailSenderSettings? overrideSettings = null, AuthenticationSecret? overrideSecret = null)
        {
            var secret = overrideSecret ?? authSecret ?? throw new InvalidOperationException("There should be either an auth secret from config or one provided as override");

            foreach (var email in emails)
            {
                Console.WriteLine("--- Email ---");
                Console.WriteLine($"To: {email.Recipient}");
                Console.WriteLine($"Subject: {email.Subject}");
                Console.WriteLine("");
                Console.WriteLine(email.Body);
                if (email.Operation is Operation op)
                {
                    op = Authenticator.AddAuthentication(secret, op, email.Recipient);
                    Console.WriteLine("");
                    Console.WriteLine($"{email.OperationDescription ?? DefaultOperationText}: {new Uri(baseUri, op.RelativeUri)}");
                }
                if (email.AttachedEvent is not null)
                {
                    Console.WriteLine("");
                    Console.WriteLine($"Event: {email.AttachedEvent}");
                }
                Console.WriteLine("--- ----- ---");
                Console.WriteLine("");
            }
            return Task.CompletedTask;
        }

        public override Task SendCopyAsync(string subject, string body, IReadOnlyCollection<string> recipients, Operation? operation = null, string? operationDescription = null)
        {
            Console.WriteLine("--- Email ---");
            Console.WriteLine($"To: {string.Join(", ", recipients)}");
            Console.WriteLine($"Subject: {subject}");
            Console.WriteLine($"Body: {body}");
            if (operation is Operation op)
            {
                Console.WriteLine("");
                Console.WriteLine($"{operationDescription ?? DefaultOperationText}: {new Uri(baseUri, op.RelativeUri)}");
            }
            Console.WriteLine("--- ----- ---");
            Console.WriteLine("");
            return Task.CompletedTask;
        }
    }
#endif
}