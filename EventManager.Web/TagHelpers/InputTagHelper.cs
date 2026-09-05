using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EventManager.Web.TagHelpers;

// IMPORTANT: do not use/set the 'id' attribute of the input, and do not use `querySelector`!
// This works in isolation but some inputs are used inside our "repeating input" tag helper,
// which puts its contents inside a <template> and clones it.
// As a result, if something inside that template has an 'id', multiple elements will have it once cloned multiple times.
// Instead, use an "onload" handler, which the layouts trigger via script on all input elements when the page is loaded,
// and which the repeating input also triggers on all elements when a template is cloned.

// Current exceptions to the above:
// - ExistingFileId is not supported in repeated inputs
//   (because it uses 'id')
// - <select> using data- attributes on options to submit multiple values is not supported in repeated inputs
//   (because it uses 'querySelector')

// IF YOU ADD A TARGET ELEMENT HERE, ALSO UPDATE _BareLayout and _Layout load trigger and RepeatedInputTagHelper's handling!
[HtmlTargetElement("input", TagStructure = TagStructure.NormalOrSelfClosing)]
[HtmlTargetElement("select")]
[HtmlTargetElement("textarea")]
public sealed class InputTagHelper : TagHelper
{
    [HtmlAttributeName("x-label")]
    public string Label { get; set; } = "";

    [HtmlAttributeName("x-label-details")]
    public string LabelDetails { get; set; } = "";

    [HtmlAttributeName("x-label-can-use-markdown")]
    public bool LabelCanUseMarkdown { get; set; }

    [HtmlAttributeName("x-suggestions")]
    public IEnumerable<string>? Suggestions { get; set; }

    [HtmlAttributeName("x-enforce-after")]
    public string EnforceAfterName { get; set; } = "";

    [HtmlAttributeName("x-enforce-usable-when-checked")]
    public string EnforceUsableWhenCheckedName { get; set; } = "";

    [HtmlAttributeName("x-enforce-required-when-unchecked")]
    public string EnforceRequiredWhenUncheckedName { get; set; } = "";

    [HtmlAttributeName("x-enforce-placeholders")]
    public string EnforcePlaceholders { get; set; } = "";

    [HtmlAttributeName("x-max-file-size")]
    public uint? MaxFileSizeInBytes { get; set; }

    [HtmlAttributeName("x-existing-file-id")]
    public string? ExistingFileId { get; set; }

    [HtmlAttributeName("x-existing-file-preview-height")]
    public string? ExistingFilePreviewHeight { get; set; }

