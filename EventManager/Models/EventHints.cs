using System.Collections.Immutable;

namespace EventManager.Models;

/// <summary>
/// Collection of hints about event processes, intended to help participants know what to expect.
/// </summary>
/// <param name="ApplicationHints">Hints about the application process, in addition to standard ones.</param>
/// <param name="PresentationHints"> Hints about project presentations.</param>
public sealed record EventHints(ImmutableArray<Hint> ApplicationHints, string PresentationHintsHeader, ImmutableArray<Hint> PresentationHints)
{
    public static readonly EventHints Default = new(
        [
            new("🌈", "**Don't hesitate** to apply!", "Whether you're a beginner or a veteran, this event is for you.")
        ],
        "Presentation Tips",
        [
            new("🎯", "**Focus** on what matters, you have little time.", "Do not spend time introducing yourself or discussing your background."),
            new("🪄", "Start with the **main cool idea** of your project, usually as a question.", "For instance, \"Have you ever been in a situation where… ?\" or \"Do you ever wish you could… ?\""),
            new("🧪", "**Show**, don't tell.", "Demo the coolest features of your project in real time as early in the presentation as possible."),
            new("⏳", "**Save time** where possible by preparing inputs in advance.", "For instance, if you need to write a long text, have it ready and only copy-paste it during the demo."),
            new("🗃️", "Have a **backup plan** if your demo involves something beyond your control, like hardware.", "For instance, if you're scanning QR codes, add a demo button that loads a specific code, in case the scanner fails."),
            new("🥳", "Have **fun**! No need to be formal.", "Judging is just one small part of the hackathon, and the jury knows you're tired after all the hard work.")
        ]
    );
}