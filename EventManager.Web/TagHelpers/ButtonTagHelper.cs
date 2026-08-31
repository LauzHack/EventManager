using System.Threading.Tasks;

using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EventManager.Web.TagHelpers;

public sealed class ButtonTagHelper : TagHelper
{
    [HtmlAttributeName("x-require-javascript")]
    public bool RequireJavascript { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (RequireJavascript)
        {
            var content = await output.GetChildContentAsync();
            output.Attributes.Add("disabled", "disabled");
            output.PostElement.SetHtmlContent($"""
                <script>
                    var btn = document.currentScript.previousElementSibling;
                    btn.disabled = null;
                    btn.textContent = '{content.GetContent().Trim()}';
                </script>
            """);
            output.Content.SetContent("Please enable JavaScript");
        }

        // While type="submit" is the default, pico.css only styles buttons that explicitly set it
        if (!context.AllAttributes.ContainsName("type"))
        {
            output.Attributes.Add("type", "submit");
        }
    }
}