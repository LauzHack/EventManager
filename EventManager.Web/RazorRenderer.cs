using System;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace EventManager.Web;

public static class RazorRenderer
{
    public static async Task RenderAsync(string name, Func<Type, object?> modelFactory, HttpContext context)
    {
        // RazorViewEngine won't find views if given a "path", starting with a '/', rather than a "name", not starting with a '/'...
        if (name.StartsWith('/'))
        {
            name = name[1..];
        }

        var viewEngine = context.RequestServices.GetRequiredService<IRazorViewEngine>();
        var tempDataProvider = context.RequestServices.GetRequiredService<ITempDataProvider>();

        var actionContext = new ActionContext(context, new RouteData(), new ActionDescriptor());
        var viewEngineResult = viewEngine.FindView(actionContext, name, isMainPage: true);
        if (!viewEngineResult.Success)
        {
            throw new ArgumentException($"Couldn't find view '{name}'", nameof(name));
        }

        // this is really ugly, because RazorPage doesn't have Model, only the compiler-generated subclass does...
        var pageType = ((RazorView)viewEngineResult.View).RazorPage.GetType();
        var modelType = pageType.GetProperty("Model")!.PropertyType;
        var model = modelFactory(modelType);

        var writer = new HttpResponseStreamWriter(context.Response.Body, Encoding.UTF8);
        var viewContext = new ViewContext(
            actionContext,
            viewEngineResult.View,
            new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            {
                Model = model
            },
            new TempDataDictionary(context, tempDataProvider),
            writer,
            new HtmlHelperOptions()
        );

        await context.Response.StartAsync(context.RequestAborted);
        await viewEngineResult.View.RenderAsync(viewContext);
        await writer.DisposeAsync();
        await context.Response.CompleteAsync();
    }
}