using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager.AdminPages;

public sealed class BackupPage(Db db, EventDetails eventDetails, ProfileForm profileForm, FileStorage fileStorage, TimeProvider timeProvider) : Page<Admin>
{
    public override async Task<PageView> ViewAsync(Admin admin)
    {
        if (admin.IsOwner)
        {
            return EditableView("Backup", "Import / Export");
        }
        return ForbiddenView();
    }

    public async Task<File> ExportParticipantsCsvAsync()
    {
        var result = new CsvWriter();
        var groupIds = new Dictionary<ApplicationGroup, int>();

        // First column is an index, to disambiguate participants with the same name when we emit the files
        result.WriteField("#");
        result.WriteField("Email address");
        result.WriteField("Given name");
        result.WriteField("Family name");
        result.WriteField("Status");
        result.WriteField("Soft rejected?");
        foreach (var choice in profileForm.Choices)
        {
            if (!choice.IsRequiredSingleOption)
            {
                result.WriteField(choice.Name);
            }
        }
        result.WriteField("Admin remarks");
        result.WriteField("Referrer");
        result.WriteField("Application Group #");
        result.NextRow();

        var participants = await db.Participants.ToCollectionAsync();
        var applicationGroups = await db.ApplicationGroups.ToCollectionAsync();

        foreach (var (index, participant) in OrderParticipants(participants))
        {
            if (participant.Status == ParticipantStatus.Created)
            {
                // Ignore participants who haven't confirmed their email address yet
                continue;
            }

            result.WriteField(index.ToString(CultureInfo.InvariantCulture));
            result.WriteField(participant.EmailAddress);
            result.WriteField(participant.GivenName);
            result.WriteField(participant.FamilyName);
            result.WriteField(participant.Status.ToDisplayString());
            result.WriteField(participant.IsSoftRejected ? "true" : ""); // don't pollute the sheet for a rare case
            foreach (var choice in profileForm.Choices)
            {
                if (!choice.IsRequiredSingleOption)
                {
                    result.WriteField(participant.Profile.GetValueOrDefault(choice.Name, ""));
                }
            }
            result.WriteField(participant.AdminRemarks);
            result.WriteField(participant.Referrer);

            var group = applicationGroups.FirstOrDefault(g => g.Members.Contains(participant));
            if (group is null)
            {
                result.WriteField("");
            }
            else
            {
                if (!groupIds.TryGetValue(group, out int groupId))
                {
                    groupId = groupIds.Count;
                    groupIds.Add(group, groupId);
                }
                // prefix with G just so they're in a different "namespace" than the index column
                result.WriteField("G" + groupId.ToString(CultureInfo.InvariantCulture));
            }

            result.NextRow();
        }

        return result.ToFile("participants");
    }

    public async Task<File> ExportParticipantFilesAsync(bool onlyConfirmed)
    {
        var participants = await db.Participants.ToCollectionAsync();

        using var memoryStream = new System.IO.MemoryStream();
        await using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (index, participant) in OrderParticipants(participants))
            {
                if (onlyConfirmed && participant.Status < ParticipantStatus.Confirmed)
                {
                    continue;
                }
                foreach (var file in profileForm.Files)
                {
                    if (participant.Profile.TryGetValue(file.Name, out var id))
                    {
                        var storedFile = await fileStorage.GetFileAsync(id)
                                      ?? throw new InvalidOperationException($"File {id} not in storage for {participant.FullName}"); // should never happen

                        string extension = storedFile.Name.LastIndexOf('.') is int n and >= 0 ? storedFile.Name[n..] : "";
                        string fullName = $"{index.ToString(CultureInfo.InvariantCulture)} {participant.GivenName} {participant.FamilyName} {file.Name}{extension}";
                        var entryFile = archive.CreateEntry(fullName);

                        await using var entryStream = await entryFile.OpenAsync();
                        await storedFile.CopyToAsync(entryStream);
                    }
                }
            }
        }

        return new File.InMemory("files.zip", "application/zip", memoryStream.ToArray());
    }

    public async Task<File> ExportAsync()
        => await Backup.ExportAsync(db, fileStorage) with
        {
            Name = $"{timeProvider.GetUtcNow().ToString("yyyy-MM-ddTHH-mm-ss", CultureInfo.InvariantCulture)}-{eventDetails.Title}{Backup.FileExtension}"
        };

    public async Task<StatusMessage> ImportAsync(Admin admin, File backup)
    {
        await Backup.ImportAsync(backup, db, fileStorage);

        // We do not want to end up in a situation where someone restores their account away.
        var newAdmin = await db.Admins.FindAsync(admin.EmailAddress);
        if (newAdmin is null)
        {
            db.Admins.Add(admin);
        }
        else
        {
            newAdmin.IsOwner = true;
        }

        return Success("System restored from the backup.");
    }

    // Use a human-friendly index starting at 1, not 0
    private static IEnumerable<(int, Participant)> OrderParticipants(IEnumerable<Participant> participants)
        => participants.OrderByName()
                       .Index()
                       .Select(t => (t.Index + 1, t.Item));

    // Very basic implementation of https://www.rfc-editor.org/rfc/rfc4180
    private sealed class CsvWriter
    {
        // Force a Unicode BOM so Excel understands this is Unicode
        // But remember the doc: "Note that the GetBytes method does not prepend a BOM to a sequence of encoded bytes;
        //                        supplying a BOM at the beginning of an appropriate byte stream is the developer's responsibility."
        private static readonly Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        private readonly StringBuilder _builder = new();

        public void WriteField(string? value)
        {
            if (value is null)
            {
                _builder.Append(',');
            }
            else
            {
                // always escape, it's simpler
                _builder.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append("\",");
            }
        }

        public void NextRow()
        {
            // remove the last ',' first
            _builder.Remove(_builder.Length - 1, 1);
            _builder.Append("\r\n");
        }

        public File.InMemory ToFile(string name)
            => new(name + ".csv", "text/csv;header=present", [.. _encoding.GetPreamble(), .. _encoding.GetBytes(_builder.ToString())]);
    }
}