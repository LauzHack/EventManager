using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;

namespace EventManager;

/// <summary>
/// Event management system implementation.
/// </summary>
/// <typeparam name="TRequest">Type of the requests.</typeparam>
[SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "At module boundary")]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Main system module")]
public abstract class EventManagerSystem<TRequest>
{
    // DO NOT CHANGE THE ORDER OF TASKS UNLESS YOU ARE ABSOLUTELY CERTAIN YOU KNOW WHAT YOU ARE DOING!
    // Any task that changes participants' status must come before any task that checks it.
    private static readonly List<Type> TaskTypes = [
        typeof(PeriodicTasks.ConfirmationDelayEnforcementTask),
        typeof(PeriodicTasks.FinalizationReminderTask),
        typeof(PeriodicTasks.AcceptanceReminderTask)
    ];

    // DO NOT CHANGE THE ORDER OF PAGES UNLESS YOU ARE ABSOLUTELY CERTAIN YOU KNOW WHAT YOU ARE DOING!
    private static readonly SystemPages Pages = new(new Dictionary<Type, IReadOnlyCollection<Type>> {
        { typeof(Participant), [// Exceptional flow:
                                typeof(ParticipantPages.RejectedPage),
                                typeof(ParticipantPages.DisabledPage),
                                typeof(ParticipantPages.WithdrawnPage),
                                // Basic flow:
                                typeof(ParticipantPages.EmailPage),
                                typeof(ParticipantPages.NamePage),
                                typeof(ParticipantPages.AliasPage),
                                typeof(ParticipantPages.ProfilePage),
                                typeof(ParticipantPages.GroupPage),
                                typeof(ParticipantPages.WaitForAcceptancePage),
                                // If visa invitation letters are enabled:
                                typeof(ParticipantPages.VisaInvitationLetterPage),
                                // Always, with more features if travel reimbursement is enabled:
                                typeof(ParticipantPages.TravelPage),
                                // Mostly to catch users who weren't properly checked in:
                                typeof(ParticipantPages.WaitForCheckInPage),
                                // If projects are enabled:
                                typeof(ParticipantPages.ProjectPage),
                                typeof(ParticipantPages.ProjectTeamPage),
                                // Otherwise:
                                typeof(ParticipantPages.WelcomePage)] },

        { typeof(ChallengeSetter), [typeof(ChallengeSetterPages.DescriptionPage),
                                    typeof(ChallengeSetterPages.JudgingPage)] },

        { typeof(Admin), [// Mandatory setup
                          typeof(AdminPages.EmailSetupPage),
                          typeof(AdminPages.EventDetailsPage),
                          typeof(AdminPages.EventLimitsPage),
                          typeof(AdminPages.EventThemePage),
                          typeof(AdminPages.ProfileFormPage),
                          // Optional setup and related pages
                          typeof(AdminPages.EventHintsPage),
                          typeof(AdminPages.LetterDataPage),
                          typeof(AdminPages.VisaInvitationLettersPage),
                          typeof(AdminPages.TravelReimbursementPolicyPage),
                          typeof(AdminPages.TravelExpensesPage),
                          typeof(AdminPages.TravelReimbursementPage),
                          // User and project management
                          typeof(AdminPages.AdminsPage),
                          typeof(AdminPages.ParticipantsPage),
                          typeof(AdminPages.ProjectsPage),
                          typeof(AdminPages.ChallengesPage),
                          // Utility
                          typeof(AdminPages.MassEmailPage),
                          typeof(AdminPages.AuditPage),
                          typeof(AdminPages.BackupPage),
                          // Event management workflow
                          typeof(AdminPages.OpenApplicationsPage),
                          typeof(AdminPages.AcceptancePage),
                          typeof(AdminPages.StartCheckInPage),
                          typeof(AdminPages.CheckInPage),
                          typeof(AdminPages.EndPage)] }
    });

    private readonly AsyncReaderWriterLock _lock = new();
    private Uri? _baseUri;
    private bool definitelyHasAdmins;

