using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EventManager.Web.TagHelpers;

[HtmlTargetElement("x-repeated-input")]
public sealed partial class RepeatedInputTagHelper : TagHelper
{
    [GeneratedRegex("name=\"(?<name>[^\"]*)\"", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NamesRegex { get; }

    public string Label { get; set; } = "";

    public IEnumerable? ExistingItems { get; set; }

    public bool StackVertically { get; set; }

    public bool EnableDragDrop { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (EnableDragDrop)
        {
            // Annoyingly, HTML/JS make a distinction between *attribute values* and *properties*.
            // Because our drag-n-drop code works by using the dragged elements' `innerHTML`,
            // we must sync what the user has input with what's really in the HTML.
            output.PostElement.AppendHtml("""
                <script>
                    function sync(root) {
                        root.querySelectorAll("input, textarea, select").forEach(el => {
                            if (el instanceof HTMLInputElement) {
                                switch (el.type) {
                                    case "checkbox":
                                    case "radio":
                                        el.toggleAttribute("checked", el.checked);
                                        break;
                                    default:
                                        el.setAttribute("value", el.value);
                                }
                            } else if (el instanceof HTMLTextAreaElement) {
                                el.textContent = el.value;
                            } else if (el instanceof HTMLSelectElement) {
                                el.querySelectorAll("option").forEach(option => {
                                    option.toggleAttribute("selected", option.selected);
                                });
                            }
                        });
                    }
                    var draggedElement;
                    function handleMouseDown(e) {
                        e.target.parentNode.setAttribute('draggable', 'true');
                    }
                    function handleMouseUp(e) {
                        e.target.parentNode.setAttribute('draggable', 'false');
                    }
                    function getContainer(e) {
                        let target = e.target;
                        while (target) {
                            if (target.tagName === "FIELDSET") {
                                return target;
                            }
                            target = target.parentNode;
                        }
                        return null;
                    }
                    function containerDragStart(e) {
                        draggedElement = getContainer(e);
                        sync(draggedElement);
                        e.dataTransfer.effectAllowed = 'move';
                        e.dataTransfer.setData('text/html', draggedElement.innerHTML);
                    }
                    function containerDragOver(e) {
                        e.preventDefault();
                        e.dataTransfer.dropEffect = 'move';
                        getContainer(e).classList.add("over");
                    }
                    function containerDragLeave(e) {
                        getContainer(e).classList.remove("over");
                    }
                    function containerDragEnd(e) {
                        getContainer(e).setAttribute('draggable', 'false');
                    }
                    function containerDrop(e) {
                        let container = getContainer(e);
                        if (container === null) {
                            return;
                        }
                        sync(container);
                        draggedElement.innerHTML = container.innerHTML;
                        container.innerHTML = e.dataTransfer.getData('text/html');
                        container.classList.remove("over");
                    }
                </script>
                <style>
                    .over {
                        border: 0.2vh dashed black;
                        opacity: 0.5;
                    }
                </style>
            """);
        }

        var id = Guid.NewGuid().ToString("N"); // all digits, no hyphens, so it's a valid CSS and JS identifier suffix
        var content = await output.GetChildContentAsync();
        var contentAsString = content.GetContent();

        var contentNames = NamesRegex.Matches(contentAsString).Cast<Match>()
                .Select(m => m.Groups["name"].Value)
                .Select(s => (Name: s, Id: s.Replace(".", "", StringComparison.Ordinal)))
                .ToArray();

        output.TagName = "fieldset";
        output.Attributes.Add("role", "group");
        output.Content.SetHtmlContent($"""
            <p>{Label}</p>
            <button type="button" onclick="add{id}({string.Join(", ", contentNames.Select(_ => "null"))})">Add</button>
        """);
        var (dragHandle, dragAttributes) = EnableDragDrop
            ? ($"""<p style="cursor: move; padding: 0 1em; {(StackVertically ? "float: right; margin: 0;" : "")}" onmousedown="handleMouseDown(event)" onmouseup="handleMouseUp(event)">⋯</p>""",
               """
               ondragstart="containerDragStart(event)"
               ondragend="containerDragEnd(event)"
               ondragleave="containerDragLeave(event)"
               ondragover="containerDragOver(event)"
               ondrop="containerDrop(event)"
               class=""
               """)
            : ("", "");
        // For an explanation of why we trigger a 'load' on each input after adding it, see InputTagHelper
        output.PostElement.AppendHtml($$"""
            <fieldset id="container-{{id}}"></fieldset>
            <template id="template-{{id}}">
                <fieldset {{dragAttributes}} role="{{(StackVertically ? "generic" : "group")}}">
                    {{dragHandle}}
                    {{contentAsString}}
                    <button type="button" style="{{(StackVertically ? "float: right" : "")}}" onclick="delete{{id}}(this)">Delete</button>
                </fieldset>
            </template>
            <script>
                function setFormItem(clone, name, value) {
                    var el = clone.querySelector('[name="' + name + '"]');
                    if (el instanceof HTMLInputElement) {
                        switch (el.type) {
                            case "checkbox":
                            case "radio":
                                el.checked = value;
                                break;
                            default:
                                el.value = value;
                        }
                    } else if (el instanceof HTMLTextAreaElement) {
                        el.textContent = value;
                    } else if (el instanceof HTMLSelectElement) {
                        el.value = value;
                    }
                }
                function add{{id}}({{string.Join(", ", contentNames.Select(p => p.Id))}}) {
                    var container = document.getElementById('container-{{id}}');
                    var template = document.getElementById('template-{{id}}');
                    var clone = template.content.cloneNode(true);
                    {{string.Join('\n', contentNames.Select(p => $"setFormItem(clone, '{p.Name}', {p.Id});"))}}
                    container.appendChild(clone);
                    //setTimeout(() =>
                    container.querySelectorAll('input, textarea, select').forEach(e => e.dispatchEvent(new Event('load')));//, 10);
                }
                function delete{{id}}(elem) {
                    var container = document.getElementById('container-{{id}}');
                    container.removeChild(elem.parentNode);
                }
            </script>
        """);

        if (ExistingItems is not null)
        {
            output.PostElement.AppendHtml("<script>");
            // Easier than using reflection to detect KeyValuePair...
            if (ExistingItems is IDictionary dict)
            {
                foreach (var key in dict.Keys)
                {
                    output.PostElement.AppendHtml($"add{id}({Encode(key)}, {Encode(dict[key]!)});");
                }
            }
            else
            {
                foreach (var item in ExistingItems)
                {
                    output.PostElement.AppendHtml($"add{id}({Encode(item)});");
                }
            }
            output.PostElement.AppendHtml("</script>");
        }
    }

    private static string Encode(object existingItem)
    {
        static string EncodeCore(object item) => item switch
        {
            true => "true",
            false => "false",
            IEnumerable<string> strs => "'" + string.Join("\\n", strs.Select(EncodeCore)) + "'",
            string str => JsonSerializer.Serialize(str),
            IFormattable n => n.ToString(null, CultureInfo.InvariantCulture),
            _ => throw new ArgumentException("I don't know how to encode: " + item, nameof(item))
        };
        if (existingItem is IEnumerable<object> many)
        {
            return string.Join(", ", many.Select(EncodeCore));
        }
        if (existingItem.GetType().GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance) is not null)
        {
            return string.Join(", ", existingItem.GetType().GetProperties().Where(p => p.GetSetMethod() != null).Select(p => p.GetGetMethod()!.Invoke(existingItem, [])!).Select(EncodeCore));
        }
        return EncodeCore(existingItem);
    }
}