    [HtmlAttributeName("x-existing-file-removal-input-name")]
    public string? ExistingFileRemovalInputName { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        bool hasMaxLength = context.AllAttributes.TryGetAttribute("maxlength", out var maxLengthAttr);
        if (Label is not "" || LabelCanUseMarkdown || MaxFileSizeInBytes is not null || hasMaxLength)
        {
            // If it was converted from Markdown it'll be wrapped in a <p>, we don't want that
            Label = Label.Trim();
            if (Label.StartsWith("<p>", StringComparison.OrdinalIgnoreCase) && Label.EndsWith("</p>", StringComparison.OrdinalIgnoreCase) && Label.LastIndexOf("<p>", StringComparison.OrdinalIgnoreCase) == 0)
            {
                Label = Label["<p>".Length..^"</p>".Length];
            }

            // Unlike all other inputs, checkboxes and radios must have their label set after them.
            // We also make an exception when we're going to add stuff to show there's already a file.
            var type = GetAttributeValue(context, "type");
            if (ExistingFileId is not null)
            {
                var id = context.UniqueId;
                output.Attributes.SetAttribute("id", id);
                output.PreElement.AppendHtml($"<label for=\"{id}\">{Label}</label>");
            }
            else if (context.TagName.Equals("input", StringComparison.OrdinalIgnoreCase) && (type.Equals("checkbox", StringComparison.Ordinal) || type.Equals("radio", StringComparison.Ordinal)))
            {
                output.PreElement.AppendHtml("<label>");
                output.PostElement.AppendHtml(Label);
            }
            else
            {
                output.PreElement.AppendHtml("<label>");
                output.PreElement.AppendHtml(Label);
            }

            // Details always go afterwards, even for textboxes
            string details = LabelDetails;
            if (LabelCanUseMarkdown)
            {
                if (details is not "")
                {
                    details += "<br />";
                }
                details += "You can use <a target=\" _blank\" href=\"https://commonmark.org/help/\">Markdown</a>.";
            }
            if (MaxFileSizeInBytes is uint maxSize && maxSize < uint.MaxValue)
            {
                if (details is not "")
                {
                    details += "<br />";
                }
                if (maxSize < 1024 * 1024)
                {
                    details += $"Maximum {Math.Round(maxSize / 1024.0, 0, MidpointRounding.ToZero).ToString(CultureInfo.InvariantCulture)} KB.";
                }
                else
                {
                    details += $"Maximum {Math.Round(maxSize / (1024.0 * 1024.0), 2, MidpointRounding.ToZero).ToString(CultureInfo.InvariantCulture)} MB.";
                }
            }
            if (hasMaxLength)
            {
                if (details is not "")
                {
                    details += "<br />";
                }
                details += $"Maximum {maxLengthAttr.Value} characters.";
            }
            if (details is not "")
            {
                output.PostElement.AppendHtml("<small>" + details + "</small>");
            }

            if (ExistingFileId is null)
            {
                output.PostElement.AppendHtml("</label>");
            }
        }

        if (Suggestions is not null)
        {
            var hash = Suggestions.GetHashCode().ToString(CultureInfo.InvariantCulture); // collisions shouldn't happen... right?
            output.Attributes.SetAttribute("list", $"data-{hash}");
            output.PreElement.AppendHtml($"""
                <datalist id="data-{hash}">
                    {string.Join('\n', Suggestions.Select(s => $"""<option value="{s}"></option>"""))}
                </datalist>
            """);
        }

        // Enforce "required" inputs to also disallow whitespace-only values,
        // and in the case of selects, to have a default value that goes away when something is selected
        if (context.AllAttributes.ContainsName("required"))
        {
            if (context.TagName.Equals("input", StringComparison.OrdinalIgnoreCase))
            {
                if (GetAttributeValue(context, "type").Equals("text", StringComparison.Ordinal))
                {
                    output.Attributes.Add("pattern", ".*\\S.*");
                    output.Attributes.Add("title", "This field cannot be empty.");
                }
            }
            else if (context.TagName.Equals("textarea", StringComparison.OrdinalIgnoreCase))
            {
                AddOnLoad(output, $$"""
                    var textarea = this;
                    var validate = () => {
                        if (textarea.value.trim() === '') {
                            textarea.setCustomValidity('Please enter some text.');
                        } else {
                            textarea.setCustomValidity('');
                        }
                    }
                    textarea.addEventListener('input', validate);
                    textarea.addEventListener('blur', validate);
                """);
            }
            else if (context.TagName.Equals("select", StringComparison.OrdinalIgnoreCase))
            {
                var existing = await output.GetChildContentAsync();
                output.Content.SetHtmlContent("<option hidden disabled selected value> -- select -- </option>\n" + existing.GetContent());
            }
            else
            {
                throw new NotSupportedException($"I don't know how to validate {context.TagName}");
            }
        }

        if (EnforceAfterName is not "")
        {
            // This works as long as the thing being referenced is earlier in the HTML.
            // Note that the submit even won't even trigger if there is custom validity on the elements, so we must clear it on each input
            output.PostElement.AppendHtml($$"""
                <script>
                    for (const form of document.forms) {
                        var startInput = form.elements['{{EnforceAfterName}}'];
                        var endInput = form.elements['{{GetAttributeValue(context, "name")}}'];
                        if (startInput !== undefined && endInput !== undefined) {
                            form.addEventListener('submit', (e) => {
                                var start = new Date(startInput.value);
                                var end = new Date(endInput.value);
                                if (start >= end) {
                                    startInput.setCustomValidity('Start date must be before end date.');
                                    e.preventDefault();
                                    form.reportValidity();
                                }
                            });
                            [startInput, endInput].forEach(input => {
                                input.addEventListener('input', () => {
                                    startInput.setCustomValidity('');
                                });
                            });
                        }
                    }
                </script>
            """);
        }

        if (EnforceUsableWhenCheckedName is not "")
        {
            AddOnLoad(output, $$"""
                var box = this;
                var boxIndex = Array.prototype.indexOf.call(box.form.querySelectorAll("[name='" + this.name + "']"), this);
                var target = box.form.querySelectorAll("[name='{{EnforceUsableWhenCheckedName}}']")[boxIndex];
                var container = target;
                if (target.parentNode.tagName === 'LABEL') {
                    container = target.parentNode;
                }
                var update = () => {
                    container.style.display = box.checked ? 'block' : 'none';
                    if (!box.checked) { target.value = ''; }
                };
                box.addEventListener('change', update);
                update();
            """);
        }

        // Only supported for input & textarea for now
        if (EnforceRequiredWhenUncheckedName is not "")
        {
            AddOnLoad(output, $$"""
                var box = this;
                var boxIndex = Array.prototype.indexOf.call(box.form.querySelectorAll("[name='" + this.name + "']"), this);
                var target = box.form.querySelectorAll("[name='{{EnforceRequiredWhenUncheckedName}}']")[boxIndex];
                box.form.addEventListener('submit', (e) => {
                    if (!box.checked && target.value.trim() === '') {
                        target.setCustomValidity('Please enter some text.');
                        box.form.reportValidity();
                        e.preventDefault();
                    }
                });
                box.addEventListener('input', () => {
                    target.setCustomValidity('');
                });
                target.addEventListener('input', () => {
                    target.setCustomValidity('');
                });
            """);
        }

        if (EnforcePlaceholders is not "")
        {
            var placeholders = EnforcePlaceholders.Split(',', StringSplitOptions.TrimEntries);
            // Same remark re: clearing validity as for EnforceAfterName
            AddOnLoad(output, $$"""
                var input = this;
                input.form.addEventListener('submit', (e) => {
                    if ({{string.Join(" || ", placeholders.Select(p => $"!input.value.includes('{p}')"))}}) {
                        input.setCustomValidity('The text must contain placeholders: {{string.Join(", ", placeholders)}}');
                        input.form.reportValidity();
                        e.preventDefault();
                    }
                });
                input.addEventListener('input', () => {
                    input.setCustomValidity('');
                });
            """);
        }

        // Support multi-valued <select> options through the `data-...` properties
        // e.g., if an option has `data-min-status=0 data-max-status=10` we want the form to send `minStatus=0,maxStatus=10` to the server
        // Note that kebab-case gets turned into camelCase as per the `dataset` standard
        // Assumes all options have the same dataset keys
        if (context.TagName.Equals("select", StringComparison.OrdinalIgnoreCase))
        {
            var content = await output.GetChildContentAsync();
            if (content.GetContent().Contains("data-", StringComparison.Ordinal))
            {
                AddOnLoad(output, $$"""
                    var select = this;
                    select.addEventListener("change", () => {
                        var option = select.selectedOptions[0];
                        for (const [name, value] of Object.entries(option.dataset)) {
                            var input = select.form.querySelector("input[name='" + name + "']");
                            if (!input) {
                                input = document.createElement("input");
                                input.type = "hidden";
                                input.name = name;
                                select.form.appendChild(input);
                            }
                            input.value = value;
                        }
                    });
                """);
            }
        }

        // Don't just support MaxFileSizeInBytes, enforce it, so we can't forget to set it (can always set it to int.MaxValue if needed)
        if (context.TagName.Equals("input", StringComparison.OrdinalIgnoreCase) && GetAttributeValue(context, "type").Equals("file", StringComparison.Ordinal))
        {
            if (MaxFileSizeInBytes is uint maxSize)
            {
                if (maxSize < uint.MaxValue)
                {
                    AddOnLoad(output, $$"""
                        this.addEventListener('change', event => {
                            if (event.target.files && event.target.files[0]) {
                                if (event.target.files[0].size > {{maxSize.ToString(CultureInfo.InvariantCulture)}}) {
                                    event.target.value = '';
                                    showAlert('Error', 'This file exceeds the maximum size of {{(maxSize / 1024).ToString(CultureInfo.InvariantCulture)}} KB. Please select another file.');
                                }
                            }
                        });
                    """);
                }
            }
            else
            {
                throw new InvalidOperationException("Do not create input[type=file] without setting a max file size!");
            }
        }

        if (!string.IsNullOrEmpty(ExistingFileId))
        {
            var id = context.UniqueId;
            output.Attributes.SetAttribute("id", id);
            bool required = output.Attributes.RemoveAll("required");
            output.Attributes.SetAttribute("style", "display: none");

            string removeLine = "";
            if (!string.IsNullOrEmpty(ExistingFileRemovalInputName))
            {
                output.PreElement.AppendHtml($"""
                    <input id="remove-{id}"
                           name="{ExistingFileRemovalInputName}"
                           type="hidden" />
                """);
                removeLine = $"document.getElementById('remove-{id}').value = 'true';";
            }

            output.PreElement.AppendHtml(
                $"<fieldset id='existing-{id}'>"
            );
            if (string.IsNullOrEmpty(ExistingFilePreviewHeight))
            {
                output.PreElement.AppendHtml($"""
                    <p>Already provided. (<a href="/File/{ExistingFileId}" download>Download</a>)</p>
                """);
            }
            else
            {
                output.PreElement.AppendHtml($"""
                    <img src="/File/{ExistingFileId}"
                         alt="Existing file"
                         style="height: {ExistingFilePreviewHeight}" />
                """);
            }
            output.PreElement.AppendHtml($"""
                    <a href="#" onclick="document.getElementById('existing-{id}').style.display = 'none';
                                         {removeLine}
                                         document.getElementById('{id}').style.display = 'revert';
                                         document.getElementById('{id}').required = {(required ? "'required'" : "null")};
                                         return false;">Remove</a>
                </fieldset>
            """);
        }
    }

    private static string GetAttributeValue(TagHelperContext context, string name)
        => context.AllAttributes.TryGetAttribute(name, out var typeAttr) && typeAttr.Value is HtmlString { Value: string typeVal } ? typeVal : "";

    private static void AddOnLoad(TagHelperOutput output, string handler)
    {
        if (output.Attributes.TryGetAttribute("onload", out var existing))
        {
            output.Attributes.SetAttribute("onload", existing.Value + "\n" + handler);
        }
        else
        {
            output.Attributes.SetAttribute("onload", handler);
        }
    }
}