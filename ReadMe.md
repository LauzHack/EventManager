# LauzHack Event Manager

Website to manage an event such as a hackathon, including application in groups with a customizable form,
acceptance confirmation, project submission, mass email, and more.

See [Using.md](./Using.md) for the short setup instructions, as well as tips and FAQs.

## Goals

- **Easy to use for applicants**.
  Each step of the process is clear and well-documented, with reminders when necessary.
  Nobody fails to apply due to misunderstandings or forgetfulness.

- **Easy to deploy and setup for admins**.
  No extra database to setup, just one single web app in a widely-supported framework.

- **Easy to maintain for IT staff**.
  Well-known programming language, many automated tests, proper modularization.

- **Cheap to run**.
  Can be hosted on a very cheap cloud machine, such as Azure's Linux B1 ($13/mo).
  In combination with a cheap email provider, such as Brevo's "starter" marketing ($9/mo), that's <$100 per event.
  (To plan your email plan: LauzHack 2025 had 1,200 applications and sent a little over 5,000 emails in the single month applications were open)

## Features

- **Clear instructions**:
  Every bit of text in the system, from the application page to the confirmation email text, has been refined based on reactions and feedback from real users, to minimize confusion and time spent answering participant questions.

- **Group applications**:
  Optionally let people apply in groups, so that if their friends are accepted they are too.

- **Custom application form**:
  Add custom questions, including free text, multiple choices, required checkboxes, and file uploads.

- **Confirmations and reminders**:
  Applicants must confirm once they're accepted, and receive reminders if they delay this confirmation.

- **Project submission**:
  Optionally let participants submit their project, all of which are then displayed in a public projects gallery that can be easily exported.

- **Visa invitation letter management**:
  Optionally let participants request a visa invitation letter, providing a customizable set of information, allowing admins to generate a letter.

- **Travel reimbursement management**:
  Optionally let participants submit their travel expenses, including shared ones and per-region caps, and have admins accept or reject each expense then get a table of who is owed how much.
  If project submissions are enabled, optionally mark participants as having demoed if you want to only reimburse those.

- **Referral tracking**:
  Use the standard `?utm_source=...` URL parameters when sending the signup URL and you will see which participants came from where.

- **Soft rejection**:
  Mark applicants as "soft rejected" to ensure they cannot be accepted without letting them know until all rejections are sent.

- **Alias checks**:
  If the same person applies with different email addresses, the system will recognize their name and ask if they want to migrate to the first email address they applied with.

- **System export/import**:
  Export participant data as a CSV and files as a ZIP archive, or the entire system as a backup file, and restore the system from a backup file if needed.

## Development

Feedback and pull requests welcome!

See [Contributing.md](./Contributing.md) for how to start contributing, instructions on how to set up and run the project locally, as well as documentation on the project's internals.

Keep in mind [LauzHack's usual rules on conduct](https://lauzhack.com/rules) apply to this repository.
