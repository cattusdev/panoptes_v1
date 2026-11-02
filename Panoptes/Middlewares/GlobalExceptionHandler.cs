using System.Globalization;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Panoptes.Services.Localization;

namespace Panoptes.Middlewares
{
    // Κάνουμε implement το νέο interface του .NET 8+
    public class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<GlobalExceptionHandler> _logger;
        private readonly IStringLocalizer<SharedResource> _t;

        public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IStringLocalizer<SharedResource> localizer)
        {
            _logger = logger;
            _t = localizer;
        }

        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            // 1. Παίρνουμε το μοναδικό ID της ταυτότητας αυτού του request
            var traceId = httpContext.TraceIdentifier;

            // 2. Καταγράφουμε το FULL error στο Serilog (για τα μάτια του IT μόνο)
            _logger.LogError(exception, "🔥 System Crash! TraceId: {TraceId}. Message: {Message}", traceId, exception.Message);

            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

            // Η γλώσσα του request (το UseRequestLocalization την έχει βρει, αλλά η αλλαγή της κουλτούρας δεν φτάνει ως εδώ)
            using var _ = AppLanguages.Use(httpContext.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture.Name ?? AppLanguages.Default);

            // 3. API (το React app) -> ProblemDetails JSON. Το "detail" εμφανίζεται στον χρήστη (toast) μαζί με το reference.
            var isApi = httpContext.Request.Path.StartsWithSegments("/api") ||
                        httpContext.Request.Headers.Accept.ToString().Contains("application/json");
            if (isApi)
            {
                await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = _t["Server error"],
                    Detail = _t["Something went wrong on our side. Reference: {0}", traceId],
                    Extensions = { ["traceId"] = traceId }
                }, options: null, contentType: "application/problem+json", cancellationToken);
                return true;
            }

            // 4. Οτιδήποτε άλλο -> μια αυτόνομη σελίδα λάθους (χωρίς εξαρτήσεις, ώστε να δουλεύει ό,τι κι αν χάλασε)
            httpContext.Response.ContentType = "text/html; charset=utf-8";
            await httpContext.Response.WriteAsync(ErrorPage(HtmlEncoder.Default.Encode(traceId)), cancellationToken);
            return true;
        }

        // Οι μεταφράσεις είναι δικό μας κείμενο (χωρίς HTML)· το reference έρχεται ήδη encoded
        private string ErrorPage(string traceId) => $$"""
            <!doctype html>
            <html lang="{{CultureInfo.CurrentUICulture.TwoLetterISOLanguageName}}">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>{{_t["Something went wrong"]}} - Panoptes</title>
              <link rel="icon" type="image/svg+xml" href="/img/logo.svg" />
              <style>
                body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #f6f7f6; color: #111214;
                       font-family: system-ui, -apple-system, Segoe UI, sans-serif; }
                .card { max-width: 420px; margin: 16px; padding: 28px; background: #fff; border: 1px solid #e4e6ea; border-radius: 12px; }
                h1 { font-size: 1.4rem; margin: 0 0 8px; }
                p { color: #6b7076; line-height: 1.5; margin: 0 0 20px; }
                code { background: #f0f1f3; padding: 2px 6px; border-radius: 6px; font-size: .85rem; }
                a { display: inline-block; background: #0e0e10; color: #fff; padding: 11px 18px; border-radius: 8px; text-decoration: none; font-weight: 600; }
              </style>
            </head>
            <body>
              <div class="card">
                <h1>{{_t["Something went wrong"]}}</h1>
                <p>{{_t["An unexpected error occurred on our side. If it keeps happening, contact support with this reference: {0}", $"<code>{traceId}</code>"]}}</p>
                <a href="/">{{_t["Return home"]}}</a>
              </div>
            </body>
            </html>
            """;
    }
}
