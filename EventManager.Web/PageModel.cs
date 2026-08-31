using System;
using System.Collections.Immutable;
using System.Diagnostics;

using EventManager.Abstractions;

namespace EventManager.Web;

public record PageModel(PageView View, Uri Uri, Status Status, string Message, ImmutableArray<PageView> AvailableViews, Config Config)
{
    public static Func<Type, object> CreateFactory(OperationResult.Page result)
    {
        return type =>
        {
            if (type == typeof(PageModel))
            {
                type = typeof(PageModel<object, object>);
            }

            // If we're showing a required page, the URI should just be the one for the user.
            // Otherwise, it should be the page-specific one.
            // This ensures refreshing the page works as expected in both cases.
            // (If we showed the page-specific one always, refreshing might break if the currently-required page becomes unavailable for some external reason,
            //  e.g., the user had opened the page before the event started and is refreshing it after it has started)
            var uri = result.View.IsRequired
                    ? Operation.CreatePageView(result.View.Page.UserType).RelativeUri
                    : Operation.CreatePageView(result.View.Page).RelativeUri;

            return Activator.CreateInstance(type, result.View, uri, result.Model, result.User, result.Status, result.Message, result.AvailableViews, result.Configuration)
                ?? throw new UnreachableException();
        };
    }
}

public sealed record PageModel<TData, TUser>(PageView View, Uri Uri, TData Data, TUser User, Status Status, string Message, ImmutableArray<PageView> AvailableViews, Config Config)
    : PageModel(View, Uri, Status, Message, AvailableViews, Config);