    /// <summary>
    /// Runs the system for the given request.
    /// </summary>
    public async Task RunAsync(TRequest request)
    {
        try
        {
            // Start by finding the URI of the request.
            var uri = GetUri(request);
            if (uri is null)
            {
                // If the request is ignored by the underlying system, we're done.
                return;
            }

            // Set the URI used as base URI for emails, so that periodic tasks can use it; overriding it is not a problem.
            _baseUri = new Uri(uri.GetLeftPart(UriPartial.Authority));

            // Parse the operation from the URI and add any extra arguments from the request.
            var operation = Operation.Parse(uri) + await ParseExtraArgumentsAsync(request);

            await SerializeAsync(operation.IsReadOnly, _baseUri, async (deps, callback) =>
            {
                // Special case: if we haven't configured any admins yet, the default operation is the admin page view.
                // We cache this because hitting the DB every time for a property that will become true and never become false again is wasteful.
                if (operation.IsDefault)
                {
                    if (!definitelyHasAdmins)
                    {
                        definitelyHasAdmins = await deps.Database.Admins.CountAsync() > 0;
#if DEBUG
                        // For debugging convenience, print an admin login link to the console
                        if (deps.Configuration.AuthenticationSecret is not null && deps.Database.Admins.FirstOrDefault() is Admin admin)
                        {
                            var link = Authenticator.AddAuthentication(deps.Configuration.AuthenticationSecret, Operation.CreatePageView<Admin>(), admin.EmailAddress);
                            Console.WriteLine($"ADMIN LINK: {uri.GetLeftPart(UriPartial.Authority)}{link.RelativeUri}");
                        }
#endif
                    }
                    if (!definitelyHasAdmins)
                    {
                        operation = Operation.CreatePageView<Admin>();
                    }
                }

                // Authenticate the request if possible.
                // "Possible" here means authentication is available and the request has correct auth information.
                User? user = null;
                if (deps.Configuration.AuthenticationSecret is not null)
                {
                    var clientSideStorage = GetClientSideStorage(request);
                    var userId = Authenticator.LogUserIn(deps.Configuration.AuthenticationSecret, operation, clientSideStorage);
                    if (userId is not null)
                    {
                        user = await FindUserAsync(deps.Database, operation.UserType, userId);
                    }
                }

                // Finally, execute the operation.
                var result = await operation.ExecuteAsync(user, Pages, deps);
                // In the special case of an action, we must re-execute an operation to know what to display.
                // (this is where we'd short-circuit if we had a "request but don't display a new view" mode, e.g., if the frontend wanted to call the backend via JS)
                if (result is OperationResult.Action action)
                {
                    // Before executing the new operation, we must commit so the DB state is persisted even if displaying the view fails.
                    await deps.Database.CommitAsync();

                    if (user is Admin && !action.View.IsRequired)
                    {
                        // For admin actions on non-required pages, we display the same page again, so that admins can do many operations on the same page
                        // (e.g., if an admin marks a project as having demoed, they likely want to continue marking other projects)
                        result = await Operation.CreatePageView(action.View.Page)
                                                .ExecuteAsync(user, Pages, deps);
                    }
                    else
                    {
                        // For all other actions, we display the default page for the user.
                        result = await Operation.CreatePageView(operation.UserType)
                                                .ExecuteAsync(user, Pages, deps);
                    }

                    // Importantly, we must use the action's status and message in the view, so that the user knows the result of their action.
                    result = result with { Status = action.Status, Message = action.Message };
                }

                // If something interesting happened, we should log it.
                await callback(result.Status is Status.None ? null : new AuditMessage(result.Status, result.Message, user?.Id, operation.ToString(), deps.TimeProvider.GetUtcNow()));

                // The underlying system can then handle the result of the operation, which cannot be an action result at this point.
                await HandleOperationResultAsync(result, request);
            });
        }
        catch (Exception e)
        {
            await HandleOperationResultAsync(new OperationResult.SystemError(e), request);
        }
    }

    /// <summary>
    /// Runs all periodic tasks once.
    /// This method should be wrapped in a loop that uses <see cref="PeriodicTask.Period" />.
    /// </summary>
    public async Task RunPeriodicTasksAsync()
    {
        // Tasks cannot meaningfully run until we have the base URI, which is necessary to send links via email.
        // Setting it happens on every request, so this really only means tasks won't run before the system has received its first request.
        if (_baseUri is null)
        {
            return;
        }

        foreach (var taskType in TaskTypes)
        {
            await SerializeAsync(isReadOnly: false, _baseUri, async (deps, callback) =>
            {
                var periodicTask = await deps.CreatePeriodicTaskAsync(taskType);
                var result = await periodicTask.RunAsync();
                await callback(result is null ? null : new AuditMessage(Status.Success, result, null, taskType.Name, deps.TimeProvider.GetUtcNow()));
            });
        }
    }

    /// <summary>
    /// Gets the URI of the given request, or null if the request needs no handling.
    /// </summary>
    protected abstract Uri? GetUri(TRequest request);

