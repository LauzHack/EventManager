Contributions welcome!
Keep in mind [LauzHack's usual rules on conduct](https://lauzhack.com/pdf/rules.pdf) apply to this repository.

## Getting started

**For feedback**:
Open an issue to discuss anything you'd like to tell us, such as wording that could be clarified based on real-world experience, feature requests, or questions about the documentation.
_Please understand that we have limited bandwidth and may close requests for large features as "won't fix" if nobody volunteers to do them or if we believe they would not be a good fit for the system._

**For small issues**:
To fix a typo, improve a button, or other such changes that can be trivially reviewed in a minute or two, open a pull request directly.

**For large issues**:
Anything that requires thought into design and implementation _must_ begin as an issue so it can be discussed with the maintainers.
We will gladly help you figure out where a piece of logic should go, which parts of the code can be reused, and so on.
_However, like general feedback, please understand we may not accept large features that we would not have the bandwidth to maintain._

**Working on an existing issue**:
Please state your interest in a comment on the issue so maintainers know who's working on what and can provide a time frame for when there will be time to review.

## Contribution rules

Only open pull requests if you have the time and interest to see them through.
It is perfectly acceptable to open draft requests if you have specific questions, such as "is this the right place for this piece of code?" or "what other tests should be added?".
However, we cannot accept "just asking questions"-type pull requests such as asking an LLM to draft an implementation without being able to explain the resulting code or being willing to polish the implementation until it's maintainable.

Because this is a volunteer-maintained project, we will close without further review PRs that do not pass basic sanity checks, such as compiling, having a set of changes related to the PR description,
not changing unrelated code, and not blatantly breaking the system architecture and general principles described below.
We may, at our discretion, issue temporary or permanent bans from this repository starting from the second offense.

All conversation, including pull request descriptions, must be human-written.
Even if English isn't your first language, we'd rather read your own voice.

We will not accept contributions where the code is mostly LLM-written.
You may use LLMs to understand the codebase and write small bits of code, such as figuring out what CSS selector to use.
You are still responsible for all code you submit, and we will close PRs whose author clearly does not understand their contents.

## Running locally

You need the latest stable .NET version, installable [here](https://dotnet.microsoft.com/en-us/download).

Ensure you have a `HOME` environment variable pointing to a path where the database and files will be stored.
(This name is used because that's where Azure keeps its durable storage)

The easiest option if you're on Windows is to get [Visual Studio's free Community edition](https://visualstudio.microsoft.com/vs/community/), set the web project as the startup project, and run it from there.
You can then easily debug it using breakpoints.

Otherwise, you can run it from the command line:

```
dotnet run --project EventManager.Web/EventManager.Web.csproj
```

When run in debug configuration, which is the default, you do not need an email service and can write whatever you want as the email configuration during setup.
Emails are instead printed to the console.

Furthermore, in debug configuration the system prints an admin link to the console once you've setup an admin, for convenience.

## System architecture

The system models interactions with each kind of user as a linear flow of pages.
Users are directed to the first page they are required to interact with, which depends on their state and the event state.
For instance, participants begin by having to provide their email, then once that's done they must provide their name, and so on.
Every request re-traverses all pages, so there is no need to maintain separate state for which page must be shown to who.

If you'd like to understand the system, start by looking at `Page` and `Operation` in `EventManager/EventManager.Abstractions`,
then look at the `EventManager/EventManager.Models` folder, then read `EventManager/EventManagerSystem.cs`,
which includes the order of all pages.

The system uses dependency injection: pages take their dependencies as constructor arguments,
and methods take their inputs as well as the current user as argument.

The core of the system is completely independent from database, web, and test technologies. It has no external dependencies.

## Human factors

The most important feature of this system is to be as clear as possible.
In an event with 100s of applicants, small things that confuse a few % of participants each quickly add up to a lot of questions.

Every bit of text in the system has been progressively refined to be as clear as possible.
This sometimes looks exaggerated, such as some email text being both bolded and highlighted in bright yellow, but it works.

We will happily accept changes to make processes even clearer, but these need to be backed up by real experience.
Please do not suggest small edits just because you think this or that word would look better.

## Threat model

- It is acceptable to disclose that a specific email address input by a participant is already in the database.
- It is _not_ acceptable to disclose the entire list of email addresses in the database.
- It is _not_ acceptable to disclose any information from other participants to a participant aside from their name, unless they apply or submit a project together.
- User-uploaded files are _untrusted_ and may contain JS to, say, attempt to exfiltrate cookies.
  Every "view a user-uploaded file" link must therefore download the file, not display it.
- Email addresses are assumed to be case-insensitive for ease of use, even though that is technically not true.
- Users within an application group or project team are assumed to act in good faith and not be hostile toward each other.
- Admins are assumed to act in good faith and not be malicious, with "ownership rights" existing to avoid human error rather than defend against a malicious admin.
- File, database, and email operations are assumed to not spuriously fail.
  This is reasonable given that files are stored locally, the database uses SQLite, and emails are intended to use a professional email service.
  The kind of architecture necessary to sync all three into atomic operations would be overkill for such a simple piece of software.

## Tests

The backend has no global variables, i.e., C# mutable `static` variables, nor accesses to such variables declared in libraries.
This ensures the backend is testable, and indeed we test it extensively. We enforce **100% line and branch coverage for tests** on the core.
The only exception is the strong random number generation for the system's private key, used to authenticate users.

Important scenarios are also tested end-to-end in `EndToEndTests.cs` at the root of the tests folder.

Tests for the pages are more like integration tests than unit tests, since they use a real database implementation.
This is because it's otherwise too easy to write code that looks like it works in unit tests but fails due to ORM translation quirks.

It's only OK to create "fake" tests that cover code in convoluted ways if we do not believe the code can be hit at runtime, such as the fallback for the case where there is neither a user nor a page to display, which we know should be impossible.
Ideally, such cases would not exist, so contributions that refactor the codebase to avoid them are welcome, though not if it lowers readability or maintainability.

## Technical details


## Input validation

Both the frontend and the backend validate user inputs.
The goal is that backend errors should only reach a frontend user if something outside of their control changed.
For instance, trying to accept an invitation when the inviter has withdrawn it will lead to a backend error being displayed in the frontend.
However, backend errors of the type "your input is intrinsically invalid and should not have been submitted, regardless of the state of the system" should never reach a frontend user.
This also simplifies the system since there is no support for restoring whatever invalid data the user submitted so they can edit it after a backend error.

## Frontend code

General guidelines:
- Use pure HTML if at all possible on modern browsers
- Write semantic markup that gets styled automatically by Pico, with custom CSS only if truly necessary
- Avoid writing JavaScript unless there is no other option for good UX

Typical page structure:
```html
<section>
  <p>
    Description of the first (or only) section; call out the sections below
    if users may not realize they're there, e.g., "You can also... below".
  </p>
  ...
</section>
<section>
  <h2>Second section heading</h2>
  <p>
    Description of the second section.
  </p>
  <p>
    Second paragraph, usually not needed but this is an example.
  </p>
  ...
</section>
...
```

We use ASP.NET tag helpers extensively to make the views more declarative and centralize helper code.
This includes setting defaults, such as `method="post"` for all forms.
Look at existing views to see examples.

Some more specific markup guidelines:
- Use `<fieldset role="group">` for simple cases of grouping fields horizontally
- Avoid `<div>` and `<span>` unless you don't have a choice
- Only use `<br />` inside `<p>`, and only when multiple paragraphs would not look good
- Use `<em>`, not `<i>`
- Use `<strong>`, not `<b>`
- Do not use `<hr />`
