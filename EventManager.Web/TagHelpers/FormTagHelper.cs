using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EventManager.Web.TagHelpers;

public sealed class FormTagHelper : TagHelper
{
    [HtmlAttributeName("x-minimize")]
    public bool Minimize { get; set; } = false;

    [HtmlAttributeName("x-minimize-description")]
    public string MinimizeDescription { get; set; } = "";

    [HtmlAttributeName("x-enforce-acknowledgement")]
    public string EnforceAcknowledgement { get; set; } = "";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var content = await output.GetChildContentAsync();

        // The default method is GET, let's not have to overwrite it every time
        if (!context.AllAttributes.ContainsName("method"))
        {
            output.Attributes.Add("method", "post");
        }

        // Forms that submit files must have this special enctype
        if (content.GetContent().Contains("type=\"file\"", StringComparison.Ordinal))
        {
            output.Attributes.Add("enctype", "multipart/form-data");
        }

        if (Minimize)
        {
            output.PreElement.AppendHtml($"<details><summary>Click here to edit {MinimizeDescription}</summary>");
            output.PostElement.AppendHtml("</details>");
        }

        if (EnforceAcknowledgement is not "")
        {
            var id = context.UniqueId;
            output.Attributes.SetAttribute("id", id);
            output.PreElement.AppendHtml($$"""
                <label>
                    To confirm you want to perform this operation, type "<em>{{EnforceAcknowledgement}}</em>" without quotes:
                    <input id="acknowledgement-{{id}}"
                           type="text"
                           onpaste="return false;" />
                </label>
            """);
            output.PostElement.AppendHtml($$"""
                <script>
                    document.getElementById('{{id}}').addEventListener('submit', (e) => {
                        var acknowledgement = document.getElementById('acknowledgement-{{id}}');
                        if (acknowledgement.value !== '{{EnforceAcknowledgement}}') {
                            showAlert('Error', 'Please write the exact acknowledgement sentence.');
                            e.preventDefault();
                            return false;
                        }
                    });
                </script>
            """);
        }
    }
}