    /// <summary>
    /// Parses extra arguments from the given request that are not in the request URI.
    /// </summary>
    protected abstract Task<OperationArguments> ParseExtraArgumentsAsync(TRequest request);

    /// <summary>
    /// Gets client-side storage for the given request.
    /// </summary>
    protected abstract ClientSideStorage GetClientSideStorage(TRequest request);

    /// <summary>
    /// Handles the given operation result, which is never <see cref="OperationResult.Action" />, for the given request.
    /// Should do nothing if called a second time, which can happen if the first call fails.
    /// </summary>
    protected abstract Task HandleOperationResultAsync(OperationResult result, TRequest request);

    /// <summary>
    /// Indicates whether the given exception should be logged.
    /// This MUST NEVER THROW!
    /// </summary>
    protected virtual bool ShouldLogException(Exception e)
        => e is not OperationCanceledException;

    /// <summary>
    /// Creates a database instance.
    /// </summary>
    protected abstract Db CreateDb();

    /// <summary>
    /// Creates a file storage instance.
    /// </summary>
    protected abstract FileStorage CreateFileStorage();

    /// <summary>
    /// Creates an email sender, given the system's base URI as well as the system configuration.
    /// </summary>
    protected abstract EmailSender CreateEmailSender(Uri baseUri, Config config);

    /// <summary>
    /// Creates a time provider instance.
    /// </summary>
    protected abstract TimeProvider CreateTimeProvider();

    /// <summary>
    /// Serializes the given function, ensuring reader-writer locking semantics, i.e., any number of calls with `isReadOnly = true` or a single writer can run concurrently.
    /// The function is provided with dependencies and a callback, which it MUST call after it is done using the database.
    /// </summary>
    /// <remarks>
    /// This unfortunate callback scheme is necessary because if `function` returned an audit message and this method logged said message,
    /// this method would have to do the database commit after `function` has fully executed.
    /// Thus, it would be possible for `function` to execute successfully, sending a success page to the user,
    /// only for the database commit to fail, thus not persisting changes that the user believes were persisted.
    /// </remarks>
    private async Task SerializeAsync(bool isReadOnly, Uri baseUri, Func<SystemDependencies, Func<AuditMessage?, Task>, Task> function)
    {
        using var lockScope = await (isReadOnly ? _lock.EnterReaderLockAsync() : _lock.EnterWriterLockAsync());

        // If creating the DB fails, there is absolutely nothing we can do, we cannot even log to it, so this is a separate level of try without a catch
        // (we must release the lock so we need a finally anyway)
        await using var db = CreateDb();

        // We'd really like a date for any error message so let's create the provider first.
        TimeProvider? timeProvider = null;
        try
        {
            timeProvider = CreateTimeProvider();
            var fileStorage = CreateFileStorage();
            var dependencies = await SystemDependencies.CreateAsync(db, fileStorage, c => CreateEmailSender(baseUri, c), timeProvider);

            await function(dependencies, async message =>
            {
                // First, ensure read-only functions are truly read-only.
                if (isReadOnly)
                {
                    db.EnsureNoChanges();
                }
                // Second, log the resulting message if needed.
                // We're OK with read-only functions returning a message and thus not being truly read-only from a DB perspective,
                // because audit messages are append-only, and it's important to be able to log errors.
                if (message is not null)
                {
                    db.AuditMessages.Add(message);
                }
                // Finally, now that every necessary DB change has been made, commit.
                await db.CommitAsync();
            });
        }
        catch (Exception ex) when (ShouldLogException(ex))
        {
            try
            {
                db.CancelChanges();
                var message = new AuditMessage(Status.SystemError, $"```{Environment.NewLine}{ex}{Environment.NewLine}```", null, "System", timeProvider?.GetUtcNow() ?? DateTimeOffset.MinValue);
                db.AuditMessages.Add(message);
                await db.CommitAsync();
            }
            catch
            {
                // All hope is lost, we cannot even log to the DB
                db.CancelChanges();
            }
            throw new InvalidOperationException("Error while executing a request", ex);
        }
    }

    private static async Task<User?> FindUserAsync(Db database, Type userType, string id)
    {
        if (userType == typeof(Admin))
        {
            return await database.Admins.FindAsync(id);
        }
        if (userType == typeof(ChallengeSetter))
        {
            return await database.ChallengeSetters.FindAsync(id);
        }
        return await database.Participants.FindAsync(id);
    }
}